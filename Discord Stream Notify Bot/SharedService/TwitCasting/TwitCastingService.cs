using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.HttpClients;
using Discord_Stream_Notify_Bot.HttpClients.TwitCasting;
using Discord_Stream_Notify_Bot.Interaction;
using System.Runtime.InteropServices;

#if RELEASE
using Polly;
#endif

namespace Discord_Stream_Notify_Bot.SharedService.Twitcasting
{
    public class TwitcastingService : IInteractionService
    {
        public bool IsEnable { get; private set; } = true;

        private readonly HashSet<int> hashSet = new HashSet<int>();
        private readonly DiscordSocketClient _client;
        private readonly TwitcastingClient _twitcastingClient;
        private readonly EmojiService _emojiService;
        private readonly MainDbService _dbService;
        private readonly Timer _refreshCategoriesTimer, _refreshNowStreamTimer;

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

            _refreshCategoriesTimer = new Timer(async (obj) =>
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

            _refreshNowStreamTimer = new Timer(async (obj) => { await TimerHandel(); },
                null, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1));

            _dbService = dbService;
        }

        public async Task<(string ChannelId, string ChannelTitle)> GetChannelIdAndTitleAsync(string channelUrl)
        {
            string channelId = channelUrl.Split('?')[0].Replace("https://twitcasting.tv/", "").Split('/')[0];
            if (string.IsNullOrEmpty(channelId))
                return (string.Empty, string.Empty);

            string channelTitle = await GetChannelTitleAsync(channelId).ConfigureAwait(false);
            if (string.IsNullOrEmpty(channelTitle))
                return (string.Empty, string.Empty);

            return (channelId, channelTitle);
        }

        public async Task<string> GetChannelTitleAsync(string channelId)
        {
            try
            {
                HtmlAgilityPack.HtmlWeb htmlWeb = new HtmlAgilityPack.HtmlWeb();
                var htmlDocument = await htmlWeb.LoadFromWebAsync($"https://twitcasting.tv/{channelId}");
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
            if (isRuning) return;
            isRuning = true;

            using var db = _dbService.GetDbContext();
            var list = db.TwitcastingSpider.AsNoTracking().ToList();

            try
            {
                foreach (var item in list)
                {
                    var data = await _twitcastingClient.GetNewStreamDataAsync(item.ChannelId);
                    if (data == null || !data.Movie.Live)
                        continue;

                    if (hashSet.Contains(data.Movie.Id))
                        continue;

                    if (await db.TwitcastingStreams.AsNoTracking().AnyAsync((x) => x.StreamId == data.Movie.Id))
                    {
                        hashSet.Add(data.Movie.Id);
                        continue;
                    }

                    try
                    {
                        hashSet.Add(data.Movie.Id);

                        var streamData = await _twitcastingClient.GetMovieInfoAsync(data.Movie.Id);
                        if (streamData == null)
                        {
                            Log.Error($"TwitCastingService-GetMovieInfoAsync: {item.ChannelId} / {data.Movie.Id}");
                            continue;
                        }

                        var twitcastingStream = new TwitcastingStream()
                        {
                            ChannelId = item.ChannelId,
                            ChannelTitle = item.ChannelTitle,
                            StreamId = data.Movie.Id,
                            StreamTitle = streamData.Movie.Title ?? "無標題",
                            StreamSubTitle = streamData.Movie.Subtitle,
                            Category = GetCategorieNameById(streamData.Movie.Category),
                            ThumbnailUrl = streamData.Movie.LargeThumbnail,
                            StreamStartAt = UnixTimeStampToDateTime(streamData.Movie.Created)
                        };

                        await db.TwitcastingStreams.AddAsync(twitcastingStream);

                        await SendStreamMessageAsync(twitcastingStream, streamData.Movie.IsProtected, !streamData.Movie.IsProtected && item.IsRecord && RecordTwitCasting(twitcastingStream));
                    }
                    catch (Exception ex) { Log.Error($"TwitCastingService-GetData {item.ChannelId}: {ex}"); }

                    await Task.Delay(1000); // 等個一秒鐘避免觸發 429 之類的錯誤，雖然也不知道有沒有用
                }
            }
            catch (Exception ex) { Log.Error($"TwitCastingService-Timer {ex}"); }
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
                var noticeGuildList = db.NoticeTwitcastingStreamChannels.AsNoTracking().Where((x) => x.ChannelId == twitcastingStream.ChannelId).ToList();
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