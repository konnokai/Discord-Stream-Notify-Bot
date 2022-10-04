using Discord;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
            if (Program.isHoloChannelSpider) return;
            //Log.Info("Holo影片清單整理開始");
            Program.isHoloChannelSpider = true;

            try
            {
                HtmlWeb htmlWeb = new HtmlWeb();
                HtmlDocument htmlDocument = htmlWeb.Load("https://schedule.hololive.tv/simple");
                var aList = htmlDocument.DocumentNode.Descendants().Where((x) => x.Name == "a");
                List<string> idList = new List<string>();
                using (var db = DataBase.DBContext.GetDbContext())
                {
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
                        Log.NewStream($"Holo Id: {string.Join(", ", idList)}");

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

                                    Log.NewStream($"(新影片) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                    EmbedBuilder embedBuilder = new EmbedBuilder();
                                    embedBuilder.WithOkColor()
                                    .WithTitle(streamVideo.VideoTitle)
                                    .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                    .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                    .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                    .AddField("所屬", streamVideo.GetProductionType().GetProductionName(), true)
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

                                    Log.NewStream($"(排程) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                    if (startTime > DateTime.Now && startTime < DateTime.Now.AddDays(7))
                                    {
                                        EmbedBuilder embedBuilder = new EmbedBuilder();
                                        embedBuilder.WithErrorColor()
                                        .WithTitle(streamVideo.VideoTitle)
                                        .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                        .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                        .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                        .AddField("所屬", streamVideo.GetProductionType().GetProductionName(), true)
                                        .AddField("直播狀態", "尚未開台", true)
                                        .AddField("排定開台時間", startTime.ConvertDateTimeToDiscordMarkdown());
                                        //.AddField("是否記錄直播", (CanRecord(db, streamVideo) ? "是" : "否"), true);

                                        if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo))
                                        {
                                            if (!isFirstHolo) await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.NewStream).ConfigureAwait(false);
                                            StartReminder(streamVideo, streamVideo.ChannelType);
                                        }
                                    }
                                    else if (item.Snippet.LiveBroadcastContent == "live")
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

                                    Log.NewStream($"(未排程) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                    if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo) && item.Snippet.LiveBroadcastContent == "live")
                                        ReminderTimerAction(streamVideo);
                                }
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

        private async Task NijisanjiScheduleAsync()
        {
            if (Program.isNijisanjiChannelSpider) return;
            //Log.Info("彩虹社影片清單整理開始");
            Program.isNijisanjiChannelSpider = true;
            using var httpClient = _httpClientFactory.CreateClient();

            try
            {
                NijisanjiJson nijisanjiJson = null;
                try
                {
                    string json = await httpClient.GetStringAsync("https://api.itsukaralink.jp/v1.2/events.json");
                    nijisanjiJson = JsonConvert.DeserializeObject<NijisanjiJson>(json);
                }
                catch (Exception ex)
                {
                    if (!ex.Message.Contains("EOF or 0 bytes") && !ex.Message.Contains("504") && !ex.Message.Contains("500"))
                        Log.Error($"NijisanjiStream: {ex}");
                    Program.isNijisanjiChannelSpider = false;
                    return;
                }

                List<string> idList = new List<string>();

                using (var db = DataBase.DBContext.GetDbContext())
                {
                    foreach (var item in nijisanjiJson.data.events)
                    {
                        string videoId = item.url.Split("?v=")[1].Trim();
                        if (!Extensions.HasStreamVideoByVideoId(videoId) && !newStreamList.Contains(videoId) && !addNewStreamVideo.ContainsKey(videoId)) idList.Add(videoId);
                        newStreamList.Add(videoId);
                    }

                    if (idList.Count > 0)
                    {
                        Log.NewStream($"Nijisanji Id: {string.Join(", ", idList)}");

                        for (int i = 0; i < idList.Count; i += 50)
                        {
                            var video = yt.Videos.List("snippet,liveStreamingDetails");
                            video.Id = string.Join(",", idList.Skip(i).Take(50));
                            var videoResult = await video.ExecuteAsync().ConfigureAwait(false);

                            foreach (var item in videoResult.Items)
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
                                        ChannelType = DataBase.Table.Video.YTChannelType.Nijisanji
                                    };

                                    Log.NewStream($"(新影片) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                    EmbedBuilder embedBuilder = new EmbedBuilder();
                                    embedBuilder.WithOkColor()
                                    .WithTitle(streamVideo.VideoTitle)
                                    .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                    .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                    .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                    .AddField("所屬", streamVideo.GetProductionType().GetProductionName(), true)
                                    .AddField("上傳時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                    if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo) && !isFirst2434)
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
                                        ChannelType = DataBase.Table.Video.YTChannelType.Nijisanji
                                    };

                                    Log.NewStream($"(排程) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                    if (startTime > DateTime.Now && startTime < DateTime.Now.AddDays(7))
                                    {
                                        EmbedBuilder embedBuilder = new EmbedBuilder();
                                        embedBuilder.WithErrorColor()
                                        .WithTitle(streamVideo.VideoTitle)
                                        .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                        .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                        .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                        .AddField("所屬", streamVideo.GetProductionType().GetProductionName(), true)
                                        .AddField("直播狀態", "尚未開台", true)
                                        .AddField("排定開台時間", startTime.ConvertDateTimeToDiscordMarkdown());
                                        //.AddField("是否記錄直播", (CanRecord(db, streamVideo) ? "是" : "否"), true);

                                        if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo))
                                        {
                                            if (!isFirst2434) await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.NewStream).ConfigureAwait(false);
                                            StartReminder(streamVideo, streamVideo.ChannelType);
                                        }
                                    }
                                    else if (item.Snippet.LiveBroadcastContent == "live")
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
                                        ChannelType = DataBase.Table.Video.YTChannelType.Nijisanji
                                    };

                                    Log.NewStream($"(未排程) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                    if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo) && item.Snippet.LiveBroadcastContent == "live")
                                        ReminderTimerAction(streamVideo);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"NijisanjiStream: {ex}");
            }

            Program.isNijisanjiChannelSpider = false; isFirst2434 = false;
            //Log.Info("彩虹社影片清單整理完成");
        }

        //Todo: BlockingCollection應用 (但還不知道要用甚麼)
        //https://blog.darkthread.net/blog/blockingcollection/
        //https://docs.microsoft.com/en-us/dotnet/standard/collections/thread-safe/blockingcollection-overview
        private async Task OtherScheduleAsync()
        {
            if (Program.isOtherChannelSpider) return;

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

            await Program.RedisDb.StringSetAsync("youtube.otherStart", "0", TimeSpan.FromMinutes(4));
            Program.isOtherChannelSpider = true;
            Dictionary<string, List<string>> otherVideoDic = new Dictionary<string, List<string>>();
            var addVideoIdList = new List<string>();

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var channelList = db.YoutubeChannelSpider.Where((x) => db.RecordYoutubeChannel.Any((x2) => x.ChannelId == x2.YoutubeChannelId));
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
                    try
                    {
                        List<JToken> videoList = new List<JToken>();
                        using var httpClient = _httpClientFactory.CreateClient();

                        httpClient.DefaultRequestHeaders.Add("User-Agent","Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/79.0.3945.130 Safari/537.36");
                        httpClient.DefaultRequestHeaders.Add("AcceptLanguage", "zh-TW");

                        Regex regex;
                        var response = await httpClient.GetStringAsync($"https://www.youtube.com/channel/{item.ChannelId}/videos?view=57&flow=grid");

                        if (response.Contains("window[\"ytInitialData\"]"))
                            regex = new Regex("window\\[\"ytInitialData\"\\] = (.*);");
                        else
                            regex = new Regex(">var ytInitialData = (.*?);</script>");

                        var group = regex.Match(response).Groups[1];
                        var jObject = JObject.Parse(group.Value);

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
                                Log.Error(ex.ToString());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        try { otherVideoDic[item.ChannelId].Remove(videoId); }
                        catch (Exception) { }
                        Log.Error($"OtherSchedule {item.ChannelId}");
                        Log.Error($"{ex}");
                    }
                }

                for (int i = 0; i < addVideoIdList.Count; i += 50)
                {
                    if (Program.isDisconnect) break;

                    IEnumerable<Google.Apis.YouTube.v3.Data.Video> videos;
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

        public async Task AddOtherDataAsync(Google.Apis.YouTube.v3.Data.Video item, bool isFromPubSub = false)
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
                Log.NewStream($"(新影片) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                EmbedBuilder embedBuilder = new EmbedBuilder();
                embedBuilder.WithOkColor()
                .WithTitle(streamVideo.VideoTitle)
                .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                .AddField("所屬", streamVideo.GetProductionType().GetProductionName(), true)
                .AddField("上傳時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo) && !isFirstOther)
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
                Log.NewStream($"(排程) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                if (startTime > DateTime.Now && startTime < DateTime.Now.AddDays(7))
                {
                    EmbedBuilder embedBuilder = new EmbedBuilder();
                    embedBuilder.WithErrorColor()
                    .WithTitle(streamVideo.VideoTitle)
                    .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                    .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                    .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                    .AddField("所屬", streamVideo.GetProductionType().GetProductionName(), true)
                    .AddField("直播狀態", "尚未開台", true)
                    .AddField("排定開台時間", startTime.ConvertDateTimeToDiscordMarkdown());
                    //.AddField("是否記錄直播", (CanRecord(db, streamVideo) ? "是" : "否"), true);

                    if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo))
                    {
                        if (!isFirstOther) await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.NewStream).ConfigureAwait(false);
                        StartReminder(streamVideo, streamVideo.ChannelType);
                    }
                }
                else if (item.Snippet.LiveBroadcastContent == "live")
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
                    ChannelType = DataBase.Table.Video.YTChannelType.Other
                };

                streamVideo.ChannelType = streamVideo.GetProductionType();
                Log.NewStream($"(未排程) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo) && item.Snippet.LiveBroadcastContent == "live")
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

                Log.NewStream($"(一般路過的新直播室) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");
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

                if (addNewStreamVideo.Any((x) => x.Value.ChannelType == DataBase.Table.Video.YTChannelType.NotVTuber))
                {
                    using (var db = DataBase.NotVTuberVideoContext.GetDbContext())
                    {
                        foreach (var item in addNewStreamVideo.Where((x) => x.Value.ChannelType == DataBase.Table.Video.YTChannelType.NotVTuber))
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