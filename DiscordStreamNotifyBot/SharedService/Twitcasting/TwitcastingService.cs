using DiscordStreamNotifyBot.DataBase;
using DiscordStreamNotifyBot.DataBase.Table;
using DiscordStreamNotifyBot.HttpClients;
using DiscordStreamNotifyBot.Interaction;
using System.Runtime.InteropServices;
using DiscordStreamNotifyBot.HttpClients.Twitcasting.Model;


#if RELEASE
using Polly;
#endif

namespace DiscordStreamNotifyBot.SharedService.Twitcasting
{
    public class TwitcastingService : IInteractionService
    {
        public bool IsEnable { get; private set; } = true;

        private readonly HashSet<int> hashSet = new HashSet<int>();
        private readonly DiscordSocketClient _client;
        private readonly TwitcastingClient _twitcastingClient;
        private readonly EmojiService _emojiService;
        private readonly MainDbService _dbService;
        private readonly Timer _refreshCategoriesTimer, _refreshWebHookTimer;

        private List<Category> categories;
        private string twitcastingRecordPath = "";
        private bool isRuning = false;

        public TwitcastingService(DiscordSocketClient client, TwitcastingClient twitcastingClient, BotConfig botConfig, EmojiService emojiService, MainDbService dbService)
        {
            if (string.IsNullOrEmpty(botConfig.TwitCastingClientId) || string.IsNullOrEmpty(botConfig.TwitCastingClientSecret))
            {
                Log.Warn($"{nameof(botConfig.TwitCastingClientId)} 或 {nameof(botConfig.TwitCastingClientSecret)} 遺失，無法運行 TwitCasting 類功能");
                IsEnable = false;
                return;
            }

            _client = client;
            _twitcastingClient = twitcastingClient;
            _emojiService = emojiService;

            twitcastingRecordPath = botConfig.TwitCastingRecordPath;
            if (string.IsNullOrEmpty(twitcastingRecordPath)) twitcastingRecordPath = Utility.GetDataFilePath("");
            if (!twitcastingRecordPath.EndsWith(Utility.GetPlatformSlash())) twitcastingRecordPath += Utility.GetPlatformSlash();

            _refreshCategoriesTimer = new Timer(async (_) =>
            {
                try
                {
                    categories = await _twitcastingClient.GetCategoriesAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Demystify(), "TwitCasting 分類獲取失敗");
                }
            }, null, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(30));

            _refreshWebHookTimer = new Timer(async (_) => { await TimerHandel(); },
                null, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(15));

            _dbService = dbService;

            Bot.RedisSub.Subscribe(new RedisChannel("twitcasting.pubsub.startlive", RedisChannel.PatternMode.Literal), async (channel, message) =>
            {
                var webHookJson = JsonConvert.DeserializeObject<TwitCastingWebHookJson>(message);
                if (webHookJson == null)
                {
                    Log.Error("TwitCasting WebHook JSON 反序列化失敗");
                    return;
                }

                using var db = _dbService.GetDbContext();
                if (await db.TwitcastingStreams.AsNoTracking().AnyAsync((x) => x.StreamId == int.Parse(webHookJson.Movie.Id)))
                {
                    Log.Warn($"TwitCasting 重複開台通知: {webHookJson.Movie.Id} - {webHookJson.Movie.Title}");
                    return;
                }

                bool isRecord = db.TwitcastingSpider.SingleOrDefault((x) => x.ScreenId == webHookJson.Broadcaster.Id)?.IsRecord ?? false;
                var twitcastingStream = new TwitcastingStream()
                {
                    ChannelId = webHookJson.Broadcaster.Id,
                    ChannelTitle = webHookJson.Broadcaster.Name,
                    StreamId = int.Parse(webHookJson.Movie.Id),
                    StreamTitle = webHookJson.Movie.Title ?? "無標題",
                    StreamSubTitle = webHookJson.Movie.Subtitle,
                    Category = GetCategorieNameById(webHookJson.Movie.Category),
                    ThumbnailUrl = webHookJson.Movie.LargeThumbnail,
                    StreamStartAt = UnixTimeStampToDateTime(webHookJson.Movie.Created)
                };

                await db.TwitcastingStreams.AddAsync(twitcastingStream);
                await db.SaveChangesAsync();

                await SendStreamMessageAsync(twitcastingStream, webHookJson.Movie.IsProtected, !webHookJson.Movie.IsProtected && isRecord && RecordTwitCasting(twitcastingStream));
            });
        }

#nullable enable

        public async Task<HttpClients.Twitcasting.Model.Broadcaster?> GetChannelNameAndTitleAsync(string channelUrl)
        {
            string channelName = channelUrl.Split('?')[0].Replace("https://twitcasting.tv/", "").Split('/')[0];
            if (string.IsNullOrEmpty(channelName))
                return null;

            var data = await _twitcastingClient.GetUserInfoAsync(channelName).ConfigureAwait(false);

            return data?.User;
        }

        public async Task<string?> GetChannelTitleAsync(string channelName)
        {
            try
            {
                HtmlAgilityPack.HtmlWeb htmlWeb = new HtmlAgilityPack.HtmlWeb();
                var htmlDocument = await htmlWeb.LoadFromWebAsync($"https://twitcasting.tv/{channelName}");
                var htmlNodes = htmlDocument.DocumentNode.Descendants();
                var htmlNode = htmlNodes.FirstOrDefault((x) => x.Name == "span" && x.HasClass("tw-user-nav-name") || x.HasClass("tw-user-nav2-name"));

                if (htmlNode != null)
                {
                    return htmlNode.InnerText.Trim();
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Error($"TwitCastingService-GetChannelNameAsync: {ex}");
                return null;
            }
        }

        private async Task TimerHandel()
        {
#if DEBUG
            return;
#endif

            if (isRuning) return;
            isRuning = true;

            using var db = _dbService.GetDbContext();
            var spiderList = db.TwitcastingSpider.AsNoTracking().ToList();

            try
            {
                // 取得所有已註冊的 webhook
                var registeredWebhooks = await _twitcastingClient.GetAllRegistedWebHookAsync();
                if (registeredWebhooks == null)
                {
                    Log.Error("TwitCastingService-Timer: 無法獲取已註冊的 Webhook 列表，請檢查 TwitCasting API 設定是否正確。");
                    return;
                }
                var registeredChannelIds = registeredWebhooks.Select(x => x.UserId).ToHashSet();

                // 需要註冊 webhook 的頻道
                var spiderChannelIds = spiderList.Where((x) => !string.IsNullOrEmpty(x.ChannelId)).Select(x => x.ChannelId).ToHashSet();

                // 註冊缺少的 webhook
                foreach (var channelId in spiderChannelIds.Except(registeredChannelIds))
                {
                    await _twitcastingClient.RegisterWebHookAsync(channelId);
                    Log.Info($"註冊 TwitCasting Webhook: {channelId}");
                }

                // 移除多餘的 webhook
                foreach (var channelId in registeredChannelIds.Except(spiderChannelIds))
                {
                    await _twitcastingClient.RemoveWebHookAsync(channelId);
                    Log.Info($"移除 TwitCasting Webhook: {channelId}");
                }
            }
            catch (Exception ex) { Log.Error(ex.Demystify(), "TwitCastingService-Timer"); }
            finally { isRuning = false; }

            await db.SaveChangesAsync();
        }

        private async Task SendStreamMessageAsync(TwitcastingStream twitcastingStream, bool isPrivate = false, bool isRecord = false)
        {
#if DEBUG
            Log.New($"TwitCasting 開台通知: {twitcastingStream.ChannelTitle} - {twitcastingStream.StreamTitle} (isPrivate: {isPrivate})");
#else
            using (var db = _dbService.GetDbContext())
            {
                var noticeGuildList = db.NoticeTwitcastingStreamChannels.AsNoTracking().Where((x) => x.ScreenId == twitcastingStream.ChannelId).ToList();
                Log.New($"發送 TwitCasting 開台通知 ({noticeGuildList.Count}): {twitcastingStream.ChannelTitle} - {twitcastingStream.StreamTitle} (私人直播: {isPrivate})");

                EmbedBuilder embedBuilder = new EmbedBuilder()
                    .WithTitle(twitcastingStream.StreamTitle)
                    .WithDescription(Format.Url($"{twitcastingStream.ChannelTitle}", $"https://twitcasting.tv/{twitcastingStream.ChannelId}"))
                    .WithUrl($"https://twitcasting.tv/{twitcastingStream.ChannelId}/movie/{twitcastingStream.StreamId}")
                    .WithImageUrl(twitcastingStream.ThumbnailUrl)
                    .AddField("需要密碼的私人直播", isPrivate ? "是" : "否", true);

                if (!string.IsNullOrEmpty(twitcastingStream.StreamSubTitle)) embedBuilder.AddField("副標題", twitcastingStream.StreamSubTitle, true);
                if (!string.IsNullOrEmpty(twitcastingStream.Category)) embedBuilder.AddField("分類", twitcastingStream.Category, true);

                embedBuilder.AddField("開始時間", twitcastingStream.StreamStartAt.ConvertDateTimeToDiscordMarkdown());

                if (isPrivate) embedBuilder.WithErrorColor();
                if (isRecord) embedBuilder.WithRecordColor();
                else embedBuilder.WithOkColor();

                MessageComponent comp = new ComponentBuilder()
                        .WithButton("贊助小幫手 (綠界) #ad", style: ButtonStyle.Link, emote: _emojiService.ECPayEmote, url: Utility.ECPayUrl, row: 1)
                        .WithButton("贊助小幫手 (Paypal) #ad", style: ButtonStyle.Link, emote: _emojiService.PayPalEmote, url: Utility.PaypalUrl, row: 1).Build();

                foreach (var item in noticeGuildList)
                {
                    try
                    {
                        var guild = _client.GetGuild(item.GuildId);
                        if (guild == null)
                        {
                            Log.Warn($"TwitCasting 通知 ({item.DiscordChannelId}) | 找不到伺服器 {item.GuildId}");
                            db.NoticeTwitcastingStreamChannels.RemoveRange(db.NoticeTwitcastingStreamChannels.Where((x) => x.GuildId == item.GuildId));
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
                                Log.Warn($"{item.GuildId} / {item.DiscordChannelId} 發送失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                                return timeSpan;
                            })
                            .ExecuteAsync(async () =>
                            {
                                var message = await channel.SendMessageAsync(text: item.StartStreamMessage, embed: embedBuilder.Build(), components: comp, options: new RequestOptions() { RetryMode = RetryMode.AlwaysRetry });

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
                            Log.Warn($"TwitCasting 通知 - 遺失權限 {item.GuildId} / {item.DiscordChannelId}");
                            db.NoticeTwitcastingStreamChannels.RemoveRange(db.NoticeTwitcastingStreamChannels.Where((x) => x.DiscordChannelId == item.DiscordChannelId));
                            db.SaveChanges();
                        }
                        else if (((int)httpEx.HttpCode).ToString().StartsWith("50"))
                        {
                            Log.Warn($"TwitCasting 通知 - Discord 50X 錯誤: {httpEx.HttpCode}");
                        }
                        else
                        {
                            Log.Error(httpEx, $"TwitCasting 通知 - Discord 未知錯誤 {item.GuildId} / {item.DiscordChannelId}");
                        }
                    }
                    catch (TimeoutException)
                    {
                        Log.Warn($"TwitCasting 通知 - Timeout {item.GuildId} / {item.DiscordChannelId}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex.Demystify(), $"TwitCasting 通知 - 未知錯誤 {item.GuildId} / {item.DiscordChannelId}");
                    }
                }
            }
#endif
        }

        private bool RecordTwitCasting(TwitcastingStream twitcastingStream)
        {
            Log.Info($"{twitcastingStream.ChannelTitle} ({twitcastingStream.StreamId}): {twitcastingStream.StreamTitle}");

            try
            {
                if (!Directory.Exists(twitcastingRecordPath))
                    Directory.CreateDirectory(twitcastingRecordPath);
            }
            catch (Exception ex)
            {
                Log.Error($"TwitCasting 保存路徑不存在且不可建立: {twitcastingRecordPath}");
                Log.Error($"更改保存路徑至Data資料夾: {Utility.GetDataFilePath("")}");
                Log.Error(ex.ToString());

                twitcastingRecordPath = Utility.GetDataFilePath("");
            }

            // 自幹 Tc 錄影能錄但時間會出問題，還是用 StreamLink 方案好了
            string procArgs = $"streamlink https://twitcasting.tv/{twitcastingStream.ChannelId} best --output \"{twitcastingRecordPath}[{twitcastingStream.ChannelId}]{twitcastingStream.StreamStartAt:yyyyMMdd} - {twitcastingStream.StreamId}.ts\"";
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) Process.Start("tmux", $"new-window -d -n \"TwitCasting {twitcastingStream.ChannelId}\" {procArgs}");
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
                Log.Error(ex.Demystify(), "RecordTwitCasting 失敗，請確認是否已安裝 StreamLink");
                return false;
            }
        }


        // https://stackoverflow.com/questions/249760/how-can-i-convert-a-unix-timestamp-to-datetime-and-vice-versa
        private static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dateTime = dateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dateTime;
        }

        private string GetCategorieNameById(string categorieId)
        {
            string result = categorieId;

            if (categories != null && categories.Any())
            {
                foreach (var item in categories)
                {
                    var subCategory = item.SubCategories.FirstOrDefault((x) => x.Id == categorieId);
                    if (subCategory != null)
                        result = subCategory.Name;
                }
            }

            return result;
        }
    }
}