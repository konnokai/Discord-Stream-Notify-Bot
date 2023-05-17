using Discord_Stream_Notify_Bot.Interaction;
using Discord_Stream_Notify_Bot.SharedService.Youtube.Json;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Net.Http;
using System.Text.RegularExpressions;
using Extensions = Discord_Stream_Notify_Bot.Interaction.Extensions;

namespace Discord_Stream_Notify_Bot.SharedService.Youtube
{
    public partial class YoutubeStreamService
    {
        private static Dictionary<string, DataBase.Table.Video> addNewStreamVideo = new();
        private static HashSet<string> newStreamList = new();
        private bool isFirstHolo = true, isFirst2434 = true, isFirstOther = true;

        private async Task HoloScheduleAsync()
        {
            if (Program.isHoloChannelSpider || Program.isDisconnect) return;
            //Log.Info("Holo影片清單整理開始");
            Program.isHoloChannelSpider = true;

            try
            {
                HtmlWeb htmlWeb = new HtmlWeb();
                HtmlDocument htmlDocument = htmlWeb.Load("https://schedule.hololive.tv/simple");
                var aList = htmlDocument.DocumentNode.Descendants().Where((x) => x.Name == "a");
                List<string> idList = new List<string>();
                foreach (var item in aList)
                {
                    string url = item.Attributes["href"].Value;
                    if (url.StartsWith("https://www.youtube.com/watch"))
                    {
                        string videoId = url.Split("?v=")[1].Trim();
                        if (!Extensions.HasStreamVideoByVideoId(videoId) && !newStreamList.Contains(videoId) && !addNewStreamVideo.ContainsKey(videoId)) idList.Add(videoId);
                        newStreamList.Add(videoId);
                    }
                }

                if (idList.Count > 0)
                {
                    Log.New($"Holo Id: {string.Join(", ", idList)}");

                    for (int i = 0; i < idList.Count; i += 50)
                    {
                        var video = yt.Videos.List("snippet,liveStreamingDetails");
                        video.Id = string.Join(",", idList.Skip(i).Take(50));
                        var videoResult = await video.ExecuteAsync().ConfigureAwait(false);
                        foreach (var item in videoResult.Items)
                        {
                            if (item.LiveStreamingDetails == null) //上傳影片
                            {
                                var streamVideo = new DataBase.Table.Video()
                                {
                                    ChannelId = item.Snippet.ChannelId,
                                    ChannelTitle = item.Snippet.ChannelTitle,
                                    VideoId = item.Id,
                                    VideoTitle = item.Snippet.Title,
                                    ScheduledStartTime = item.Snippet.PublishedAt.Value,
                                    ChannelType = DataBase.Table.Video.YTChannelType.Holo
                                };

                                Log.New($"(新影片) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                EmbedBuilder embedBuilder = new EmbedBuilder();
                                embedBuilder.WithOkColor()
                                .WithTitle(streamVideo.VideoTitle)
                                .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                .AddField("上傳時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo) && !isFirstHolo)
                                    await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.NewVideo).ConfigureAwait(false);
                            }
                            else if (item.LiveStreamingDetails.ScheduledStartTime != null) //首播 & 直播
                            {
                                var startTime = item.LiveStreamingDetails.ScheduledStartTime.Value;
                                var streamVideo = new DataBase.Table.Video()
                                {
                                    ChannelId = item.Snippet.ChannelId,
                                    ChannelTitle = item.Snippet.ChannelTitle,
                                    VideoId = item.Id,
                                    VideoTitle = item.Snippet.Title,
                                    ScheduledStartTime = startTime,
                                    ChannelType = DataBase.Table.Video.YTChannelType.Holo
                                };

                                Log.New($"(排程) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                if (startTime > DateTime.Now && startTime < DateTime.Now.AddDays(7))
                                {
                                    EmbedBuilder embedBuilder = new EmbedBuilder();
                                    embedBuilder.WithErrorColor()
                                    .WithTitle(streamVideo.VideoTitle)
                                    .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                    .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                    .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                    .AddField("直播狀態", "尚未開台")
                                    .AddField("排定開台時間", startTime.ConvertDateTimeToDiscordMarkdown());

                                    if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo))
                                    {
                                        if (!isFirstHolo) await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.NewStream).ConfigureAwait(false);
                                        StartReminder(streamVideo, streamVideo.ChannelType);
                                    }
                                }
                                else if (startTime > DateTime.Now.AddMinutes(-10) || item.Snippet.LiveBroadcastContent == "live") // 如果開台時間在十分鐘內或已經開台
                                {
                                    if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo))
                                        StartReminder(streamVideo, streamVideo.ChannelType);
                                }
                                else addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo);
                            }
                            else if (item.LiveStreamingDetails.ActualStartTime != null) //未排程直播
                            {
                                var startTime = item.LiveStreamingDetails.ActualStartTime.Value;
                                var streamVideo = new DataBase.Table.Video()
                                {
                                    ChannelId = item.Snippet.ChannelId,
                                    ChannelTitle = item.Snippet.ChannelTitle,
                                    VideoId = item.Id,
                                    VideoTitle = item.Snippet.Title,
                                    ScheduledStartTime = startTime,
                                    ChannelType = DataBase.Table.Video.YTChannelType.Holo
                                };

                                Log.New($"(未排程) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo) && item.Snippet.LiveBroadcastContent == "live")
                                    ReminderTimerAction(streamVideo);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!ex.Message.Contains("EOF or 0 bytes"))
                    Log.Error($"HoloStream: {ex}");
            }

            Program.isHoloChannelSpider = false; isFirstHolo = false;
            //Log.Info("Holo影片清單整理完成");
        }

        // 2434官網直播表: https://www.nijisanji.jp/streams
        // 直播資料路徑會變動，~~必須要從上面的網址直接解析Json~~
        // 發現有API網址了: https://www.nijisanji.jp/api/streams?day_offset=-1
        // day_offset是必要參數，可以設定-3~6
        // 2434成員資料
        // JP: https://www.nijisanji.jp/api/livers?limit=300&offset=0&orderKey=subscriber_count&order=desc&affiliation=nijisanji&locale=ja&includeHidden=true
        // EN: https://www.nijisanji.jp/api/livers?limit=300&offset=0&orderKey=subscriber_count&order=desc&affiliation=nijisanjien&locale=ja&includeHidden=true
        // VR: https://www.nijisanji.jp/api/livers?limit=300&offset=0&orderKey=subscriber_count&order=desc&affiliation=virtuareal&locale=ja&includeHidden=true
        // KR跟ID沒看到網站有請求
        private async Task NijisanjiScheduleAsync()
        {
            if (Program.isNijisanjiChannelSpider || Program.isDisconnect) return;
            //Log.Info("彩虹社影片清單整理開始");
            Program.isNijisanjiChannelSpider = true;
            using var httpClient = _httpClientFactory.CreateClient();

            try
            {
                List<Data> datas = new List<Data>();
                NijisanjiStreamJson nijisanjiStreamJson = null;
                try
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        string result = await httpClient.GetStringAsync($"https://www.nijisanji.jp/api/streams?day_offset={i}");
                        if (result.Contains("ERROR</h1>"))
                        {
                            Log.Warn("NijisanjiScheduleAsync: CloudFront回傳錯誤，略過");
                            Program.isNijisanjiChannelSpider = false;
                            return;
                        }
                        nijisanjiStreamJson = JsonConvert.DeserializeObject<NijisanjiStreamJson>(result);
                        datas.AddRange(nijisanjiStreamJson.Included);
                        datas.AddRange(nijisanjiStreamJson.Data);
                    }
                }
                catch (Exception ex)
                {
                    if (!ex.Message.Contains("EOF or 0 bytes") && !ex.Message.Contains("504") && !ex.Message.Contains("500"))
                        Log.Error($"NijisanjiScheduleAsync: {ex}");
                    Program.isNijisanjiChannelSpider = false;
                    return;
                }

                foreach (var item in datas)
                {
                    if (item.Type != "youtube_event")
                        continue;

                    string videoId = item.Attributes.Url.Split("?v=")[1].Trim(), channelTitle = "", liverId = null, externalId = null;
                    if (Extensions.HasStreamVideoByVideoId(videoId) || newStreamList.Contains(videoId) || addNewStreamVideo.ContainsKey(videoId)) continue;
                    newStreamList.Add(videoId);

                    var youtubeChannelData = datas.FirstOrDefault((x) => x.Type == "youtube_channel" && x.Id == item.Relationships.YoutubeChannel.Data.Id);
                    if (youtubeChannelData != null)
                    {
                        channelTitle = youtubeChannelData.Attributes.Name;
                        liverId = youtubeChannelData.Relationships?.Liver?.Data?.Id;
                        externalId = datas.FirstOrDefault((x) => x.Type == "liver" && x.Id == liverId).Attributes.ExternalId;
                    }

                    DataBase.Table.Video streamVideo = null;
                    if (!string.IsNullOrEmpty(externalId))
                    {
                        var channelData = NijisanjiLiverContents.FirstOrDefault((x) => x.id == externalId);
                        if (channelData != null)
                        {
                            if (string.IsNullOrEmpty(channelTitle))
                                channelTitle = $"{channelData.name} / {channelData.enName}";

                            streamVideo = new DataBase.Table.Video()
                            {
                                ChannelId = channelData.socialLinks.youtube.Replace("https://www.youtube.com/channel/", ""),
                                ChannelTitle = channelTitle,
                                VideoId = videoId,
                                VideoTitle = item.Attributes.Title,
                                ScheduledStartTime = item.Attributes.StartAt.Value,
                                ChannelType = DataBase.Table.Video.YTChannelType.Nijisanji
                            };
                        }
                    }

                    if (streamVideo == null)
                    {
                        var video = await GetVideoAsync(videoId);
                        streamVideo = new DataBase.Table.Video()
                        {
                            ChannelId = video.Snippet.ChannelId,
                            ChannelTitle = video.Snippet.ChannelTitle,
                            VideoId = videoId,
                            VideoTitle = item.Attributes.Title,
                            ScheduledStartTime = item.Attributes.StartAt.Value,
                            ChannelType = DataBase.Table.Video.YTChannelType.Nijisanji
                        };

                        Log.Warn($"檢測到無Liver資料的頻道({videoId}): `{video.Snippet.ChannelTitle}` / {item.Attributes.Title}");
                        Log.Warn("重新刷新Liver資料清單");

                        NijisanjiLiverContents.Clear();
                        foreach (var affiliation in new string[] { "nijisanji", "nijisanjien", "virtuareal" })
                        {
                            await Task.Run(async () => await GetOrCreateNijisanjiLiverListAsync(affiliation, true));
                        }
                    }

                    Log.New($"(排程) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                    if (item.Attributes.StartAt > DateTime.Now.AddMinutes(-10)) // 如果開台時間在十分鐘內
                    {
                        EmbedBuilder embedBuilder = new EmbedBuilder();
                        embedBuilder.WithErrorColor()
                        .WithTitle(streamVideo.VideoTitle)
                        .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                        .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                        .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                        .AddField("直播狀態", "尚未開台")
                        .AddField("排定開台時間", item.Attributes.StartAt.Value.ConvertDateTimeToDiscordMarkdown());

                        if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo))
                        {
                            if (!isFirst2434) await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.NewStream).ConfigureAwait(false);
                            StartReminder(streamVideo, streamVideo.ChannelType);
                        }
                    }
                    else if (item.Attributes.Status == "on_air")
                    {
                        if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo))
                            StartReminder(streamVideo, streamVideo.ChannelType);
                    }
                    else addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"NijisanjiScheduleAsync: {ex}");
            }

            Program.isNijisanjiChannelSpider = false; isFirst2434 = false;
            //Log.Info("彩虹社影片清單整理完成");
        }

        // Todo: BlockingCollection應用 (但還不知道要用甚麼)
        // 應該是不能用，為了降低API配額消耗，所以必須取得全部的VideoId後再一次性的跟API要資料
        // https://blog.darkthread.net/blog/blockingcollection/
        // https://docs.microsoft.com/en-us/dotnet/standard/collections/thread-safe/blockingcollection-overview
        private async Task OtherScheduleAsync()
        {
            if (Program.isOtherChannelSpider || Program.isDisconnect) return;

#if RELEASE
            try
            {
                if (Program.RedisDb.KeyExists("youtube.otherStart"))
                {
                    var time = await Program.RedisDb.KeyTimeToLiveAsync("youtube.otherStart");
                    Log.Warn($"已跑過突襲開台檢測爬蟲，剩餘 {time:mm\\:ss}");
                    isFirstOther = false;
                    return;
                }
            }
            catch
            {
                Log.Error("Redis又死了zzz");
            }
#endif

            await Program.RedisDb.StringSetAsync("youtube.otherStart", "0", TimeSpan.FromMinutes(4));
            Program.isOtherChannelSpider = true;
            Dictionary<string, List<string>> otherVideoDic = new Dictionary<string, List<string>>();
            var addVideoIdList = new List<string>();

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var channelList = db.YoutubeChannelSpider.Where((x) => db.RecordYoutubeChannel.Any((x2) => x.ChannelId == x2.YoutubeChannelId));
                using var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");
                httpClient.DefaultRequestHeaders.Add("AcceptLanguage", "zh-TW");

                Log.Info($"突襲開台檢測開始: {channelList.Count()}頻道");
                foreach (var item in channelList)
                {
                    if (Program.isDisconnect) break;

                    try
                    {
                        if (item.ChannelTitle == null)
                        {
                            var ytChannel = yt.Channels.List("snippet");
                            ytChannel.Id = item.ChannelId;
                            item.ChannelTitle = (await ytChannel.ExecuteAsync().ConfigureAwait(false)).Items[0].Snippet.Title;
                            db.YoutubeChannelSpider.Update(item);
                            db.SaveChanges();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"OtherUpdateChannelTitle {item}");
                        Log.Error($"{ex}");
                    }

                    string videoId = "";

                    foreach (var type in new string[] { "videos", "streams" })
                    {
                        try
                        {
                            var response = await httpClient.GetStringAsync($"https://www.youtube.com/channel/{item.ChannelId}/{type}");

                            Regex regex;
                            if (response.Contains("window[\"ytInitialData\"]"))
                                regex = new Regex("window\\[\"ytInitialData\"\\] = (.*);");
                            else
                                regex = new Regex(">var ytInitialData = (.*?);</script>");

                            var group = regex.Match(response).Groups[1];
                            var jObject = JObject.Parse(group.Value);

                            List<JToken> videoList = new List<JToken>();
                            videoList.AddRange(jObject.Descendants().Where((x) => x.ToString().StartsWith("\"gridVideoRenderer")));
                            videoList.AddRange(jObject.Descendants().Where((x) => x.ToString().StartsWith("\"videoRenderer")));

                            if (!otherVideoDic.ContainsKey(item.ChannelId)) otherVideoDic.Add(item.ChannelId, new List<string>());

                            foreach (var item2 in videoList)
                            {
                                try
                                {
                                    videoId = JObject.Parse(item2.ToString().Substring(item2.ToString().IndexOf("{")))["videoId"].ToString();

                                    if (!otherVideoDic[item.ChannelId].Contains(videoId))
                                    {
                                        otherVideoDic[item.ChannelId].Add(videoId);
                                        if (!Extensions.HasStreamVideoByVideoId(videoId) && !newStreamList.Contains(videoId) && !addNewStreamVideo.ContainsKey(videoId)) addVideoIdList.Add(videoId);
                                        newStreamList.Add(videoId);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"OtherSchedule {item.ChannelId} - {type}: GetVideoId");
                                    Log.Error(ex.ToString());
                                }
                            }

                            if (!response.Contains($"/channel/{item.ChannelId}/streams")) // 這行應該是判定如果沒有直播頁籤的話就直接跳出迴圈?
                                break;
                        }
                        catch (Exception ex)
                        {
                            try { otherVideoDic[item.ChannelId].Remove(videoId); }
                            catch (Exception) { }
                            Log.Error($"OtherSchedule {item.ChannelId} - {type}: GetVideoList");
                            Log.Error($"{ex}");
                        }
                    }
                }

                for (int i = 0; i < addVideoIdList.Count; i += 50)
                {
                    if (Program.isDisconnect) break;

                    IEnumerable<Video> videos;
                    try
                    {
                        videos = await GetVideosAsync(addVideoIdList.Skip(i).Take(50));
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"OtherSchedule-GetVideosAsync: {ex}");
                        Program.isOtherChannelSpider = false;
                        return;
                    }

                    foreach (var item in videos)
                    {
                        try
                        {
                            await AddOtherDataAsync(item);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"OtherAddSchedule {item.Id}");
                            Log.Error($"{ex}");
                        }
                    }
                }
            }

            Program.isOtherChannelSpider = false; isFirstOther = false;
            //Log.Info("其他勢影片清單整理完成");
        }

        public async Task AddOtherDataAsync(Video item, bool isFromRNRS = false)
        {
            if (item.LiveStreamingDetails == null)
            {
                var streamVideo = new DataBase.Table.Video()
                {
                    ChannelId = item.Snippet.ChannelId,
                    ChannelTitle = item.Snippet.ChannelTitle,
                    VideoId = item.Id,
                    VideoTitle = item.Snippet.Title,
                    ScheduledStartTime = item.Snippet.PublishedAt.Value,
                    ChannelType = DataBase.Table.Video.YTChannelType.Other
                };

                streamVideo.ChannelType = streamVideo.GetProductionType();
                Log.New($"(新影片) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                EmbedBuilder embedBuilder = new EmbedBuilder();
                embedBuilder.WithOkColor()
                .WithTitle(streamVideo.VideoTitle)
                .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                .AddField("上傳時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo) && !isFirstOther && !isFromRNRS && streamVideo.ScheduledStartTime > DateTime.Now.AddDays(-2))
                    await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.NewVideo).ConfigureAwait(false);
            }
            else if (item.LiveStreamingDetails.ScheduledStartTime != null)
            {
                var startTime = item.LiveStreamingDetails.ScheduledStartTime.Value;
                var streamVideo = new DataBase.Table.Video()
                {
                    ChannelId = item.Snippet.ChannelId,
                    ChannelTitle = item.Snippet.ChannelTitle,
                    VideoId = item.Id,
                    VideoTitle = item.Snippet.Title,
                    ScheduledStartTime = startTime,
                    ChannelType = DataBase.Table.Video.YTChannelType.Other
                };

                streamVideo.ChannelType = streamVideo.GetProductionType();
                Log.New($"(排程) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                if (startTime > DateTime.Now && startTime < DateTime.Now.AddDays(7))
                {
                    EmbedBuilder embedBuilder = new EmbedBuilder();
                    embedBuilder.WithErrorColor()
                    .WithTitle(streamVideo.VideoTitle)
                    .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                    .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                    .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                    .AddField("直播狀態", "尚未開台", true)
                    .AddField("排定開台時間", startTime.ConvertDateTimeToDiscordMarkdown());

                    if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo) && !isFromRNRS)
                    {
                        if (!isFirstOther) await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.NewStream).ConfigureAwait(false);
                        StartReminder(streamVideo, streamVideo.ChannelType);
                    }
                }
                else if (startTime > DateTime.Now.AddMinutes(-10) || item.Snippet.LiveBroadcastContent == "live") // 如果開台時間在十分鐘內或已經開台
                {
                    if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo) && !isFromRNRS)
                        StartReminder(streamVideo, streamVideo.ChannelType);
                }
                else addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo);
            }
            else if (item.LiveStreamingDetails.ActualStartTime != null) //未排程直播
            {
                var startTime = item.LiveStreamingDetails.ActualStartTime.Value;
                var streamVideo = new DataBase.Table.Video()
                {
                    ChannelId = item.Snippet.ChannelId,
                    ChannelTitle = item.Snippet.ChannelTitle,
                    VideoId = item.Id,
                    VideoTitle = item.Snippet.Title,
                    ScheduledStartTime = startTime,
                    ChannelType = DataBase.Table.Video.YTChannelType.Other
                };

                streamVideo.ChannelType = streamVideo.GetProductionType();
                Log.New($"(未排程) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo) && item.Snippet.LiveBroadcastContent == "live" && !isFromRNRS)
                    ReminderTimerAction(streamVideo);
            }
            else if (item.LiveStreamingDetails.ActualStartTime == null && item.LiveStreamingDetails.ActiveLiveChatId != null)
            {
                var streamVideo = new DataBase.Table.Video()
                {
                    ChannelId = item.Snippet.ChannelId,
                    ChannelTitle = item.Snippet.ChannelTitle,
                    VideoId = item.Id,
                    VideoTitle = item.Snippet.Title,
                    ScheduledStartTime = item.Snippet.PublishedAt.Value,
                    ChannelType = DataBase.Table.Video.YTChannelType.Other
                };

                Log.New($"(一般路過的新直播室) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");
                addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo);
            }
        }

        public static void SaveDateBase()
        {
            int saveNum = 0;

            try
            {
                if (!Program.isHoloChannelSpider && addNewStreamVideo.Any((x) => x.Value.ChannelType == DataBase.Table.Video.YTChannelType.Holo))
                {
                    using (var db = DataBase.HoloVideoContext.GetDbContext())
                    {
                        foreach (var item in addNewStreamVideo.Where((x) => x.Value.ChannelType == DataBase.Table.Video.YTChannelType.Holo))
                        {
                            if (!db.Video.Any((x) => x.VideoId == item.Key))
                            {
                                try
                                {
                                    db.Video.Add(item.Value); saveNum++;
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"SaveHoloDateBase {ex}");
                                }
                            }

                            addNewStreamVideo.Remove(item.Key);
                        }

                        Log.Info($"Holo資料庫已儲存: {db.SaveChanges()}筆");
                    }
                }

                if (!Program.isNijisanjiChannelSpider && addNewStreamVideo.Any((x) => x.Value.ChannelType == DataBase.Table.Video.YTChannelType.Nijisanji))
                {
                    using (var db = DataBase.NijisanjiVideoContext.GetDbContext())
                    {
                        foreach (var item in addNewStreamVideo.Where((x) => x.Value.ChannelType == DataBase.Table.Video.YTChannelType.Nijisanji))
                        {
                            if (!db.Video.Any((x) => x.VideoId == item.Key))
                            {
                                try
                                {
                                    db.Video.Add(item.Value); saveNum++;
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"Save2434DateBase {ex}");
                                }
                            }

                            addNewStreamVideo.Remove(item.Key);
                        }

                        Log.Info($"2434資料庫已儲存: {db.SaveChanges()}筆");
                    }
                }

                if (!Program.isOtherChannelSpider && addNewStreamVideo.Any((x) => x.Value.ChannelType == DataBase.Table.Video.YTChannelType.Other))
                {
                    using (var db = DataBase.OtherVideoContext.GetDbContext())
                    {
                        foreach (var item in addNewStreamVideo.Where((x) => x.Value.ChannelType == DataBase.Table.Video.YTChannelType.Other))
                        {
                            if (!db.Video.Any((x) => x.VideoId == item.Key))
                            {
                                try
                                {
                                    db.Video.Add(item.Value); saveNum++;
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"SaveOtherDateBase {ex}");
                                }
                            }

                            addNewStreamVideo.Remove(item.Key);
                        }

                        Log.Info($"Other資料庫已儲存: {db.SaveChanges()}筆");
                    }
                }

                if (addNewStreamVideo.Any((x) => x.Value.ChannelType == DataBase.Table.Video.YTChannelType.NonApproved))
                {
                    using (var db = DataBase.NotVTuberVideoContext.GetDbContext())
                    {
                        foreach (var item in addNewStreamVideo.Where((x) => x.Value.ChannelType == DataBase.Table.Video.YTChannelType.NonApproved))
                        {
                            if (!db.Video.Any((x) => x.VideoId == item.Key))
                            {
                                try
                                {
                                    db.Video.Add(item.Value); saveNum++;
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"SaveNotVTuberDateBase {ex}");
                                }
                            }

                            addNewStreamVideo.Remove(item.Key);
                        }

                        Log.Info($"NotVTuber資料庫已儲存: {db.SaveChanges()}筆");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SaveDateBase {ex}");
            }

            if (saveNum != 0) Log.Info($"資料庫已儲存完畢: {saveNum}筆");
        }
    }
}