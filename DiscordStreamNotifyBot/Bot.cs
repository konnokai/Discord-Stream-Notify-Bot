using Discord.Commands;
using Discord.Interactions;
using DiscordStreamNotifyBot.Command;
using DiscordStreamNotifyBot.DataBase;
using DiscordStreamNotifyBot.DataBase.Table;
using DiscordStreamNotifyBot.HttpClients;
using DiscordStreamNotifyBot.Interaction;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using System.Reflection;

namespace DiscordStreamNotifyBot
{
    public class Bot
    {
        public static Stopwatch StopWatch { get; private set; } = new Stopwatch();
        public static ConnectionMultiplexer Redis { get; set; }
        public static ISubscriber RedisSub { get; set; }
        public static IDatabase RedisDb { get; set; }
        public static MainDbService DbService { get; private set; }

        public static IUser ApplicatonOwner { get; private set; } = null;
        public static BotPlayingStatus Status { get; set; } = BotPlayingStatus.Guild;

        public static bool IsConnect { get; set; } = false;
        public static bool IsDisconnect { get; set; } = false;
        public static bool IsHoloChannelSpider { get; set; } = false;
        public static bool IsNijisanjiChannelSpider { get; set; } = false;
        public static bool IsOtherChannelSpider { get; set; } = false;

        private static DiscordSocketClient client;
        private static Timer timerUpdateStatus;

        public enum BotPlayingStatus { Guild, Member, Stream, StreamCount, Info }

        private readonly static BotConfig _botConfig = new();
        private readonly int _shardId;
        private readonly int _totalShardCount;

        public Bot(int shardId, int totalShardCount)
        {
            _shardId = shardId;
            _totalShardCount = totalShardCount;

            _botConfig.InitBotConfig();
            DbService = new MainDbService(_botConfig.MySqlConnectionString);
            timerUpdateStatus = new Timer(TimerHandler);

            Log.Info($"Shard {_shardId} / {_totalShardCount} 正在初始化...");

            try
            {
                RedisConnection.Init(_botConfig.RedisOption);
                Redis = RedisConnection.Instance.ConnectionMultiplexer;
                RedisSub = Redis.GetSubscriber();
                RedisDb = Redis.GetDatabase();

                Log.Info("Redis已連線");

                if (RedisSub.Publish(new RedisChannel("youtube.test", RedisChannel.PatternMode.Literal), "nope") != 0)
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

            if (_shardId == 0)
            {
                using (var db = DbService.GetDbContext())
                    db.Database.EnsureCreated();
            }
        }

        public async Task StartAndBlockAsync()
        {
            client = new DiscordSocketClient(new DiscordSocketConfig()
            {
                ShardId = _shardId,
                TotalShards = _totalShardCount,
                LogLevel = LogSeverity.Verbose,
                ConnectionTimeout = int.MaxValue,
                MessageCacheSize = 0,
                // 因為沒有註冊事件，Discord .NET 建議可移除這兩個沒用到的特權
                // https://dotblogs.com.tw/yc421206/2015/10/20/c_scharp_enum_of_flags
                GatewayIntents = GatewayIntents.AllUnprivileged & ~GatewayIntents.GuildInvites & ~GatewayIntents.GuildScheduledEvents,
                AlwaysDownloadDefaultStickers = false,
                AlwaysResolveStickers = false,
                FormatUsersInBidirectionalUnicode = false,
                LogGatewayIntentWarnings = false,
            });

            #region 初始化Discord設定與事件
            client.Log += Log.LogMsg;

            client.Ready += async () =>
            {
                StopWatch.Start();
                timerUpdateStatus.Change(0, 15 * 60 * 1000);

                ApplicatonOwner = (await client.GetApplicationInfoAsync()).Owner;
                IsConnect = true;

                using (var db = DbService.GetDbContext())
                {
                    foreach (var guild in client.Guilds)
                    {
                        if (!await db.GuildConfig.AnyAsync(x => x.GuildId == guild.Id))
                        {
                            db.GuildConfig.Add(new GuildConfig() { GuildId = guild.Id });
                            await db.SaveChangesAsync();
                        }
                    }
                }
            };

            client.LeftGuild += (guild) =>
            {
                try
                {
                    Log.Info($"離開伺服器: {guild.Name}");

                    using (var db = DbService.GetDbContext())
                    {
                        GuildConfig guildConfig;
                        if ((guildConfig = db.GuildConfig.FirstOrDefault(x => x.GuildId == guild.Id)) != null)
                            db.GuildConfig.Remove(guildConfig);

                        IEnumerable<GuildYoutubeMemberConfig> guildYoutubeMemberConfigs;
                        if ((guildYoutubeMemberConfigs = db.GuildYoutubeMemberConfig.Where(x => x.GuildId == guild.Id)).Any())
                            db.GuildYoutubeMemberConfig.RemoveRange(guildYoutubeMemberConfigs);

                        IEnumerable<BannerChange> bannerChange;
                        if ((bannerChange = db.BannerChange.Where(x => x.GuildId == guild.Id)).Any())
                            db.BannerChange.RemoveRange(bannerChange);

                        IEnumerable<NoticeTwitterSpaceChannel> noticeTwitterSpaceChannels;
                        if ((noticeTwitterSpaceChannels = db.NoticeTwitterSpaceChannel.Where(x => x.GuildId == guild.Id)).Any())
                            db.NoticeTwitterSpaceChannel.RemoveRange(noticeTwitterSpaceChannels);

                        IEnumerable<NoticeTwitcastingStreamChannel> noticeTwitCastingStreamChannels;
                        if ((noticeTwitCastingStreamChannels = db.NoticeTwitcastingStreamChannels.Where(x => x.GuildId == guild.Id)).Any())
                            db.NoticeTwitcastingStreamChannels.RemoveRange(noticeTwitCastingStreamChannels);

                        IEnumerable<NoticeTwitchStreamChannel> NoticeTwitchStreamChannels;
                        if ((NoticeTwitchStreamChannels = db.NoticeTwitchStreamChannels.Where(x => x.GuildId == guild.Id)).Any())
                            db.NoticeTwitchStreamChannels.RemoveRange(NoticeTwitchStreamChannels);

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
                    Log.Error(ex.Demystify(), $"LeftGuild-{guild}");
                }
                return Task.CompletedTask;
            };
            #endregion

#if DEBUG || RELEASE
            Log.Info("登入中...");

            try
            {
                await client.LoginAsync(TokenType.Bot, _botConfig.DiscordToken);
                await client.StartAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "Discord 登入失敗!");
                return;
            }

            do { await Task.Delay(200); }
            while (!IsConnect);

            Log.Info("登入成功!");

            UptimeKumaClient.Init(_botConfig.UptimeKumaPushUrl, client);
#endif

            #region 初始化指令系統
            var services = new ServiceCollection()
                .AddHttpClient()
                .AddSingleton(DbService)
                .AddSingleton<SharedService.Twitter.TwitterSpacesService>()
                .AddSingleton<SharedService.Twitch.TwitchService>()
                .AddSingleton<SharedService.Youtube.YoutubeStreamService>()
                .AddSingleton<SharedService.YoutubeMember.YoutubeMemberService>()
                .AddSingleton(client)
                .AddSingleton(_botConfig)
                .AddSingleton(new InteractionService(client, new InteractionServiceConfig()
                {
                    AutoServiceScopes = true,
                    UseCompiledLambda = true,
                    EnableAutocompleteHandlers = true,
                    DefaultRunMode = Discord.Interactions.RunMode.Async,
                    ExitOnMissingModalField = true
                }))
                .AddSingleton(new CommandService(new CommandServiceConfig()
                {
                    CaseSensitiveCommands = false,
                    DefaultRunMode = Discord.Commands.RunMode.Async
                }));

            //https://blog.darkthread.net/blog/polly/
            //HandleTransientHttpError 包含 5xx 及 408 錯誤
            services.AddHttpClient<DiscordWebhookClient>();
            services.AddHttpClient<TwitterClient>();
            services.AddHttpClient<TwitcastingClient>()
                .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .RetryAsync(3));

            services.LoadInteractionFrom(Assembly.GetAssembly(typeof(InteractionHandler)));
            services.LoadCommandFrom(Assembly.GetAssembly(typeof(CommandHandler)));

            IServiceProvider serviceProvider = services.BuildServiceProvider();
            await serviceProvider.GetService<InteractionHandler>().InitializeAsync();
            await serviceProvider.GetService<CommandHandler>().InitializeAsync();
            #endregion

            #region 註冊互動指令
            try
            {
                var commandCount = (await RedisDb.StringGetSetAsync("discord_stream_bot:command_count", serviceProvider.GetService<InteractionHandler>().CommandCount)).ToString();
                if (commandCount != serviceProvider.GetService<InteractionHandler>().CommandCount.ToString())
                {
                    InteractionService interactionService = serviceProvider.GetService<InteractionService>();
#if DEBUG
                    if (_botConfig.TestSlashCommandGuildId == 0 || client.GetGuild(_botConfig.TestSlashCommandGuildId) == null)
                        Log.Warn("未設定測試Slash指令的伺服器或伺服器不存在，略過");
                    else
                    {
                        try
                        {
                            var result = await interactionService.RegisterCommandsToGuildAsync(_botConfig.TestSlashCommandGuildId);
                            Log.Info($"已註冊指令 ({_botConfig.TestSlashCommandGuildId}) : {string.Join(", ", result.Select((x) => x.Name))}");

                            result = await interactionService.AddModulesToGuildAsync(_botConfig.TestSlashCommandGuildId, false, interactionService.Modules.Where((x) => x.DontAutoRegister).ToArray());
                            Log.Info($"已註冊指令 ({_botConfig.TestSlashCommandGuildId}) : {string.Join(", ", result.Select((x) => x.Name))}");
                        }
                        catch (Exception ex)
                        {
                            Log.Error("註冊伺服器專用Slash指令失敗");
                            Log.Error(ex.ToString());
                        }
                    }
#elif RELEASE
                    try
                    {
                        if (_botConfig.TestSlashCommandGuildId != 0 && client.GetGuild(_botConfig.TestSlashCommandGuildId) != null)
                        {
                            var result = await interactionService.RemoveModulesFromGuildAsync(_botConfig.TestSlashCommandGuildId, interactionService.Modules.Where((x) => !x.DontAutoRegister).ToArray());
                            Log.Info($"({_botConfig.TestSlashCommandGuildId}) 已移除測試指令，剩餘指令: {string.Join(", ", result.Select((x) => x.Name))}");
                        }
                        try
                        {
                            foreach (var item in interactionService.Modules.Where((x) => x.Preconditions.Any((x) => x is Interaction.Attribute.RequireGuildAttribute)))
                            {
                                var guildId = ((Interaction.Attribute.RequireGuildAttribute)item.Preconditions.Single((x) => x is Interaction.Attribute.RequireGuildAttribute)).GuildId;
                                var guild = client.GetGuild(guildId.Value);

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
                    }
                    catch (Exception ex)
                    {
                        Log.Error("取得指令數量失敗，請確認Redis伺服器是否可以存取");
                        Log.Error(ex.Message);
                        IsDisconnect = true;
                    }
#endif
                }
            }
            catch (Exception ex)
            {
                Log.Error("註冊Slash指令失敗，關閉中...");
                Log.Error(ex.ToString());
                IsDisconnect = true;
            }
            #endregion

            // 因為會用到 DiscordWebhookClient Service，所以沒辦法往上移動到 Region 內
            client.JoinedGuild += (guild) =>
            {
                Log.Info($"加入伺服器: {guild.Name}");

                using (var db = DbService.GetDbContext())
                {
                    if (!db.GuildConfig.Any(x => x.GuildId == guild.Id))
                    {
                        db.GuildConfig.Add(new GuildConfig() { GuildId = guild.Id });
                        db.SaveChanges();
                    }
                }

                serviceProvider.GetService<DiscordWebhookClient>().SendMessageToDiscord($"加入 {guild.Name}({guild.Id})\n擁有者: {guild.OwnerId}");
                return Task.CompletedTask;
            };

            Log.Info("已初始化完成!");

            do { await Task.Delay(1000); }
            while (!IsDisconnect);

            while (IsHoloChannelSpider || IsOtherChannelSpider)
            {
                List<string> str = new List<string>();

                if (IsHoloChannelSpider) str.Add("Holo");
                if (IsOtherChannelSpider) str.Add("Other");

                Log.Info($"等待 {string.Join(", ", str)} 完成");
                await Task.Delay(5000);
            }

            await client.StopAsync();

            Redis.GetSubscriber().UnsubscribeAll();
            SharedService.Youtube.YoutubeStreamService.SaveDateBase();
        }

        private void TimerHandler(object state)
        {
            if (IsDisconnect) return;

            ChangeStatus();
        }

        public void ChangeStatus()
        {
            Task.Run(async () =>
            {
                switch (Status)
                {
                    case BotPlayingStatus.Guild:
                        await client.SetCustomStatusAsync($"在 {client.Guilds.Count} 個伺服器");
                        Status = BotPlayingStatus.Member;
                        break;
                    case BotPlayingStatus.Member:
                        try
                        {
                            await client.SetCustomStatusAsync($"服務 {client.Guilds.Sum((x) => x.MemberCount)} 個成員");
                            Status = BotPlayingStatus.Info;
                        }
                        catch (Exception) { Status = BotPlayingStatus.Stream; ChangeStatus(); }
                        break;
                    case BotPlayingStatus.Stream:
                        Status = BotPlayingStatus.StreamCount;
                        try
                        {
                            using var db = DbService.GetDbContext();

                            List<DataBase.Table.Video> list = null;
                            switch (new Random().Next(0, 2))
                            {
                                case 0:
                                    list = db.HoloVideos.AsNoTracking().Cast<DataBase.Table.Video>().ToList();
                                    break;
                                case 1:
                                    list = db.NijisanjiVideos.AsNoTracking().Cast<DataBase.Table.Video>().ToList();
                                    break;
                                case 2:
                                    list = db.OtherVideos.AsNoTracking().Cast<DataBase.Table.Video>().ToList();
                                    break;
                            }

                            var item = list[new Random().Next(0, list.Count)];
                            await client.SetGameAsync(item.VideoTitle, $"https://www.youtube.com/watch?v={item.VideoId}", ActivityType.Streaming);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex.Demystify(), "ChangeStatus");
                            ChangeStatus();
                        }
                        break;
                    case BotPlayingStatus.StreamCount:
                        Status = BotPlayingStatus.Info;
                        await client.SetCustomStatusAsync($"看了 {Utility.GetDbStreamCount()} 個直播");
                        break;
                    case BotPlayingStatus.Info:
                        await client.SetCustomStatusAsync("去看你的直播啦");
                        Status = BotPlayingStatus.Guild;
                        break;
                }
            });
        }

    }
}
