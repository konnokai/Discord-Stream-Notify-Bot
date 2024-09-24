using Discord.Interactions;
using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using TwitchLib.Api;
using Stream = TwitchLib.Api.Helix.Models.Streams.GetStreams.Stream;
using User = TwitchLib.Api.Helix.Models.Users.GetUsers.User;

#if RELEASE
using Polly;
#endif

namespace Discord_Stream_Notify_Bot.SharedService.Twitch
{
    public class TwitchService : IInteractionService
    {
        public bool IsEnable { get; private set; } = true;

        public enum NoticeType
        {
            [ChoiceDisplay("開始直播")]
            StartStream,
            [ChoiceDisplay("結束直播")]
            EndStream,
            [ChoiceDisplay("更改直播資料")]
            ChangeStreamData
        }

        private Regex UserLoginRegex { get; } = new(@"twitch.tv/(?<name>[\w\d\-_]+)/?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly EmojiService _emojiService;
        private readonly DiscordSocketClient _client;
        private readonly Lazy<TwitchAPI> _twitchApi;
        private readonly Timer _timer;
        private readonly HashSet<string> _hashSet = new();
        private readonly string _apiServerUrl;
        private readonly MessageComponent _messageComponent;

        private string twitchRecordPath, twitchOAuthToken, twitchWebHookSecret;
        private bool isRuning = false;

        public TwitchService(DiscordSocketClient client, BotConfig botConfig, EmojiService emojiService)
        {
            if (string.IsNullOrEmpty(botConfig.TwitchClientId) || string.IsNullOrEmpty(botConfig.TwitchClientSecret))
            {
                Log.Warn($"{nameof(botConfig.TwitchClientId)} 或 {nameof(botConfig.TwitchClientSecret)} 遺失，無法運行 Twitch 類功能");
                IsEnable = false;
                return;
            }

            try
            {
                twitchWebHookSecret = Program.RedisDb.StringGet("twitch:webhook_secret");
                if (string.IsNullOrEmpty(twitchWebHookSecret))
                {
                    Log.Warn("缺少 TwitchWebHookSecret，嘗試重新建立...");

                    twitchWebHookSecret = BotConfig.GenRandomKey(64);
                    Program.RedisDb.StringSet("twitch:webhook_secret", twitchWebHookSecret);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "獲取 TwitchWebHookSecret 失敗，無法運行 Twitch 類功能");
                IsEnable = false;
                return;
            }

            if (string.IsNullOrEmpty(botConfig.TwitchCookieAuthToken) || botConfig.TwitchCookieAuthToken.Length != 30)
            {
                Log.Warn($"{nameof(botConfig.TwitchCookieAuthToken)} 遺失或是字元非 30 字");
                Log.Warn($"請參考 https://streamlink.github.io/cli/plugins/twitch.html#authentication 後設定到 {nameof(botConfig.TwitchCookieAuthToken)}");
            }
            else
            {
                twitchOAuthToken = botConfig.TwitchCookieAuthToken;
            }

            _apiServerUrl = botConfig.ApiServerDomain;

            twitchRecordPath = botConfig.TwitchRecordPath;
            if (string.IsNullOrEmpty(twitchRecordPath)) twitchRecordPath = Program.GetDataFilePath("");
            if (!twitchRecordPath.EndsWith(Program.GetPlatformSlash())) twitchRecordPath += Program.GetPlatformSlash();

            _client = client;
            _emojiService = emojiService;

            _twitchApi = new(() => new()
            {
                Helix =
                {
                    Settings =
                    {
                        ClientId = botConfig.TwitchClientId,
                        Secret = botConfig.TwitchClientSecret
                    }
                }
            });

            _messageComponent = new ComponentBuilder()
                .WithButton("好手氣，隨機帶你到一個影片或直播", style: ButtonStyle.Link, emote: emojiService.YouTubeEmote, url: "https://api.konnokai.me/randomvideo")
                .WithButton("贊助小幫手 (Patreon) #ad", style: ButtonStyle.Link, emote: emojiService.PatreonEmote, url: Utility.PatreonUrl, row: 1)
                .WithButton("贊助小幫手 (Paypal) #ad", style: ButtonStyle.Link, emote: emojiService.PayPalEmote, url: Utility.PaypalUrl, row: 1).Build();

#nullable enable

            Program.RedisSub.Subscribe(new RedisChannel("twitch:stream_offline", RedisChannel.PatternMode.Literal), async (channel, streamData) =>
            {
                var data = JsonConvert.DeserializeObject<TwitchLib.EventSub.Core.SubscriptionTypes.Stream.StreamOffline>(streamData!)!;
                Log.Info($"Twitch 直播離線: {data.BroadcasterUserId}");

                try
                {
                    var list = await _twitchApi.Value.Helix.EventSub.GetEventSubSubscriptionsAsync(userId: data.BroadcasterUserId);
                    foreach (var item in list.Subscriptions)
                    {
                        Log.Info($"Delete EventSub: {item.Id} ({item.Type})");
                        await _twitchApi.Value.Helix.EventSub.DeleteEventSubSubscriptionAsync(item.Id);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Event Delete Error: {data.BroadcasterUserId}");
                }

                TwitchStream? twitchStream = null;
                try
                {
                    var redisJson = await Program.RedisDb.StringGetAsync(new RedisKey($"twitch:stream_data:{data.BroadcasterUserId}"));
                    if (redisJson.HasValue)
                        twitchStream = JsonConvert.DeserializeObject<TwitchStream>(redisJson!);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Twitch Get Redis Data Error: {data.BroadcasterUserId}");
                }

                using var db = MainDbContext.GetDbContext();
                var twitchSpider = db.TwitchSpider.AsNoTracking().FirstOrDefault((x) => x.UserId == data.BroadcasterUserId);

                var embedBuilder = new EmbedBuilder()
                    .WithErrorColor()
                    .WithTitle("(找不到標題)")
                    .WithUrl($"https://twitch.tv/{data.BroadcasterUserLogin}")
                    .WithDescription(Format.Url($"{data.BroadcasterUserName}", $"https://twitch.tv/{data.BroadcasterUserLogin}"))
                    .AddField("直播狀態", "已關台");

                if (twitchStream != null)
                {
                    embedBuilder
                        .WithTitle(twitchStream.StreamTitle)
                        .AddField("直播時間", $"{DateTime.UtcNow.Subtract(twitchStream.StreamStartAt):hh'時'mm'分'ss'秒'}");
                }

                embedBuilder.AddField("關台時間", DateTime.UtcNow.ConvertDateTimeToDiscordMarkdown());

                if (twitchSpider != null)
                {
                    embedBuilder.WithImageUrl(twitchSpider.OfflineImageUrl)
                        .WithThumbnailUrl(twitchSpider.ProfileImageUrl);
                }

                await Task.Run(() => SendStreamMessageAsync(data.BroadcasterUserId, embedBuilder.Build(), NoticeType.EndStream));
            });

            Program.RedisSub.Subscribe(new RedisChannel("twitch:channel_update", RedisChannel.PatternMode.Literal), async (channel, updateData) =>
            {
                var data = JsonConvert.DeserializeObject<TwitchLib.EventSub.Core.SubscriptionTypes.Channel.ChannelUpdate>(updateData!)!;
                Log.Info($"Twitch 頻道更新: {data.BroadcasterUserName} - {data.Title} ({data.CategoryName})");

                TwitchStream? twitchStream = null;
                try
                {
                    var redisJson = await Program.RedisDb.StringGetAsync(new RedisKey($"twitch:stream_data:{data.BroadcasterUserId}"));
                    if (redisJson.HasValue)
                        twitchStream = JsonConvert.DeserializeObject<TwitchStream>(redisJson!);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Twitch Get Redis Data Error: {data.BroadcasterUserId}");
                }

                var embedBuilder = new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle(data.Title)
                    .WithUrl($"https://twitch.tv/{data.BroadcasterUserLogin}")
                    .WithDescription(Format.Url($"{data.BroadcasterUserName}", $"https://twitch.tv/{data.BroadcasterUserLogin}"))
                    .AddField("直播狀態", "直播資料更新");

                if (!string.IsNullOrEmpty(data.CategoryName))
                    embedBuilder.AddField("分類", data.CategoryName);

                if (twitchStream != null)
                {
                    embedBuilder.AddField("更新的時間軸", $"{DateTime.UtcNow.Subtract(twitchStream.StreamStartAt):hh'時'mm'分'ss'秒'}");
                }

                try
                {
                    twitchStream = new TwitchStream()
                    {
                        StreamId = twitchStream?.StreamId,
                        StreamTitle = data.Title,
                        GameName = data.CategoryName,
                        ThumbnailUrl = twitchStream?.ThumbnailUrl,
                        UserId = data.BroadcasterUserId,
                        UserLogin = data.BroadcasterUserLogin,
                        UserName = data.BroadcasterUserName,
                        StreamStartAt = twitchStream?.StreamStartAt ?? DateTime.UtcNow
                    };

                    await Program.RedisDb.StringSetAsync(new($"twitch:stream_data:{data.BroadcasterUserId}"), JsonConvert.SerializeObject(twitchStream));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Twitch Channel Update Set Redis Data Error: {data.BroadcasterUserId}");
                }

                using var db = MainDbContext.GetDbContext();
                var twitchSpider = db.TwitchSpider.AsNoTracking().FirstOrDefault((x) => x.UserId == data.BroadcasterUserId);
                if (twitchSpider != null)
                    embedBuilder.WithThumbnailUrl(twitchSpider.ProfileImageUrl);

                await Task.Run(() => SendStreamMessageAsync(data.BroadcasterUserId, embedBuilder.Build(), NoticeType.ChangeStreamData));
            });

#nullable disable

            _timer = new Timer(async (obj) => { await TimerHandel(); },
            null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
        }

        public string GetUserLoginByUrl(string url)
        {
            url = url.Split('?')[0];

            var match = UserLoginRegex.Match(url);
            if (match.Success)
            {
                url = match.Groups["name"].Value;
            }

            return url;
        }

        public async Task<User> GetUserAsync(string twitchUserId = "", string twitchUserLogin = "")
        {
            List<string> userId = null, userLogin = null;
            if (!string.IsNullOrEmpty(twitchUserId))
                userId = new List<string> { twitchUserId };
            else if (!string.IsNullOrEmpty(twitchUserLogin))
                userLogin = new List<string> { twitchUserLogin };
            else throw new ArgumentException("兩者參數不可同時為空");

            try
            {
                string accessToken = await _twitchApi.Value.Auth.GetAccessTokenAsync();

                if (string.IsNullOrEmpty(accessToken))
                {
                    Log.Warn($"Access Token 獲取失敗，請確認 {nameof(BotConfig.TwitchClientId)} 或 {nameof(BotConfig.TwitchClientSecret)} 是否正常");
                    return null;
                }

                var users = await _twitchApi.Value.Helix.Users.GetUsersAsync(userId, userLogin, accessToken: accessToken);
                return users.Users.FirstOrDefault();
            }
            catch (TwitchLib.Api.Core.Exceptions.BadRequestException)
            {
                Log.Error($"無法取得 Twitch 資料，可能是找不到輸入的使用者資料: ({twitchUserId}) {twitchUserLogin}");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"無法取得 Twitch 資料: ({twitchUserId}) {twitchUserLogin}");
                return null;
            }
        }

        public async Task<IReadOnlyList<User>> GetUsersAsync(params string[] twitchUserLogins)
        {
            try
            {
                string accessToken = await _twitchApi.Value.Auth.GetAccessTokenAsync();

                if (string.IsNullOrEmpty(accessToken))
                {
                    Log.Warn($"Access Token 獲取失敗，請確認 {nameof(BotConfig.TwitchClientId)} 或 {nameof(BotConfig.TwitchClientSecret)} 是否正常");
                    return Array.Empty<User>();
                }

                List<User> result = new();
                foreach (var item in twitchUserLogins.Chunk(100))
                {
                    var users = await _twitchApi.Value.Helix.Users.GetUsersAsync(logins: new List<string>(twitchUserLogins), accessToken: accessToken);
                    if (users.Users.Any())
                    {
                        result.AddRange(users.Users);
                    }
                }

                return result;
            }
            catch (TwitchLib.Api.Core.Exceptions.BadRequestException)
            {
                Log.Error($"無法取得 Twitch 資料，可能是找不到輸入的使用者資料: {twitchUserLogins.First()}");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"無法取得 Twitch 資料: {twitchUserLogins.First()}");
                return null;
            }
        }

        public async Task<IReadOnlyList<Stream>> GetNowStreamsAsync(params string[] twitchUserIds)
        {
            try
            {
                string accessToken = await _twitchApi.Value.Auth.GetAccessTokenAsync();

                if (string.IsNullOrEmpty(accessToken))
                {
                    Log.Warn($"Access Token 獲取失敗，請確認 {nameof(BotConfig.TwitchClientId)} 或 {nameof(BotConfig.TwitchClientSecret)} 是否正常");
                    return Array.Empty<Stream>();
                }

                List<Stream> result = new();
                foreach (var item in twitchUserIds.Chunk(100))
                {
                    var streams = await _twitchApi.Value.Helix.Streams.GetStreamsAsync(first: 100, userIds: new List<string>(twitchUserIds), accessToken: accessToken);
                    if (streams.Streams.Any())
                    {
                        result.AddRange(streams.Streams);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"無法取得 Twitch 資料，請確認 {nameof(BotConfig.TwitchClientId)} 或 {nameof(BotConfig.TwitchClientSecret)} 是否正常");
                return Array.Empty<Stream>();
            }
        }

        private async Task TimerHandel()
        {
            if (isRuning) return;
            isRuning = true;

            try
            {
                using var twitchStreamDb = TwitchStreamContext.GetDbContext();
                using var db = MainDbContext.GetDbContext();

                foreach (var twitchSpiders in db.TwitchSpider.Distinct((x) => x.UserId).Chunk(100))
                {
                    var streams = await GetNowStreamsAsync(twitchSpiders.Select((x) => x.UserId).ToArray());
                    if (!streams.Any())
                        continue;

                    foreach (var stream in streams)
                    {
                        if (string.IsNullOrEmpty(stream.Id))
                            continue;

                        if (_hashSet.Contains(stream.Id))
                            continue;

                        _hashSet.Add(stream.Id);

                        if (twitchStreamDb.TwitchStreams.AsNoTracking().Any((x) => x.StreamId == stream.Id))
                            continue;

                        var twitchSpider = twitchSpiders.Single((x) => x.UserId == stream.UserId);
                        var userData = await GetUserAsync(twitchUserId: twitchSpider.UserId);
                        twitchSpider.OfflineImageUrl = userData.OfflineImageUrl;
                        twitchSpider.ProfileImageUrl = userData.ProfileImageUrl;
                        twitchSpider.UserName = userData.DisplayName;
                        db.TwitchSpider.Update(twitchSpider);

                        try
                        {
                            var twitchStream = new TwitchStream()
                            {
                                StreamId = stream.Id,
                                StreamTitle = stream.Title,
                                GameName = stream.GameName,
                                ThumbnailUrl = stream.ThumbnailUrl.Replace("{width}", "854").Replace("{height}", "480"),
                                UserId = stream.UserId,
                                UserLogin = stream.UserLogin,
                                UserName = stream.UserName,
                                StreamStartAt = stream.StartedAt
                            };

                            twitchStreamDb.TwitchStreams.Add(twitchStream);

                            EmbedBuilder embedBuilder = new EmbedBuilder()
                                .WithTitle(twitchStream.StreamTitle)
                                .WithDescription(Format.Url($"{twitchStream.UserName}", $"https://twitch.tv/{twitchStream.UserLogin}"))
                                .WithUrl($"https://twitch.tv/{twitchStream.UserLogin}")
                                .WithThumbnailUrl(twitchSpider.ProfileImageUrl)
                                .WithImageUrl($"{twitchStream.ThumbnailUrl}?t={DateTime.Now.ToFileTime()}") // 新增參數避免預覽圖被 Discord 快取
                                .AddField("直播狀態", "直播中");

                            if (!string.IsNullOrEmpty(twitchStream.GameName))
                                embedBuilder.AddField("分類", twitchStream.GameName, true);

                            embedBuilder.AddField("開始時間", twitchStream.StreamStartAt.ConvertDateTimeToDiscordMarkdown());

                            if (twitchSpider.IsRecord && RecordTwitch(twitchStream))
                                embedBuilder.WithRecordColor();
                            else
                                embedBuilder.WithOkColor();

                            await SendStreamMessageAsync(twitchStream.UserId, embedBuilder.Build(), NoticeType.StartStream);

                            if (!twitchSpider.IsWarningUser)
                            {
                                try
                                {
                                    await Program.RedisDb.StringSetAsync(new($"twitch:stream_data:{stream.UserId}"), JsonConvert.SerializeObject(twitchStream));
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, $"Twitch Set Redis Data Error: {stream.Id}");
                                }

                                try
                                {
                                    await _twitchApi.Value.Helix.EventSub.CreateEventSubSubscriptionAsync("channel.update", "2", new() { { "broadcaster_user_id", stream.UserId } },
                                          TwitchLib.Api.Core.Enums.EventSubTransportMethod.Webhook, webhookCallback: $"https://{_apiServerUrl}/TwitchWebHooks", webhookSecret: twitchWebHookSecret);

                                    await _twitchApi.Value.Helix.EventSub.CreateEventSubSubscriptionAsync("stream.offline", "1", new() { { "broadcaster_user_id", stream.UserId } },
                                          TwitchLib.Api.Core.Enums.EventSubTransportMethod.Webhook, webhookCallback: $"https://{_apiServerUrl}/TwitchWebHooks", webhookSecret: twitchWebHookSecret);

                                    Log.Info($"已註冊 Twitch WebHook: {twitchSpider.UserId} ({twitchSpider.UserName})");
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, $"註冊 Twitch WebHook 失敗，也許是已經註冊過了?");
                                }
                            }
                        }
                        catch (Exception ex) { Log.Error(ex, $"TwitchService-GetData: {twitchSpider.UserLogin}"); }
                    }

                    await Task.Delay(1000); // 等個一秒鐘避免觸發 429 之類的錯誤，雖然也不知道有沒有用
                }

                try
                {
                    twitchStreamDb.SaveChanges();
                    db.SaveChanges();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "TwitchService-Timer: SaveDb Error");
                }
            }
            catch (Exception ex) { Log.Error(ex, "TwitchService-Timer"); }
            finally { isRuning = false; }

        }

        private async Task SendStreamMessageAsync(string twitchUserId, Embed embed, NoticeType noticeType)
        {
            if (!Program.IsConnect)
                return;

#if DEBUG || DEBUG_DONTREGISTERCOMMAND
            Log.New($"Twitch 通知: {twitchUserId} - {embed.Title} ({noticeType})");
#else
            using (var db = MainDbContext.GetDbContext())
            {
                var noticeGuildList = db.NoticeTwitchStreamChannels.Where((x) => x.NoticeTwitchUserId == twitchUserId).ToList();
                Log.New($"發送 Twitch 通知 ({noticeGuildList.Count} / {noticeType}): ({twitchUserId}) - {embed.Title}");

                foreach (var item in noticeGuildList)
                {
                    try
                    {
                        string sendMessage = "";
                        switch (noticeType)
                        {
                            case NoticeType.StartStream:
                                sendMessage = item.StartStreamMessage;
                                break;
                            case NoticeType.EndStream:
                                sendMessage = item.EndStreamMessage;
                                break;
                            case NoticeType.ChangeStreamData:
                                sendMessage = item.ChangeStreamDataMessage;
                                break;
                        }

                        if (sendMessage == "-") continue;

                        var guild = _client.GetGuild(item.GuildId);
                        if (guild == null)
                        {
                            Log.Warn($"Twitch 通知 ({twitchUserId}) | 找不到伺服器 {item.GuildId}");
                            db.NoticeTwitchStreamChannels.RemoveRange(db.NoticeTwitchStreamChannels.Where((x) => x.GuildId == item.GuildId));
                            db.SaveChanges();
                            continue;
                        }

                        var channel = guild.GetTextChannel(item.DiscordChannelId);
                        if (channel == null) continue;

                        await Policy.Handle<TimeoutException>()
                            .Or<Discord.Net.HttpException>((httpEx) => ((int)httpEx.HttpCode).ToString().StartsWith("50"))
                            .WaitAndRetryAsync(3, (retryAttempt) =>
                            {
                                var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                                Log.Warn($"Twitch 通知 ({twitchUserId}) | {item.GuildId} / {item.DiscordChannelId} 發送失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                                return timeSpan;
                            })
                            .ExecuteAsync(async () =>
                            {
                                var message = await channel.SendMessageAsync(text: sendMessage, embed: embed, components: noticeType == NoticeType.StartStream ? _messageComponent : null, options: new RequestOptions() { RetryMode = RetryMode.AlwaysRetry });

                                try
                                {
                                    if (channel is INewsChannel && Utility.OfficialGuildList.Contains(guild.Id))
                                        await message.CrosspostAsync();
                                }
                                catch (Discord.Net.HttpException httpEx) when (httpEx.DiscordCode == DiscordErrorCode.MessageAlreadyCrossposted)
                                {
                                    // ignore
                                }
                            });
                    }
                    catch (Discord.Net.HttpException httpEx)
                    {
                        if (httpEx.DiscordCode.HasValue && (httpEx.DiscordCode.Value == DiscordErrorCode.InsufficientPermissions || httpEx.DiscordCode.Value == DiscordErrorCode.MissingPermissions))
                        {
                            Log.Warn($"Twitch 通知 ({twitchUserId}) | 遺失權限 {item.GuildId} / {item.DiscordChannelId}");
                            db.NoticeTwitchStreamChannels.RemoveRange(db.NoticeTwitchStreamChannels.Where((x) => x.DiscordChannelId == item.DiscordChannelId));
                            db.SaveChanges();
                        }
                        else if (((int)httpEx.HttpCode).ToString().StartsWith("50"))
                        {
                            Log.Warn($"Twitch 通知 ({twitchUserId}) | Discord 50X 錯誤: {httpEx.HttpCode}");
                        }
                        else
                        {
                            Log.Error(httpEx, $"Twitch 通知 ({twitchUserId}) | Discord 未知錯誤 {item.GuildId} / {item.DiscordChannelId}");
                        }
                    }
                    catch (TimeoutException)
                    {
                        Log.Warn($"Twitch 通知 ({twitchUserId}) | Timeout {item.GuildId} / {item.DiscordChannelId}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Twitch 通知 ({twitchUserId}) | 未知錯誤 {item.GuildId} / {item.DiscordChannelId}");
                    }
                }
            }
#endif
        }

        private bool RecordTwitch(TwitchStream twitchStream)
        {
            Log.Info($"{twitchStream.UserName} ({twitchStream.StreamId}): {twitchStream.StreamTitle}");

            try
            {
                if (!Directory.Exists(twitchRecordPath))
                    Directory.CreateDirectory(twitchRecordPath);
            }
            catch (Exception ex)
            {
                Log.Error($"Twitch 保存路徑不存在且不可建立: {twitchRecordPath}");
                Log.Error($"更改保存路徑至 Data 資料夾: {Program.GetDataFilePath("")}");
                Log.Error(ex.ToString());

                twitchRecordPath = Program.GetDataFilePath("");
            }

            string procArgs = $"streamlink --twitch-disable-ads https://twitch.tv/{twitchStream.UserLogin} best --output \"{twitchRecordPath}[{twitchStream.UserLogin}]{twitchStream.StreamStartAt:yyyyMMdd} - {twitchStream.StreamId}.ts\"";
            if (!string.IsNullOrEmpty(twitchOAuthToken))
                procArgs += $" \"--twitch-api-header=Authorization=OAuth {twitchOAuthToken}\"";

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) Process.Start("tmux", $"new-window -d -n \"Twitch {twitchStream.UserLogin}\" {procArgs}");
                else Process.Start(new ProcessStartInfo()
                {
                    FileName = "streamlink",
                    Arguments = procArgs.Replace("streamlink", ""),
                    CreateNoWindow = false,
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "RecordTwitch 失敗，請確認是否已安裝 StreamLink");
                return false;
            }
        }
    }
}
