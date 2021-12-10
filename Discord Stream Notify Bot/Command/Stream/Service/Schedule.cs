using Discord;
using Discord_Stream_Notify_Bot.DataBase.Table;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Data;

namespace Discord_Stream_Notify_Bot.Command.Stream.Service
{
    public partial class StreamService
    {
        private static Dictionary<StreamVideo, ChannelType> addNewStreamVideo = new();
        private bool isFirstHolo = true, isFirst2434 = true, isFirstOther = true;

        private async Task HoloScheduleAsync()
        {
            if (Program.isHoloChannelSpider) return;
            Log.Info("Holo影片清單整理開始");
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
                            if (!db.HasStreamVideoByVideoId(videoId)) idList.Add(videoId);
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
                                    var streamVideo = new StreamVideo()
                                    {
                                        ChannelId = item.Snippet.ChannelId,
                                        ChannelTitle = item.Snippet.ChannelTitle,
                                        VideoId = item.Id,
                                        VideoTitle = item.Snippet.Title,
                                        ScheduledStartTime = item.Snippet.PublishedAt.Value,
                                        ChannelType = ChannelType.Holo
                                    };

                                    Log.NewStream($"(新影片) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                    EmbedBuilder embedBuilder = new EmbedBuilder();
                                    embedBuilder.WithOkColor()
                                    .WithTitle(streamVideo.VideoTitle)
                                    .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                    .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                    .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                    .AddField("所屬", streamVideo.GetProductionType().GetProductionName())
                                    .AddField("上傳時間", item.Snippet.PublishedAt.Value, true);

                                    if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType) && !isFirstHolo)
                                        await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.NewVideo).ConfigureAwait(false);
                                }
                                else if (item.LiveStreamingDetails.ScheduledStartTime != null) //首播 & 直播
                                {
                                    var startTime = item.LiveStreamingDetails.ScheduledStartTime.Value;
                                    var streamVideo = new StreamVideo()
                                    {
                                        ChannelId = item.Snippet.ChannelId,
                                        ChannelTitle = item.Snippet.ChannelTitle,
                                        VideoId = item.Id,
                                        VideoTitle = item.Snippet.Title,
                                        ScheduledStartTime = startTime,
                                        ChannelType = ChannelType.Holo
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
                                        .AddField("所屬", streamVideo.GetProductionType().GetProductionName())
                                        .AddField("直播狀態", "尚未開台", true)
                                        .AddField("排定開台時間", startTime, true);
                                        //.AddField("是否記錄直播", (CanRecord(db, streamVideo) ? "是" : "否"), true);

                                        if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
                                        {
                                            if (!isFirstHolo) await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.NewStream).ConfigureAwait(false);
                                            StartReminder(streamVideo, streamVideo.ChannelType);
                                        }
                                    }
                                    else if (item.Snippet.LiveBroadcastContent == "live")
                                    {
                                        if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
                                            StartReminder(streamVideo, streamVideo.ChannelType);
                                    }
                                    else addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType);
                                }
                                else if (item.LiveStreamingDetails.ActualStartTime != null) //未排程直播
                                {
                                    var startTime = item.LiveStreamingDetails.ActualStartTime.Value;
                                    var streamVideo = new StreamVideo()
                                    {
                                        ChannelId = item.Snippet.ChannelId,
                                        ChannelTitle = item.Snippet.ChannelTitle,
                                        VideoId = item.Id,
                                        VideoTitle = item.Snippet.Title,
                                        ScheduledStartTime = startTime,
                                        ChannelType = ChannelType.Holo
                                    };

                                    Log.NewStream($"(未排程) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                    if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType) && item.Snippet.LiveBroadcastContent == "live")
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
                    Log.Error("HoloStream\r\n" + ex.Message + "\r\n" + ex.StackTrace);
            }

            Program.isHoloChannelSpider = false; isFirstHolo = false;
            Log.Info("Holo影片清單整理完成");
        }

        private async Task NijisanjiScheduleAsync()
        {
            if (Program.isNijisanjiChannelSpider) return;
            Log.Info("彩虹社影片清單整理開始");
            Program.isNijisanjiChannelSpider = true;

            try
            {
                using (WebClient webClient = new WebClient())
                {
                    NijisanjiJson nijisanjiJson = null;
                    try
                    {
                        string json = await webClient.DownloadStringTaskAsync("https://api.itsukaralink.jp/v1.2/events.json").ConfigureAwait(false);
                        nijisanjiJson = JsonConvert.DeserializeObject<NijisanjiJson>(json);
                    }
                    catch (Exception ex)
                    {
                        if (!ex.Message.Contains("EOF or 0 bytes") && !ex.Message.Contains("504") && !ex.Message.Contains("500"))
                            Log.Error("NijisanjiStream\r\n" + ex.Message + "\r\n" + ex.StackTrace);
                        Program.isNijisanjiChannelSpider = false;
                        return;
                    }

                    List<string> idList = new List<string>();

                    using (var db = DataBase.DBContext.GetDbContext())
                    {
                        foreach (var item in nijisanjiJson.data.events)
                        {
                            string videoId = item.url.Split("?v=")[1].Trim();
                            if (!db.HasStreamVideoByVideoId(videoId)) idList.Add(videoId);
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
                                        var streamVideo = new StreamVideo()
                                        {
                                            ChannelId = item.Snippet.ChannelId,
                                            ChannelTitle = item.Snippet.ChannelTitle,
                                            VideoId = item.Id,
                                            VideoTitle = item.Snippet.Title,
                                            ScheduledStartTime = item.Snippet.PublishedAt.Value,
                                            ChannelType = ChannelType.Nijisanji
                                        };

                                        Log.NewStream($"(新影片) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                        EmbedBuilder embedBuilder = new EmbedBuilder();
                                        embedBuilder.WithOkColor()
                                        .WithTitle(streamVideo.VideoTitle)
                                        .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                        .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                        .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                        .AddField("所屬", streamVideo.GetProductionType().GetProductionName())
                                        .AddField("上傳時間", item.Snippet.PublishedAt.Value, true);

                                        if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType) && !isFirst2434)
                                            await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.NewVideo).ConfigureAwait(false);
                                    }
                                    else if (item.LiveStreamingDetails.ScheduledStartTime != null)
                                    {
                                        var startTime = item.LiveStreamingDetails.ScheduledStartTime.Value;
                                        var streamVideo = new StreamVideo()
                                        {
                                            ChannelId = item.Snippet.ChannelId,
                                            ChannelTitle = item.Snippet.ChannelTitle,
                                            VideoId = item.Id,
                                            VideoTitle = item.Snippet.Title,
                                            ScheduledStartTime = startTime,
                                            ChannelType = ChannelType.Nijisanji
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
                                            .AddField("所屬", streamVideo.GetProductionType().GetProductionName())
                                            .AddField("直播狀態", "尚未開台", true)
                                            .AddField("排定開台時間", startTime, true);
                                            //.AddField("是否記錄直播", (CanRecord(db, streamVideo) ? "是" : "否"), true);

                                            if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
                                            {
                                                if (!isFirst2434) await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.NewStream).ConfigureAwait(false);
                                                StartReminder(streamVideo, streamVideo.ChannelType);
                                            }
                                        }
                                        else if (item.Snippet.LiveBroadcastContent == "live")
                                        {
                                            if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
                                                StartReminder(streamVideo, streamVideo.ChannelType);
                                        }
                                        else addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType);
                                    }
                                    else if (item.LiveStreamingDetails.ActualStartTime != null) //未排程直播
                                    {
                                        var startTime = item.LiveStreamingDetails.ActualStartTime.Value;
                                        var streamVideo = new StreamVideo()
                                        {
                                            ChannelId = item.Snippet.ChannelId,
                                            ChannelTitle = item.Snippet.ChannelTitle,
                                            VideoId = item.Id,
                                            VideoTitle = item.Snippet.Title,
                                            ScheduledStartTime = startTime,
                                            ChannelType = ChannelType.Nijisanji
                                        };

                                        Log.NewStream($"(未排程) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                        if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType) && item.Snippet.LiveBroadcastContent == "live")
                                            ReminderTimerAction(streamVideo);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("NijisanjiStream\r\n" + ex.Message + "\r\n" + ex.StackTrace);
            }

            Program.isNijisanjiChannelSpider = false; isFirst2434 = false;
            Log.Info("彩虹社影片清單整理完成");
        }

        private async Task OtherScheduleAsync()
        {
            if (Program.isOtherChannelSpider) return;
            Log.Info("其他勢影片清單整理開始");
            Program.isOtherChannelSpider = true;
            Dictionary<string, List<string>> otherVideoDic = new Dictionary<string, List<string>>();

            using (var db = DataBase.DBContext.GetDbContext())
            {
                foreach (var item in db.YoutubeChannelSpider)
                {
                    try
                    {
                        if (item.ChannelTitle == null)
                        {
                            var ytChannel = yt.Channels.List("snippet");
                            ytChannel.Id = item.ChannelId;
                            item.ChannelTitle = (await ytChannel.ExecuteAsync().ConfigureAwait(false)).Items[0].Snippet.Title;
                            db.YoutubeChannelSpider.Update(item);
                            await db.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"OtherUpdateChannelTitle {item}");
                        Log.Error($"{ex.Message}\r\n{ex.StackTrace}");
                    }

                    string videoId = "";
                    try
                    {
                        List<JToken> videoList = new List<JToken>();

                        using (WebClient web = new WebClient())
                        {
                            web.Headers.Add(HttpRequestHeader.UserAgent, "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/79.0.3945.130 Safari/537.36");
                            web.Headers.Add(HttpRequestHeader.AcceptLanguage, "zh-TW");

                            Regex regex;
                            var response = await web.DownloadStringTaskAsync($"https://www.youtube.com/channel/{item.ChannelId}/videos?view=57&flow=grid");

                            if (response.Contains("window[\"ytInitialData\"]"))
                                regex = new Regex("window\\[\"ytInitialData\"\\] = (.*);");
                            else
                                regex = new Regex(">var ytInitialData = (.*?);</script>");

                            var group = regex.Match(response).Groups[1];
                            var jObject = JObject.Parse(group.Value);

                            videoList.AddRange(jObject.Descendants().Where((x) => x.ToString().StartsWith("\"gridVideoRenderer")));
                            videoList.AddRange(jObject.Descendants().Where((x) => x.ToString().StartsWith("\"videoRenderer")));

                            if (!otherVideoDic.ContainsKey(item.ChannelId)) otherVideoDic.Add(item.ChannelId, new List<string>());

                            var addVideoIdList = new List<string>();
                            foreach (var item2 in videoList)
                            {
                                try
                                {
                                    videoId = JObject.Parse(item2.ToString().Substring(item2.ToString().IndexOf("{")))["videoId"].ToString();

                                    if (!otherVideoDic[item.ChannelId].Contains(videoId))
                                    {
                                        otherVideoDic[item.ChannelId].Add(videoId);
                                        if (!db.HasStreamVideoByVideoId(videoId)) addVideoIdList.Add(videoId);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(ex.Message);
                                    Console.WriteLine(ex.StackTrace);
                                }
                            }

                            for (int i = 0; i < addVideoIdList.Count; i += 50)
                            {
                                foreach (var item2 in await GetVideosAsync(addVideoIdList.Skip(i).Take(50)))
                                {
                                    try
                                    {
                                        if (item2.LiveStreamingDetails == null)
                                        {
                                            var streamVideo = new StreamVideo()
                                            {
                                                ChannelId = item2.Snippet.ChannelId,
                                                ChannelTitle = item2.Snippet.ChannelTitle,
                                                VideoId = item2.Id,
                                                VideoTitle = item2.Snippet.Title,
                                                ScheduledStartTime = item2.Snippet.PublishedAt.Value,
                                                ChannelType = ChannelType.Other
                                            };

                                            streamVideo.ChannelType = streamVideo.GetProductionType();
                                            Log.NewStream($"(新影片) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                            EmbedBuilder embedBuilder = new EmbedBuilder();
                                            embedBuilder.WithOkColor()
                                            .WithTitle(streamVideo.VideoTitle)
                                            .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                            .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                            .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                            .AddField("所屬", streamVideo.GetProductionType().GetProductionName())
                                            .AddField("上傳時間", streamVideo.ScheduledStartTime, true);

                                            if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType) && !isFirstOther)
                                                await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.NewVideo).ConfigureAwait(false);
                                        }
                                        else if (item2.LiveStreamingDetails.ScheduledStartTime != null)
                                        {
                                            var startTime = item2.LiveStreamingDetails.ScheduledStartTime.Value;
                                            var streamVideo = new StreamVideo()
                                            {
                                                ChannelId = item2.Snippet.ChannelId,
                                                ChannelTitle = item2.Snippet.ChannelTitle,
                                                VideoId = item2.Id,
                                                VideoTitle = item2.Snippet.Title,
                                                ScheduledStartTime = startTime,
                                                ChannelType = ChannelType.Other
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
                                                .AddField("所屬", streamVideo.GetProductionType().GetProductionName())
                                                .AddField("直播狀態", "尚未開台", true)
                                                .AddField("排定開台時間", startTime, true);
                                                //.AddField("是否記錄直播", (CanRecord(db, streamVideo) ? "是" : "否"), true);

                                                if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
                                                {
                                                    if (!isFirstOther) await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.NewStream).ConfigureAwait(false);
                                                    StartReminder(streamVideo, streamVideo.ChannelType);
                                                }
                                            }
                                            else if (item2.Snippet.LiveBroadcastContent == "live")
                                            {
                                                if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
                                                    StartReminder(streamVideo, streamVideo.ChannelType);
                                            }
                                            else addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType);
                                        }
                                        else if (item2.LiveStreamingDetails.ActualStartTime != null) //未排程直播
                                        {
                                            var startTime = item2.LiveStreamingDetails.ActualStartTime.Value;
                                            var streamVideo = new StreamVideo()
                                            {
                                                ChannelId = item2.Snippet.ChannelId,
                                                ChannelTitle = item2.Snippet.ChannelTitle,
                                                VideoId = item2.Id,
                                                VideoTitle = item2.Snippet.Title,
                                                ScheduledStartTime = startTime,
                                                ChannelType = ChannelType.Other
                                            };

                                            streamVideo.ChannelType = streamVideo.GetProductionType();
                                            Log.NewStream($"(未排程) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                            if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType) && item2.Snippet.LiveBroadcastContent == "live")
                                                ReminderTimerAction(streamVideo);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error($"OtherAddSchedule {item}");
                                        Log.Error($"{ex.Message}\r\n{ex.StackTrace}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        try { otherVideoDic[item.ChannelId].Remove(videoId); }
                        catch (Exception) { }
                        Log.Error($"OtherSchedule {item}");
                        Log.Error($"{ex.Message}\r\n{ex.StackTrace}");
                    }
                }
            }

            Program.isOtherChannelSpider = false; isFirstOther = false;
            Log.Info("其他勢影片清單整理完成");
        }

        public static async Task SaveDateBase()
        {
            int saveNum = 0;

            try
            {
                using (var db = DataBase.DBContext.GetDbContext())
                {
                    if (!Program.isHoloChannelSpider && addNewStreamVideo.Any((x) => x.Value == ChannelType.Holo))
                    {
                        foreach (var item in addNewStreamVideo.Where((x) => x.Value == ChannelType.Holo))
                        {
                            if (!db.HoloStreamVideo.Any((x) => x.VideoId == item.Key.VideoId))
                            {
                                try
                                {
                                    await db.HoloStreamVideo.AddAsync(item.Key.ConvertToHoloStreamVideo()); saveNum++;
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"SaveHoloDateBase {ex.Message}\r\n{ex.StackTrace}");
                                    if (ex.InnerException != null) Log.Error($"{ex.InnerException.Message}\r\n{ex.InnerException.StackTrace}");
                                }
                            }

                            addNewStreamVideo.Remove(item.Key);
                        }

                        Log.Info($"Holo資料庫已儲存: {await db.SaveChangesAsync()}筆");
                    }

                    if (!Program.isNijisanjiChannelSpider && addNewStreamVideo.Any((x) => x.Value == ChannelType.Nijisanji))
                    {
                        foreach (var item in addNewStreamVideo.Where((x) => x.Value == ChannelType.Nijisanji))
                        {
                            if (!db.NijisanjiStreamVideo.Any((x) => x.VideoId == item.Key.VideoId))
                            {
                                try
                                {
                                    await db.NijisanjiStreamVideo.AddAsync(item.Key.ConvertToNijisanjiStreamVideo()); saveNum++;
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"SaveOtherDateBase {ex.Message}\r\n{ex.StackTrace}");
                                    if (ex.InnerException != null) Log.Error($"{ex.InnerException.Message}\r\n{ex.InnerException.StackTrace}");
                                }
                            }

                            addNewStreamVideo.Remove(item.Key);
                        }

                        Log.Info($"2434資料庫已儲存: {await db.SaveChangesAsync()}筆");
                    }

                    if (!Program.isOtherChannelSpider && addNewStreamVideo.Any((x) => x.Value == ChannelType.Other))
                    {
                        foreach (var item in addNewStreamVideo.Where((x) => x.Value == ChannelType.Other))
                        {
                            if (!db.OtherStreamVideo.Any((x) => x.VideoId == item.Key.VideoId))
                            {
                                try
                                {
                                    await db.OtherStreamVideo.AddAsync(item.Key.ConvertToOtherStreamVideo()); saveNum++;
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"SaveOtherDateBase {ex.Message}\r\n{ex.StackTrace}");
                                    if (ex.InnerException != null) Log.Error($"{ex.InnerException.Message}\r\n{ex.InnerException.StackTrace}");
                                }
                            }

                            addNewStreamVideo.Remove(item.Key);
                        }

                        Log.Info($"Other資料庫已儲存: {await db.SaveChangesAsync()}筆");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SaveDateBase {ex.Message}\r\n{ex.StackTrace}");
                if (ex.InnerException != null) Log.Error($"{ex.InnerException.Message}\r\n{ex.InnerException.StackTrace}");
            }

            if (saveNum != 0) Log.Info($"資料庫已儲存完畢: {saveNum}筆");
        }
    }    
}