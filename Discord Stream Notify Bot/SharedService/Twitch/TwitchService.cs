﻿using Discord.Interactions;
using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction;
using Dorssel.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Exceptions;
using Clip = TwitchLib.Api.Helix.Models.Clips.GetClips.Clip;
using Stream = TwitchLib.Api.Helix.Models.Streams.GetStreams.Stream;
using User = TwitchLib.Api.Helix.Models.Users.GetUsers.User;
using Video = TwitchLib.Api.Helix.Models.Videos.GetVideos.Video;

#if RELEASE
using Polly;
#endif

namespace Discord_Stream_Notify_Bot.SharedService.Twitch
{
    public class TwitchService : IInteractionService
    {
        public enum NoticeType
        {
            [ChoiceDisplay("開始直播")]
            StartStream,
            [ChoiceDisplay("結束直播")]
            EndStream,
            [ChoiceDisplay("更改直播資料")]
            ChangeStreamData
        }

        internal bool IsEnable { get; private set; } = true;
        internal Lazy<TwitchAPI> TwitchApi { get; }
        private Regex UserLoginRegex { get; } = new(@"twitch.tv/(?<name>[\w\d\-_]+)/?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private bool isRuning = false;

        private readonly EmojiService _emojiService;
        private readonly DiscordSocketClient _client;
        private readonly Timer _timer;
        private readonly HashSet<string> _hashSet = new();
        private readonly MessageComponent _messageComponent;
        private readonly string _apiServerUrl, _twitchOAuthToken, _twitchWebHookSecret;
        private readonly ConcurrentDictionary<string, DebounceChannelUpdateMessage> _debounceChannelUpdateMessage = new();

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
                _twitchWebHookSecret = Program.RedisDb.StringGet("twitch:webhook_secret");
                if (string.IsNullOrEmpty(_twitchWebHookSecret))
                {
                    Log.Warn("缺少 TwitchWebHookSecret，嘗試重新建立...");

                    _twitchWebHookSecret = BotConfig.GenRandomKey(64);
                    Program.RedisDb.StringSet("twitch:webhook_secret", _twitchWebHookSecret);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "獲取 TwitchWebHookSecret 失敗，無法運行 Twitch 類功能");
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
                _twitchOAuthToken = botConfig.TwitchCookieAuthToken;
            }

            _apiServerUrl = botConfig.ApiServerDomain;
            _client = client;
            _emojiService = emojiService;

            TwitchApi = new(() => new()
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
                    var list = await TwitchApi.Value.Helix.EventSub.GetEventSubSubscriptionsAsync(userId: data.BroadcasterUserId);
                    foreach (var item in list.Subscriptions)
                    {
                        Log.Info($"Delete EventSub: {item.Id} ({item.Type})");
                        await TwitchApi.Value.Helix.EventSub.DeleteEventSubSubscriptionAsync(item.Id);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Demystify(), $"Event Delete Error: {data.BroadcasterUserId}");
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
                    Log.Error(ex.Demystify(), $"Twitch Get Redis Data Error: {data.BroadcasterUserId}");
                }

                DateTime createAt = DateTime.Now.AddDays(-3), endAt = DateTime.Now;
                var video = await GetLatestVODAsync(data.BroadcasterUserId);
                if (video == null)
                {
                    Log.Warn($"找不到對應的 Video 資料: {data.BroadcasterUserId}");
                }
                else
                {
                    twitchStream = twitchStream ?? new()
                    {
                        // 這個會有問題，因為圖奇的 VOD 標題會直接以開播當下的標題為準，中途變更的話也不會改
                        // 所以只有在 Redis 抓不到資料的時候再設定標題上去，否則以 Redis 最新的資料為準
                        StreamTitle = video.Title,
                    };
                    twitchStream.StreamStartAt = DateTime.Parse(video.CreatedAt);

                    createAt = DateTime.Parse(video.CreatedAt);
                    endAt = createAt + ParseToTimeSpan(video.Duration);
                }

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
                        .AddField("直播時長", $"{DateTime.Now.Subtract(twitchStream.StreamStartAt):hh'時'mm'分'ss'秒'}");
                }

                embedBuilder.AddField("關台時間", DateTime.UtcNow.ConvertDateTimeToDiscordMarkdown());

                if (video != null) // 僅增加與該場直播有關的 Clip
                {
                    var clips = await GetClipsAsync(data.BroadcasterUserId, createAt, endAt);
                    if (clips != null && clips.Any((x) => x.VideoId == video.Id))
                    {
                        int i = 0;
                        embedBuilder.AddField("最多觀看的 Clip", string.Join('\n', clips.Where((x) => x.VideoId == video.Id)
                            .Select((x) => $"{i++}. {Format.Url(x.Title, x.Url)} By `{x.CreatorName}` (`{x.ViewCount}` 次觀看)")));
                    }
                }

                using var db = MainDbContext.GetDbContext();
                var twitchSpider = db.TwitchSpider.AsNoTracking().FirstOrDefault((x) => x.UserId == data.BroadcasterUserId);
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
                    Log.Error(ex.Demystify(), $"Twitch Get Redis Data Error: {data.BroadcasterUserId}");
                }

                if (twitchStream == null)
                {
                    Log.Warn($"Redis 找不到 Twitch 頻道資料，忽略: {data.BroadcasterUserName}");
                    return;
                }

                bool isChangeTitle = twitchStream.StreamTitle != data.Title;
                bool isChangeCategory = twitchStream.GameName != data.CategoryName;
                if (!isChangeTitle && !isChangeCategory)
                {
                    Log.Warn($"Twitch 頻道更新資料相同，忽略: {data.BroadcasterUserName}");
                    return;
                }

                string message = $"`{DateTime.UtcNow.Subtract(twitchStream.StreamStartAt):hh':'mm':'ss}`";

                if (isChangeTitle)
                {
                    message += $"\n標題變更 `{twitchStream.StreamTitle}` => `{data.Title}`";
                }

                if (isChangeCategory)
                {
                    message += $"\n分類變更 `" +
                    (string.IsNullOrEmpty(twitchStream.GameName) ? "無" : twitchStream.GameName) +
                    "` => `" +
                    (string.IsNullOrEmpty(data.CategoryName) ? "無" : data.CategoryName) +
                    "`";
                }

                _debounceChannelUpdateMessage.AddOrUpdate(data.BroadcasterUserId,
                    (userId) =>
                    {
                        var debounce = new DebounceChannelUpdateMessage(this, data.BroadcasterUserName, data.BroadcasterUserLogin, data.BroadcasterUserId);
                        debounce.AddMessage(message);
                        return debounce;
                    },
                    (userId, debounce) =>
                    {
                        debounce.AddMessage(message);
                        return debounce;
                    });

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
                    Log.Error(ex.Demystify(), $"Twitch Channel Update Set Redis Data Error: {data.BroadcasterUserId}");
                }
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

        // Generate by ChatGPT
        public TimeSpan ParseToTimeSpan(string input)
        {
            int days = 0, hours = 0, minutes = 0, seconds = 0;
            // 定義正則表達式去匹配天、時、分、秒
            Regex regex = new Regex(@"(\d+)d|(\d+)h|(\d+)m|(\d+)s");
            MatchCollection matches = regex.Matches(input);
            // 遍歷匹配結果並賦值
            foreach (Match match in matches)
            {
                if (match.Groups[1].Success)
                    days = int.Parse(match.Groups[1].Value);
                if (match.Groups[2].Success)
                    hours = int.Parse(match.Groups[2].Value);
                if (match.Groups[3].Success)
                    minutes = int.Parse(match.Groups[3].Value);
                if (match.Groups[4].Success)
                    seconds = int.Parse(match.Groups[4].Value);
            }
            return new TimeSpan(days, hours, minutes, seconds);
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

                            if (twitchSpider.IsRecord && await RecordTwitchAsync(twitchStream))
                                embedBuilder.WithRecordColor();
                            else
                                embedBuilder.WithOkColor();

                            if (!twitchSpider.IsWarningUser)
                            {
                                try
                                {
                                    await Program.RedisDb.StringSetAsync(new($"twitch:stream_data:{stream.UserId}"), JsonConvert.SerializeObject(twitchStream));
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex.Demystify(), $"Twitch Set Redis Data Error: {stream.Id}");
                                }

#if RELEASE
                                if (await CreateEventSubSubscriptionAsync(stream.UserId))
                                {
                                    Log.Info($"已註冊 Twitch WebHook: {twitchSpider.UserId} ({twitchSpider.UserName})");
                                }
#endif
                            }

                            await SendStreamMessageAsync(twitchStream.UserId, embedBuilder.Build(), NoticeType.StartStream);
                        }
                        catch (Exception ex) { Log.Error(ex.Demystify(), $"TwitchService-GetData: {twitchSpider.UserLogin}"); }
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
                    Log.Error(ex.Demystify(), "TwitchService-Timer: SaveDb Error");
                }
            }
            catch (Exception ex) { Log.Error(ex.Demystify(), "TwitchService-Timer"); }
            finally { isRuning = false; }

        }

        internal async Task<bool> CreateEventSubSubscriptionAsync(string broadcasterUserId)
        {
            try
            {
                await TwitchApi.Value.Helix.EventSub.CreateEventSubSubscriptionAsync("channel.update", "2", new() { { "broadcaster_user_id", broadcasterUserId } },
                      EventSubTransportMethod.Webhook, webhookCallback: $"https://{_apiServerUrl}/TwitchWebHooks", webhookSecret: _twitchWebHookSecret);

                await TwitchApi.Value.Helix.EventSub.CreateEventSubSubscriptionAsync("stream.offline", "1", new() { { "broadcaster_user_id", broadcasterUserId } },
                      EventSubTransportMethod.Webhook, webhookCallback: $"https://{_apiServerUrl}/TwitchWebHooks", webhookSecret: _twitchWebHookSecret);

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), $"註冊 {broadcasterUserId} 的 Twitch WebHook 失敗，也許是已經註冊過了?");
            }

            return false;
        }

        internal async Task SendStreamMessageAsync(string twitchUserId, Embed embed, NoticeType noticeType)
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
                        Log.Error(ex.Demystify(), $"Twitch 通知 ({twitchUserId}) | 未知錯誤 {item.GuildId} / {item.DiscordChannelId}");
                    }
                }
            }
#endif
        }

        private async Task<bool> RecordTwitchAsync(TwitchStream twitchStream)
        {
            Log.Info($"{twitchStream.UserName} ({twitchStream.StreamId}): {twitchStream.StreamTitle}");

            if (Program.Redis != null)
            {
                if (await Program.RedisSub.PublishAsync(new RedisChannel("twitch.record", RedisChannel.PatternMode.Literal), twitchStream.UserLogin) != 0)
                {
                    Log.Info($"已發送 Twitch 錄影請求: {twitchStream.UserLogin}");
                    return true;
                }
                else
                {
                    Log.Warn($"Redis Sub 頻道不存在，請開啟錄影工具: {twitchStream.UserLogin}");
                }
            }

            return false;
        }

        #region TwitchAPI
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
                var users = await TwitchApi.Value.Helix.Users.GetUsersAsync(userId, userLogin);
                return users.Users.FirstOrDefault();
            }
            catch (BadRequestException)
            {
                Log.Error($"無法取得 Twitch 資料，可能是找不到輸入的使用者資料: ({twitchUserId}) {twitchUserLogin}");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), $"無法取得 Twitch 資料: ({twitchUserId}) {twitchUserLogin}");
                return null;
            }
        }

        public async Task<IReadOnlyList<User>> GetUsersAsync(params string[] twitchUserLogins)
        {
            try
            {
                List<User> result = new();
                foreach (var item in twitchUserLogins.Chunk(100))
                {
                    var users = await TwitchApi.Value.Helix.Users.GetUsersAsync(logins: new List<string>(twitchUserLogins));
                    if (users.Users.Any())
                    {
                        result.AddRange(users.Users);
                    }
                }

                return result;
            }
            catch (BadRequestException)
            {
                Log.Error($"無法取得 Twitch 資料，可能是找不到輸入的使用者資料: {twitchUserLogins.First()}");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), $"無法取得 Twitch 資料: {twitchUserLogins.First()}");
                return null;
            }
        }

        public async Task<Video> GetLatestVODAsync(string twitchUserId)
        {
            try
            {
                var videosResponse = await TwitchApi.Value.Helix.Videos.GetVideosAsync(userId: twitchUserId, first: 1, type: VideoType.Archive);
                return videosResponse.Videos.FirstOrDefault();
            }
            catch (BadRequestException)
            {
                Log.Error($"無法取得 Twitch 資料，可能是找不到輸入的使用者資料: {twitchUserId}");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), $"無法取得 Twitch 資料: {twitchUserId}");
                return null;
            }
        }

        public async Task<IReadOnlyList<Clip>> GetClipsAsync(string twitchUserId, DateTime startedAt, DateTime endedAt)
        {
            try
            {
                var clipsResponse = await TwitchApi.Value.Helix.Clips.GetClipsAsync(broadcasterId: twitchUserId, startedAt: startedAt, endedAt: endedAt, first: 5);
                if (clipsResponse.Clips.Any())
                {
                    return clipsResponse.Clips;
                }
                else
                {
                    return null;
                }
            }
            catch (BadRequestException)
            {
                Log.Error($"無法取得 Twitch 資料，可能是找不到輸入的使用者資料: {twitchUserId}");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), $"無法取得 Twitch 資料: {twitchUserId}");
                return null;
            }
        }

        public async Task<IReadOnlyList<Stream>> GetNowStreamsAsync(params string[] twitchUserIds)
        {
            try
            {
                List<Stream> result = new();
                foreach (var item in twitchUserIds.Chunk(100))
                {
                    var streams = await TwitchApi.Value.Helix.Streams.GetStreamsAsync(first: 100, userIds: new List<string>(twitchUserIds));
                    if (streams.Streams.Any())
                    {
                        result.AddRange(streams.Streams);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), $"無法取得 Twitch 資料，請確認 {nameof(BotConfig.TwitchClientId)} 或 {nameof(BotConfig.TwitchClientSecret)} 是否正常");
                return Array.Empty<Stream>();
            }
        }
        #endregion

        // https://blog.darkthread.net/blog/dotnet-debounce/
        // https://github.com/dorssel/dotnet-debounce
        class DebounceChannelUpdateMessage
        {
            private readonly Debouncer _debouncer;
            private readonly TwitchService _twitchService;
            private readonly string _twitchUserName, _twitchUserLogin, _twitchUserId;
            private readonly ConcurrentQueue<string> messageQueue = new();

            public DebounceChannelUpdateMessage(TwitchService twitchService, string twitchUserName, string twitchUserLogin, string twitchUserId)
            {
                _twitchService = twitchService;
                _twitchUserName = twitchUserName;
                _twitchUserLogin = twitchUserLogin;
                _twitchUserId = twitchUserId;

                _debouncer = new()
                {
                    DebounceWindow = TimeSpan.FromMinutes(1),
                    DebounceTimeout = TimeSpan.FromMinutes(3),
                };
                _debouncer.Debounced += _debouncer_Debounced;
            }

            private void _debouncer_Debounced(object sender, DebouncedEventArgs e)
            {
                try
                {
                    Log.Info($"{_twitchUserLogin} 發送頻道更新通知 (Debouncer 觸發數量: {e.Count})");

                    var description = string.Join("\n\n", messageQueue);

                    var embedBuilder = new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle($"{_twitchUserName} 直播資料更新")
                        .WithUrl($"https://twitch.tv/{_twitchUserLogin}")
                        .WithDescription(description);

                    using var db = MainDbContext.GetDbContext();
                    var twitchSpider = db.TwitchSpider.AsNoTracking().FirstOrDefault((x) => x.UserId == _twitchUserId);
                    if (twitchSpider != null)
                        embedBuilder.WithThumbnailUrl(twitchSpider.ProfileImageUrl);

                    Task.Run(async () => { await _twitchService.SendStreamMessageAsync(_twitchUserId, embedBuilder.Build(), NoticeType.ChangeStreamData); });
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Demystify(), $"{_twitchUserLogin} 訊息去抖動失敗");
                }
                finally
                {
                    messageQueue.Clear();
                    _debouncer.Reset();
                }
            }

            public void AddMessage(string message)
            {
                Log.Debug($"Debouncer ({_twitchUserLogin}): {message}");

                messageQueue.Enqueue(message);
                _debouncer.Trigger();
            }

            bool isDisposed;
            public void Dispose()
            {
                if (!isDisposed)
                {
                    _debouncer.Debounced -= _debouncer_Debounced;
                    _debouncer.Dispose();
                    isDisposed = true;
                }
            }
        }
    }
}
