using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord_Stream_Notify_Bot.Command;
using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot
{
    class Program
    {
        public const string VERSION = "V1.0.2";
        public static ConnectionMultiplexer Redis { get; set; }
        public static ISubscriber RedisSub { get; set; }
        public static IDatabase RedisDb { get; set; }

        public static IUser ApplicatonOwner { get; private set; } = null;
        public static DiscordSocketClient _client;
        public static UpdateStatus updateStatus = UpdateStatus.Guild;
        public static Stopwatch stopWatch = new Stopwatch();
        public static bool isConnect = false, isDisconnect = false;
        static Timer timerUpdateStatus;
        static BotConfig botConfig = new();

        public enum UpdateStatus { Guild, Member, Stream, Info }

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

            using (var db = new DBContext())
            {
                db.Database.EnsureCreated();                
            }

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

                void SetKey(string key) {
                    if (!RedisDb.KeyExists($"streambot.save.schedule.{key}")) RedisDb.StringSet($"streambot.save.schedule.{key}", "[]");
                }                

                SetKey("holo"); SetKey("nijisanji"); SetKey("other");
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
#if DEBUG
                LogLevel = LogSeverity.Verbose,
#else
                LogLevel = LogSeverity.Critical,
#endif
                ConnectionTimeout = int.MaxValue,
                MessageCacheSize = 50,
                ExclusiveBulkDelete = true,
                AlwaysDownloadUsers = true
            }); ;

            _client.Log += Log.LogMsg;

            _client.Ready += () =>
            {
                using (var db = new DBContext())
                {
                    foreach (var guild in _client.Guilds)
                    {
                        if (!db.GuildConfig.Any(x => x.GuildId == guild.Id))
                        {
                            db.GuildConfig.Add(new GuildConfig() { GuildId = guild.Id });
                        }
                    }

                    db.SaveChanges();
                }

                stopWatch.Start();
                timerUpdateStatus.Change(0, 15 * 60 * 1000);

                ApplicatonOwner = _client.GetApplicationInfoAsync().GetAwaiter().GetResult().Owner;
                isConnect = true;

                return Task.CompletedTask;
            };

            _client.JoinedGuild += async (guild) =>
            {
                using (var db = new DBContext())
                {
                    if (!db.GuildConfig.Any(x => x.GuildId == guild.Id))
                    {
                        db.GuildConfig.Add(new GuildConfig() { GuildId = guild.Id });
                        await db.SaveChangesAsync().ConfigureAwait(false);
                    }
                }

                SendMessageToDiscord($"加入 {guild.Name}({guild.Id})\n擁有者: {guild.Owner.Username}({guild.Owner.Mention})");
            };

            #region 初始化指令系統
            var s = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(botConfig)
                .AddSingleton(new CommandService(new CommandServiceConfig()
                {
                    CaseSensitiveCommands = false,
                    DefaultRunMode = RunMode.Async
                }));

            s.LoadFrom(Assembly.GetAssembly(typeof(CommandHandler)));
            IServiceProvider service = s.BuildServiceProvider();
            await service.GetService<CommandHandler>().InitializeAsync();
            #endregion

            #region Login
            await _client.LoginAsync(TokenType.Bot, botConfig.DiscordToken);
            #endregion

            await _client.StartAsync();

            Log.Info("已初始化完成!");

            do { await Task.Delay(1000); }
            while (!isDisconnect);

            await _client.StopAsync();
            await Command.Stream.Service.StreamService.SaveDateBase();
        }

        private static void TimerHandler(object state)
        {
            if (isDisconnect) return;

            ChangeStatus();
        }

        public static void ChangeStatus()
        {
            Action<string> setGame = new Action<string>((string text) => { _client.SetGameAsync($"s!h | {text}"); });

            switch (updateStatus)
            {
                case UpdateStatus.Guild:
                    setGame($"在 {_client.Guilds.Count} 個伺服器");
                    updateStatus = UpdateStatus.Member;
                    break;
                case UpdateStatus.Member:
                    try
                    {
                        setGame($"服務 {_client.Guilds.Sum((x) => x.MemberCount)} 個成員");
                        updateStatus = UpdateStatus.Info;
                    }
                    catch (Exception) { updateStatus = UpdateStatus.Stream; ChangeStatus(); }
                    break;
                case UpdateStatus.Stream:
                    updateStatus = UpdateStatus.Info;
                    try
                    {
                        using (var uow = new DBContext())
                        {
                            List<StreamVideo> list = null;
                            switch (new Random().Next(0, 2))
                            {
                                case 0:
                                    list = Queryable.Select(uow.HoloStreamVideo, (x) => (StreamVideo)x).ToList();
                                    break;
                                case 1:
                                    list = Queryable.Select(uow.NijisanjiStreamVideo, (x) => (StreamVideo)x).ToList();
                                    break;
                                case 2:
                                    list = Queryable.Select(uow.OtherStreamVideo, (x) => (StreamVideo)x).ToList();
                                    break;
                            }
                            var item = list[new Random().Next(0, list.Count)];
                            setGame($"{item.VideoId} - {item.VideoTitle}\n{item.ChannelTitle}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("ChangeStatus");
                        Log.Error(ex.Message);
                        ChangeStatus();
                    }
                    break;
                case UpdateStatus.Info:
                    setGame("去看你的直播啦");
                    updateStatus = UpdateStatus.Guild;
                    break;
            }
        }

        public static string GetDataFilePath(string fileName)
        {
            return AppDomain.CurrentDomain.BaseDirectory + "Data" +
                (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\\" : "/") + fileName;
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            isDisconnect = true;
            e.Cancel = true;
        }

        public static void SendMessageToDiscord(string content)
        {
            Message message = new Message();

            if (isConnect) message.username = _client.CurrentUser.Username;
            else message.username = "Bot";

            if (isConnect) message.avatar_url = _client.CurrentUser.GetAvatarUrl();
            else message.avatar_url = "";

            message.content = content;

            using (WebClient webClient = new WebClient())
            {
                webClient.Encoding = System.Text.Encoding.UTF8;
                webClient.Headers["Content-Type"] = "application/json";
                webClient.UploadString(botConfig.WebHookUrl, JsonConvert.SerializeObject(message));
            }
        }

        public class Message
        {
            public string username { get; set; }
            public string content { get; set; }
            public string avatar_url { get; set; }
        }
    }
}
