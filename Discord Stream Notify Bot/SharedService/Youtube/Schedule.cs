using Discord_Stream_Notify_Bot.Interaction;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Polly;
using System.Data;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Extensions = Discord_Stream_Notify_Bot.Interaction.Extensions;

namespace Discord_Stream_Notify_Bot.SharedService.Youtube
{
    public partial class YoutubeStreamService
    {
        private static Dictionary<string, DataBase.Table.Video> addNewStreamVideo = new();
        private static HashSet<string> newStreamList = new();
        private bool isFirstOther = true;

        private void ReScheduleReminder()
        {
            List<string> recordChannelId = new();
            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                if (db.RecordYoutubeChannel.Any())
                    recordChannelId = db.RecordYoutubeChannel.AsNoTracking().Select((x) => x.YoutubeChannelId).ToList();
            }
            using (var db = DataBase.OtherVideoContext.GetDbContext())
            {
                foreach (var streamVideo in db.Video.AsNoTracking().Where((x) => x.ScheduledStartTime > DateTime.Now && !x.IsPrivate))
                {
                    StartReminder(streamVideo, DataBase.Table.Video.YTChannelType.Other);
                }
            }
        }

        // Todo: BlockingCollection應用 (但還不知道要用甚麼)
        // 應該是不能用，為了降低API配額消耗，所以必須取得全部的VideoId後再一次性的跟API要資料
        // https://blog.darkthread.net/blog/blockingcollection/
        // https://docs.microsoft.com/en-us/dotnet/standard/collections/thread-safe/blockingcollection-overview
        private async Task OtherScheduleAsync()
        {
            if (Program.IsOtherChannelSpider || Program.IsDisconnect) return;

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
                Log.Error("Redis 又死了zzz");
            }
#endif

            await Program.RedisDb.StringSetAsync("youtube.otherStart", "0", TimeSpan.FromMinutes(4));
            Program.IsOtherChannelSpider = true;
            Dictionary<string, List<string>> otherVideoDic = new Dictionary<string, List<string>>();
            var addVideoIdList = new List<string>();

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var channelList = db.YoutubeChannelSpider.Where((x) => db.RecordYoutubeChannel.Any((x2) => x.ChannelId == x2.YoutubeChannelId));
                using var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");
                httpClient.DefaultRequestHeaders.Add("AcceptLanguage", "zh-TW");

                Log.Info($"突襲開台檢測開始: {channelList.Count()} 個頻道");
                foreach (var item in channelList)
                {
                    if (Program.IsDisconnect) break;

                    try
                    {
                        if (item.ChannelTitle == null)
                        {
                            var ytChannel = YouTubeService.Channels.List("snippet");
                            ytChannel.Id = item.ChannelId;
                            item.ChannelTitle = (await ytChannel.ExecuteAsync().ConfigureAwait(false)).Items[0].Snippet.Title;
                            db.YoutubeChannelSpider.Update(item);
                            db.SaveChanges();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex.Demystify(), $"OtherUpdateChannelTitle {item}");
                    }

                    string videoId = "";

                    foreach (var type in new string[] { "videos", "streams" })
                    {
                        try
                        {
                            var response = await Policy.Handle<HttpRequestException>()
                                .Or<WebException>((ex) => ex.Message.Contains("unavailable")) // Resource temporarily unavailable
                                .WaitAndRetryAsync(3, (retryAttempt) =>
                                {
                                    var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                                    Log.Warn($"OtherSchedule {item.ChannelId} - {type}: GET 失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                                    return timeSpan;
                                })
                                .ExecuteAsync(async () =>
                                {
                                    return await httpClient.GetStringAsync($"https://www.youtube.com/channel/{item.ChannelId}/{type}");
                                });

                            if (string.IsNullOrEmpty(response))
                            {
                                Log.Warn($"OtherSchedule {item.ChannelId} - {type}: Response 為空，放棄本次排程");
                                continue;
                            }

                            Regex regex;
                            if (response.Contains("window[\"ytInitialData\"]"))
                                regex = new Regex("window\\[\"ytInitialData\"\\] = (.*);");
                            else
                                regex = new Regex(">var ytInitialData = (.*?);</script>");

                            var group = regex.Match(response).Groups[1];
                            var jObject = JObject.Parse(group.Value);
                            var alerts = jObject["alerts"];

                            if (alerts != null)
                            {
                                foreach (var alert in alerts)
                                {
                                    var alertRenderer = alert["alertRenderer"];
                                    if (alertRenderer["type"].ToString() == "ERROR")
                                    {
                                        try
                                        {
                                            Log.Warn($"{item.ChannelTitle} ({item.ChannelId}) 頻道錯誤: {alertRenderer["text"]["simpleText"]}");

                                            await Program.ApplicatonOwner.SendMessageAsync($"`{item.ChannelTitle}` ({item.ChannelId}) 頻道錯誤: {alertRenderer["text"]["simpleText"]}");

                                            db.YoutubeChannelSpider.Remove(item);
                                            db.SaveChanges();
                                        }
                                        catch (Exception ex)
                                        {
                                            Log.Error(ex.Demystify(), $"頻道被 Ban 了但移除爬蟲失敗: {item.ChannelTitle} ({item.ChannelId})");
                                        }
                                    }
                                }

                                break;
                            }

                            List<JToken> videoList = new List<JToken>();
                            videoList.AddRange(jObject.Descendants().Where((x) => x.ToString().StartsWith("\"gridVideoRenderer")));
                            videoList.AddRange(jObject.Descendants().Where((x) => x.ToString().StartsWith("\"videoRenderer")));

                            if (!otherVideoDic.ContainsKey(item.ChannelId))
                            {
                                otherVideoDic.Add(item.ChannelId, new List<string>());
                            }

                            foreach (var item2 in videoList)
                            {
                                try
                                {
                                    videoId = JObject.Parse(item2.ToString().Substring(item2.ToString().IndexOf("{")))["videoId"].ToString();

                                    if (!otherVideoDic[item.ChannelId].Contains(videoId))
                                    {
                                        otherVideoDic[item.ChannelId].Add(videoId);
                                        if (!newStreamList.Contains(videoId) && !addNewStreamVideo.ContainsKey(videoId) && !Extensions.HasStreamVideoByVideoId(videoId))
                                            addVideoIdList.Add(videoId);
                                        newStreamList.Add(videoId);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex.Demystify(), $"OtherSchedule {item.ChannelId} - {type}: GetVideoId");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            try { otherVideoDic[item.ChannelId].Remove(videoId); }
                            catch (Exception) { }
                            Log.Error(ex.Demystify(), $"OtherSchedule {item.ChannelId} - {type}: GetVideoList");
                        }
                    }
                }

                for (int i = 0; i < addVideoIdList.Count; i += 50)
                {
                    if (Program.IsDisconnect) break;

                    IEnumerable<Video> videos;
                    try
                    {
                        videos = await GetVideosAsync(addVideoIdList.Skip(i).Take(50));
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"OtherSchedule-GetVideosAsync: {ex}");
                        Program.IsOtherChannelSpider = false;
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
                            Log.Error(ex.Demystify(), $"OtherAddSchedule {item.Id}");
                        }
                    }
                }
            }

            Program.IsOtherChannelSpider = false; isFirstOther = false;
            //Log.Info("其他勢影片清單整理完成");
        }

        private async Task CheckScheduleTime()
        {
            try
            {
                // Key原則上不會有null或空白的情況才對
                //var list = Reminders.Where((x) => string.IsNullOrEmpty(x.Key)).ToList();
                //list.AddRange(Reminders.Where((x) => x.Value.StreamVideo.ScheduledStartTime < DateTime.Now));
                foreach (var item in Reminders.Where((x) => x.Value.StreamVideo.ScheduledStartTime < DateTime.Now))
                {
                    Reminders.TryRemove(item);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CheckScheduleTime-TryRemove: {ex}");
            }

            List<string> recordChannelId = new();
            try
            {
                using (var db = DataBase.MainDbContext.GetDbContext())
                {
                    if (db.RecordYoutubeChannel.Any())
                        recordChannelId = db.RecordYoutubeChannel.Select((x) => x.YoutubeChannelId).ToList();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"CheckScheduleTime-GetRecordYoutubeChannel: {ex}");
            }

            int changeVideoNum = 0;
            for (int i = 0; i < Reminders.Count; i += 50)
            {
                try
                {
                    var remindersList = Reminders.Skip(i).Take(50);
                    if (!remindersList.Any())
                    {
                        Log.Error($"CheckScheduleTime-Any: {i} / {Reminders.Count}");
                        break;
                    }

                    var video = YouTubeService.Videos.List("snippet,liveStreamingDetails");
                    video.Id = string.Join(",", remindersList.Select((x) => x.Key));
                    var videoResult = await video.ExecuteAsync(); // 如果直播被刪除的話該直播 Id 不會回傳資訊，但 API 會返回 200 狀態

                    foreach (var reminder in remindersList) // 直接使用 Reminders 來做迴圈
                    {
                        try
                        {
                            // 如果 viderResult 內沒有該 VideoId 直播的話，則判定該直播已刪除
                            if (!videoResult.Items.Any((x) => x.Id == reminder.Key))
                            {
                                // 如果是錄影頻道的話則忽略
                                //if (recordChannelId.Any((x) => x == reminder.Value.StreamVideo.ChannelId))
                                //{
                                //    Log.Warn($"CheckScheduleTime-VideoResult-{reminder.Key}: 錄影頻道已刪除直播，略過");
                                //    continue;
                                //}

                                Log.Warn($"CheckScheduleTime-VideoResult-{reminder.Key}: 已刪除直播");

                                EmbedBuilder embedBuilder = new EmbedBuilder();
                                embedBuilder.WithErrorColor()
                                    .WithTitle(reminder.Value.StreamVideo.VideoTitle)
                                    .WithDescription(Format.Url(reminder.Value.StreamVideo.ChannelTitle, $"https://www.youtube.com/channel/{reminder.Value.StreamVideo.ChannelId}"))
                                    .WithUrl($"https://www.youtube.com/watch?v={reminder.Key}")
                                    .AddField("直播狀態", "已刪除直播")
                                    .AddField("排定開台時間", reminder.Value.StreamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown(), true);

                                await SendStreamMessageAsync(reminder.Value.StreamVideo, embedBuilder.Build(), NoticeType.Delete).ConfigureAwait(false);
                                Reminders.TryRemove(reminder.Key, out var reminderItem);

                                reminder.Value.StreamVideo.IsPrivate = true;

                                DataBase.OtherVideoContext.GetDbContext().UpdateAndSave(reminder.Value.StreamVideo);

                                continue;
                            }

                            var item = videoResult.Items.First((x) => x.Id == reminder.Key);

                            // 可能是有調整到排程導致 API 回傳無資料，很少見但真的會遇到
                            if (item.LiveStreamingDetails == null || string.IsNullOrEmpty(item.LiveStreamingDetails.ScheduledStartTimeRaw))
                            {
                                Reminders.TryRemove(reminder.Key, out var reminderItem);

                                EmbedBuilder embedBuilder = new EmbedBuilder();
                                embedBuilder.WithTitle(reminder.Value.StreamVideo.VideoTitle)
                                    .WithOkColor()
                                    .WithDescription(Format.Url(reminder.Value.StreamVideo.ChannelTitle, $"https://www.youtube.com/channel/{reminder.Value.StreamVideo.ChannelId}"))
                                    .WithImageUrl($"https://i.ytimg.com/vi/{reminder.Key}/maxresdefault.jpg")
                                    .WithUrl($"https://www.youtube.com/watch?v={reminder.Key}")
                                    .AddField("直播狀態", "直播排程資料遺失")
                                    .AddField("原先預定開台時間", reminder.Value.StreamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                if (Program.ApplicatonOwner != null) await Program.ApplicatonOwner.SendMessageAsync(null, false, embedBuilder.Build()).ConfigureAwait(false);
                                await SendStreamMessageAsync(reminder.Value.StreamVideo, embedBuilder.Build(), NoticeType.Start).ConfigureAwait(false);
                                continue;
                            }

                            if (reminder.Value.StreamVideo.ScheduledStartTime != DateTime.Parse(item.LiveStreamingDetails.ScheduledStartTimeRaw))
                            {
                                changeVideoNum++;
                                try
                                {
                                    if (Reminders.TryRemove(reminder.Key, out var t))
                                    {
                                        t.Timer.Change(Timeout.Infinite, Timeout.Infinite);
                                        t.Timer.Dispose();
                                    }

                                    var startTime = DateTime.Parse(item.LiveStreamingDetails.ScheduledStartTimeRaw);
                                    var streamVideo = new DataBase.Table.Video()
                                    {
                                        ChannelId = item.Snippet.ChannelId,
                                        ChannelTitle = item.Snippet.ChannelTitle,
                                        VideoId = item.Id,
                                        VideoTitle = item.Snippet.Title,
                                        ScheduledStartTime = startTime,
                                        ChannelType = reminder.Value.StreamVideo.ChannelType
                                    };

                                    DataBase.OtherVideoContext.GetDbContext().UpdateAndSave(streamVideo);

                                    Log.Info($"時間已更改 {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                    if (startTime > DateTime.Now && startTime < DateTime.Now.AddDays(14))
                                    {
                                        EmbedBuilder embedBuilder = new EmbedBuilder();
                                        embedBuilder.WithErrorColor()
                                            .WithTitle(streamVideo.VideoTitle)
                                            .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                            .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                            .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                            .AddField("直播狀態", "尚未開台(已更改時間)", true)
                                            .AddField("排定開台時間", reminder.Value.StreamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown())
                                            .AddField("更改開台時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                        await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.ChangeTime).ConfigureAwait(false);
                                        StartReminder(streamVideo, streamVideo.ChannelType);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"CheckScheduleTime-HasValue: {ex}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"CheckScheduleTime-VideoResult-Items: {reminder.Key}");
                            Log.Error($"{ex}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"CheckScheduleTime: {ex}");
                }
            }

            if (changeVideoNum > 0)
            {
                Log.Info($"CheckScheduleTime-Done: {changeVideoNum} / {Reminders.Count}");
            }
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
                    ScheduledStartTime = DateTime.Parse(item.Snippet.PublishedAtRaw),
                    ChannelType = DataBase.Table.Video.YTChannelType.Other
                };

                streamVideo.ChannelType = streamVideo.GetProductionType();
                Log.New($"(新影片) | {streamVideo.ScheduledStartTime} | {streamVideo.ChannelTitle} - {streamVideo.VideoTitle} ({streamVideo.VideoId})");

                EmbedBuilder embedBuilder = new EmbedBuilder();
                embedBuilder.WithOkColor()
                    .WithTitle(streamVideo.VideoTitle)
                    .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                    .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                    .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                    .AddField("上傳時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo) && !isFirstOther && !isFromRNRS && streamVideo.ScheduledStartTime > DateTime.Now.AddDays(-2))
                    await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.NewVideo).ConfigureAwait(false);
            }
            else if (!string.IsNullOrEmpty(item.LiveStreamingDetails.ActualStartTimeRaw)) //已開台直播
            {
                var startTime = DateTime.Parse(item.LiveStreamingDetails.ActualStartTimeRaw);
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
                Log.New($"(已開台) | {streamVideo.ScheduledStartTime} | {streamVideo.ChannelTitle} - {streamVideo.VideoTitle} ({streamVideo.VideoId})");

                if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo) && item.Snippet.LiveBroadcastContent == "live" && !isFromRNRS)
                    await ReminderTimerActionAsync(streamVideo);
            }
            else if (!string.IsNullOrEmpty(item.LiveStreamingDetails.ScheduledStartTimeRaw)) // 尚未開台的直播
            {
                var startTime = DateTime.Parse(item.LiveStreamingDetails.ScheduledStartTimeRaw);
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
                Log.New($"(新直播) | {streamVideo.ScheduledStartTime} | {streamVideo.ChannelTitle} - {streamVideo.VideoTitle} ({streamVideo.VideoId})");

                if (startTime > DateTime.Now && startTime < DateTime.Now.AddDays(14))
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
                        if (!isFirstOther) await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.NewStream).ConfigureAwait(false);
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
            // 好像從沒看過這個分類觸發過
            else if (string.IsNullOrEmpty(item.LiveStreamingDetails.ActualStartTimeRaw) && item.LiveStreamingDetails.ActiveLiveChatId != null)
            {
                var streamVideo = new DataBase.Table.Video()
                {
                    ChannelId = item.Snippet.ChannelId,
                    ChannelTitle = item.Snippet.ChannelTitle,
                    VideoId = item.Id,
                    VideoTitle = item.Snippet.Title,
                    ScheduledStartTime = DateTime.Parse(item.Snippet.PublishedAtRaw),
                    ChannelType = DataBase.Table.Video.YTChannelType.Other
                };

                Log.New($"(一般路過的新直播室) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle} ({streamVideo.VideoId})");
                addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo);
            }
        }

        public static void SaveDateBase()
        {
            int saveNum = 0;

            try
            {
                if (!Program.IsOtherChannelSpider && addNewStreamVideo.Any((x) => x.Value.ChannelType == DataBase.Table.Video.YTChannelType.Other))
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

                        Log.Info($"Other 資料庫已儲存: {db.SaveChanges()} 筆");
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

                        Log.Info($"NotVTuber 資料庫已儲存: {db.SaveChanges()} 筆");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SaveDateBase {ex}");
            }

            if (saveNum != 0) Log.Info($"資料庫已儲存完畢: {saveNum} 筆");
        }
    }
}