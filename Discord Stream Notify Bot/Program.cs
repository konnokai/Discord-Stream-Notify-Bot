global using Google.Apis.YouTube.v3.Data;

using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Discord_Stream_Notify_Bot.Command;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.HttpClients;
using Discord_Stream_Notify_Bot.Interaction;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot
{
    public class Program
    {
        public static string VERSION => GetLinkerTime(Assembly.GetEntryAssembly());
        public static ConnectionMultiplexer Redis { get; set; }
        public static ISubscriber RedisSub { get; set; }
        public static IDatabase RedisDb { get; set; }
        
        public static IUser ApplicatonOwner { get; private set; } = null;
        public static DiscordSocketClient _client;
        public static BotPlayingStatus Status = BotPlayingStatus.Guild;
        public static Stopwatch stopWatch = new Stopwatch();
        public static bool isConnect = false, isDisconnect = false, isNeedRegisterAppCommand = false;
        public static bool isHoloChannelSpider = false, isNijisanjiChannelSpider = false, isOtherChannelSpider = false;
        static Timer timerUpdateStatus;
        static BotConfig botConfig = new();

        public enum BotPlayingStatus { Guild, Member, Stream, StreamCount, Info }

        static void Main(string[] args)
        {
            stopWatch.Start();

            Log.Info(VERSION + " 初始化中");
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.CancelKeyPress += Console_CancelKeyPress;

            botConfig.InitBotConfig();
            timerUpdateStatus = new Timer(TimerHandler);

            if (!Directory.Exists(Path.GetDirectoryName(GetDataFilePath(""))))
                Directory.CreateDirectory(Path.GetDirectoryName(GetDataFilePath("")));

            using (var db = DataBase.DBContext.GetDbContext())
                db.Database.EnsureCreated();
            using (var db = DataBase.HoloVideoContext.GetDbContext())
                db.Database.EnsureCreated();
            using (var db = DataBase.NijisanjiVideoContext.GetDbContext())
                db.Database.EnsureCreated();
            using (var db = DataBase.OtherVideoContext.GetDbContext())
                db.Database.EnsureCreated();
            using (var db = DataBase.NotVTuberVideoContext.GetDbContext())
                db.Database.EnsureCreated();

            try
            {
                RedisConnection.Init(botConfig.RedisOption);
                Redis = RedisConnection.Instance.ConnectionMultiplexer;
                RedisSub = Redis.GetSubscriber();
                RedisDb = Redis.GetDatabase();

                Log.Info("Redis已連線");

                if (RedisSub.Publish("youtube.test", "nope") != 0)
                {
                    Log.Info("Redis Sub已存在");
                }
                else
                {
                    Log.Warn("Redis Sub不存在，請開啟錄影工具");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Redis連線錯誤，請確認伺服器是否已開啟");
                Log.Error(ex.Message);
                return;
            }

            new Program().MainAsync().GetAwaiter().GetResult();

            Redis.GetSubscriber().UnsubscribeAll();
        }

        public async Task MainAsync()
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose,
                ConnectionTimeout = int.MaxValue,
                MessageCacheSize = 50,
                GatewayIntents = GatewayIntents.AllUnprivileged
            }); ;

            #region 初始化互動指令系統
            var interactionServices = new ServiceCollection()
                .AddHttpClient()
                .AddSingleton<SharedService.Twitter.TwitterSpacesService>()
                .AddSingleton<SharedService.Youtube.YoutubeStreamService>()
                .AddSingleton<SharedService.YoutubeMember.YoutubeMemberService>()
                .AddSingleton(_client)
                .AddSingleton(botConfig)
                .AddSingleton(new InteractionService(_client, new InteractionServiceConfig()
                {
                    AutoServiceScopes = true,
                    UseCompiledLambda = true,
                    EnableAutocompleteHandlers = true,
                    DefaultRunMode = Discord.Interactions.RunMode.Async,
                    ExitOnMissingModalField = true,
                }));

            //https://blog.darkthread.net/blog/polly/
            //HandleTransientHttpError 包含 5xx 及 408 錯誤
            interactionServices.AddHttpClient<HttpClients.DiscordWebhookClient>();
            interactionServices.AddHttpClient<HttpClients.TwitterClient>()
                .AddPolicyHandler(HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .RetryAsync(3));

            interactionServices.LoadInteractionFrom(Assembly.GetAssembly(typeof(InteractionHandler)));
            IServiceProvider iService = interactionServices.BuildServiceProvider();
            await iService.GetService<InteractionHandler>().InitializeAsync();
            #endregion

            #region 初始化一般指令系統
            var commandServices = new ServiceCollection()
                .AddHttpClient()
                .AddSingleton(iService.GetService<SharedService.Twitter.TwitterSpacesService>())
                .AddSingleton(iService.GetService<SharedService.Youtube.YoutubeStreamService>())
                .AddSingleton(iService.GetService<SharedService.YoutubeMember.YoutubeMemberService>())
                .AddSingleton(_client)
                .AddSingleton(botConfig)
                .AddSingleton(new CommandService(new CommandServiceConfig()
                {
                    CaseSensitiveCommands = false,
                    DefaultRunMode = Discord.Commands.RunMode.Async
                }));

            commandServices.AddHttpClient<HttpClients.DiscordWebhookClient>();
            commandServices.AddHttpClient<HttpClients.TwitterClient>()
                .AddPolicyHandler(HttpPolicyExtensions
                    .HandleTransientHttpError()
                    .RetryAsync(3));

            commandServices.LoadCommandFrom(Assembly.GetAssembly(typeof(CommandHandler)));
            IServiceProvider service = commandServices.BuildServiceProvider();
            await service.GetService<CommandHandler>().InitializeAsync();
            #endregion

            #region 初始化Discord設定與事件
            _client.Log += Log.LogMsg;

            _client.Ready += async () =>
            {
                stopWatch.Start();
                timerUpdateStatus.Change(0, 15 * 60 * 1000);

                UptimeKumaClient.Init(botConfig.UptimeKumaPushUrl, _client);

                ApplicatonOwner = (await _client.GetApplicationInfoAsync()).Owner;
                isConnect = true;

                using (var db = DataBase.DBContext.GetDbContext())
                {
                    foreach (var guild in _client.Guilds)
                    {
                        if (!db.GuildConfig.Any(x => x.GuildId == guild.Id))
                        {
                            db.GuildConfig.Add(new GuildConfig() { GuildId = guild.Id });
                            db.SaveChanges();
                        }
                    }
                }

                #region 註冊互動指令
                try
                {
                    InteractionService interactionService = iService.GetService<InteractionService>();
#if DEBUG
                    if (botConfig.TestSlashCommandGuildId == 0 || _client.GetGuild(botConfig.TestSlashCommandGuildId) == null)
                        Log.Warn("未設定測試Slash指令的伺服器或伺服器不存在，略過");
                    else
                    {
                        try
                        {
                            var result = await interactionService.RegisterCommandsToGuildAsync(botConfig.TestSlashCommandGuildId);
                            Log.Info($"已註冊指令 ({botConfig.TestSlashCommandGuildId}) : {string.Join(", ", result.Select((x) => x.Name))}");

                            result = await interactionService.AddModulesToGuildAsync(botConfig.TestSlashCommandGuildId, false, interactionService.Modules.Where((x) => x.DontAutoRegister).ToArray());
                            Log.Info($"已註冊指令 ({botConfig.TestSlashCommandGuildId}) : {string.Join(", ", result.Select((x) => x.Name))}");
                        }
                        catch (Exception ex)
                        {
                            Log.Error("註冊伺服器專用Slash指令失敗");
                            Log.Error(ex.ToString());
                        }
                    }
#else
                    try
                    {
                        var commandCount = (await RedisDb.StringGetSetAsync("discord_stream_bot:command_count", iService.GetService<InteractionHandler>().CommandCount)).ToString();
                        if (commandCount == iService.GetService<InteractionHandler>().CommandCount.ToString()) return;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("取得指令數量失敗，請確認Redis伺服器是否可以存取");
                        Log.Error(ex.Message);
                        isDisconnect = true;
                        return;
                    }

                    try
                    {
                        foreach (var item in interactionService.Modules.Where((x) => x.Preconditions.Any((x) => x is Interaction.Attribute.RequireGuildAttribute)))
                        {
                            var guildId = ((Interaction.Attribute.RequireGuildAttribute)item.Preconditions.FirstOrDefault((x) => x is Interaction.Attribute.RequireGuildAttribute)).GuildId;                          
                            var guild = _client.GetGuild(guildId.Value);

                            if (guild == null)
                            {
                                Log.Warn($"{item.Name} 註冊失敗，伺服器 {guildId} 不存在");
                                continue;
                            }

                            var result = await interactionService.AddModulesToGuildAsync(guild, false, item);
                            Log.Info($"已在 {guild.Name}({guild.Id}) 註冊指令: {string.Join(", ", item.SlashCommands.Select((x) => x.Name))}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("註冊伺服器專用Slash指令失敗");
                        Log.Error(ex.ToString());
                    }

                    await interactionService.RegisterCommandsGloballyAsync();
                    Log.Info("已註冊全球指令");
#endif
                }
                catch (Exception ex)
                {
                    Log.Error("註冊Slash指令失敗，關閉中...");
                    Log.Error(ex.ToString());
                    isDisconnect = true;
                }
                #endregion
            };

            _client.JoinedGuild += (guild) =>
            {
                using (var db = DataBase.DBContext.GetDbContext())
                {
                    if (!db.GuildConfig.Any(x => x.GuildId == guild.Id))
                    {
                        db.GuildConfig.Add(new GuildConfig() { GuildId = guild.Id });
                        db.SaveChanges();
                    }
                }

                iService.GetService<DiscordWebhookClient>().SendMessageToDiscord($"加入 {guild.Name}({guild.Id})\n擁有者: {guild.OwnerId}");
                return Task.CompletedTask;
            };

            _client.LeftGuild += (guild) =>
            {
                try
                {
                    using (var db = DataBase.DBContext.GetDbContext())
                    {
                        GuildConfig guildConfig;
                        if (( guildConfig = db.GuildConfig.FirstOrDefault(x => x.GuildId == guild.Id)) != null)
                            db.GuildConfig.Remove(guildConfig);

                        GuildYoutubeMemberConfig guildYoutubeMemberConfig;
                        if ((guildYoutubeMemberConfig = db.GuildYoutubeMemberConfig.FirstOrDefault(x => x.GuildId == guild.Id)) != null)                        
                            db.GuildYoutubeMemberConfig.Remove(guildYoutubeMemberConfig);

                        IEnumerable<NoticeTwitterSpaceChannel> noticeTwitterSpaceChannels;
                        if ((noticeTwitterSpaceChannels = db.NoticeTwitterSpaceChannel.Where(x => x.GuildId == guild.Id)).Any())                        
                            db.NoticeTwitterSpaceChannel.RemoveRange(noticeTwitterSpaceChannels);

                        IEnumerable<NoticeYoutubeStreamChannel> noticeYoutubeStreamChannels;
                        if ((noticeYoutubeStreamChannels = db.NoticeYoutubeStreamChannel.Where(x => x.GuildId == guild.Id)).Any())
                            db.NoticeYoutubeStreamChannel.RemoveRange(noticeYoutubeStreamChannels);

                        IEnumerable<YoutubeMemberCheck> youtubeMemberChecks;
                        if ((youtubeMemberChecks = db.YoutubeMemberCheck.Where(x => x.GuildId == guild.Id)).Any())
                            db.YoutubeMemberCheck.RemoveRange(youtubeMemberChecks);

                        var saveTime = DateTime.Now;
                        bool saveFailed;

                        do
                        {
                            saveFailed = false;
                            try
                            {
                                db.SaveChanges();
                            }
                            catch (DbUpdateConcurrencyException ex)
                            {
                                saveFailed = true;
                                foreach (var item in ex.Entries)
                                {
                                    try
                                    {
                                        item.Reload();
                                    }
                                    catch (Exception ex2)
                                    {
                                        Log.Error($"LeftGuild-SaveChanges-Reload-{guild}");
                                        Log.Error(item.DebugView.ToString());
                                        Log.Error(ex2.ToString());
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"LeftGuild-SaveChanges-{guild}: {ex}");
                                Log.Error(db.ChangeTracker.DebugView.LongView);
                            }
                        } while (saveFailed && DateTime.Now.Subtract(saveTime) <= TimeSpan.FromMinutes(1));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"LeftGuild-{guild}: {ex}");
                }
                return Task.CompletedTask;
            };
            #endregion

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Exception e = (Exception)args.ExceptionObject;
                iService.GetService<HttpClients.DiscordWebhookClient>().SendMessageToDiscord($"{ApplicatonOwner.Mention} {e}");                
            };

            await _client.LoginAsync(TokenType.Bot, botConfig.DiscordToken);
            await _client.StartAsync();

            Log.Info("已初始化完成!");

            do { await Task.Delay(1000); }
            while (!isDisconnect);

            while (isHoloChannelSpider || isNijisanjiChannelSpider || isOtherChannelSpider)
            {
                List<string> str = new List<string>();

                if (isHoloChannelSpider) str.Add("Holo");
                if (isNijisanjiChannelSpider) str.Add("Nijisanji");
                if (isOtherChannelSpider) str.Add("Other");

                Log.Info($"等待 {string.Join(", ", str)} 完成");
                await Task.Delay(5000);
            }

            await _client.StopAsync();
            SharedService.Youtube.YoutubeStreamService.SaveDateBase();
        }

        private static void TimerHandler(object state)
        {
            if (isDisconnect) return;

            ChangeStatus();
        }

        public static void ChangeStatus()
        {
            Action<string> setGame = new Action<string>(async (text) =>
            {
                try
                {
                    await _client.SetGameAsync($"s!h | {text}", type: ActivityType.Playing);
                }
                catch (Exception) { }
            });

            switch (Status)
            {
                case BotPlayingStatus.Guild:
                    setGame($"在 {_client.Guilds.Count} 個伺服器");
                    Status = BotPlayingStatus.Member;
                    break;
                case BotPlayingStatus.Member:
                    try
                    {
                        setGame($"服務 {_client.Guilds.Sum((x) => x.MemberCount)} 個成員");
                        Status = BotPlayingStatus.Info;
                    }
                    catch (Exception) { Status = BotPlayingStatus.Stream; ChangeStatus(); }
                    break;
                case BotPlayingStatus.Stream:
                    Status = BotPlayingStatus.StreamCount;
                    try
                    {
                        List<DataBase.Table.Video> list = null;
                        switch (new Random().Next(0, 2))
                        {
                            case 0:
                                using (var db = DataBase.HoloVideoContext.GetDbContext())
                                    list = db.Video.ToList();
                                break;
                            case 1:
                                using (var db = DataBase.NijisanjiVideoContext.GetDbContext())
                                    list = db.Video.ToList();
                                break;
                            case 2:
                                using (var db = DataBase.OtherVideoContext.GetDbContext())
                                    list = db.Video.ToList();
                                break;
                        }
                        var item = list[new Random().Next(0, list.Count)];
                        _client.SetGameAsync(item.VideoTitle, $"https://www.youtube.com/watch?v={item.VideoId}", ActivityType.Streaming);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("ChangeStatus");
                        Log.Error(ex.Message);
                        ChangeStatus();
                    }
                    break;
                case BotPlayingStatus.StreamCount:
                    Status = BotPlayingStatus.Info;
                    setGame($"看了 {Utility.GetDbStreamCount()} 個直播");
                    break;
                case BotPlayingStatus.Info:
                    setGame("去看你的直播啦");
                    Status = BotPlayingStatus.Guild;
                    break;
            }
        }

        public static string GetDataFilePath(string fileName)
            => $"{AppDomain.CurrentDomain.BaseDirectory}Data{GetPlatformSlash()}{fileName}";        

        public static string GetPlatformSlash()
            => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\\" : "/";

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            isDisconnect = true;
            e.Cancel = true;
        }

        public static string GetLinkerTime(Assembly assembly)
        {
            const string BuildVersionMetadataPrefix = "+build";

            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attribute?.InformationalVersion != null)
            {
                var value = attribute.InformationalVersion;
                var index = value.IndexOf(BuildVersionMetadataPrefix);
                if (index > 0)
                {
                    value = value[(index + BuildVersionMetadataPrefix.Length)..];
                    return value;
                }
            }
            return default;
        }
    }
}
