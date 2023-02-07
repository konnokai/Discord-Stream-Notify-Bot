using Discord.Interactions;
using Discord_Stream_Notify_Bot.Interaction;
using Discord_Stream_Notify_Bot.SharedService.Youtube.Json;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using HtmlAgilityPack;
using Polly;
using System.Collections.Concurrent;
using System.Data;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;

namespace Discord_Stream_Notify_Bot.SharedService.Youtube
{
    public partial class YoutubeStreamService : IInteractionService
    {
        public enum NoticeType
        {
            [ChoiceDisplay("新待機室")]
            NewStream,
            [ChoiceDisplay("新上傳影片")]
            NewVideo,
            [ChoiceDisplay("開始實況\\首播")]
            Start,
            [ChoiceDisplay("結束實況\\首播")]
            End,
            [ChoiceDisplay("更改開台時間")]
            ChangeTime,
            [ChoiceDisplay("已刪除或私人化實況")]
            Delete
        }

        public enum NowStreamingHost
        {
            [ChoiceDisplay("Holo")]
            Holo,
            [ChoiceDisplay("彩虹社")]
            Niji
        }

        public List<Content> NijisanjiLiverContents { get; } = new List<Content>();
        public ConcurrentDictionary<string, ReminderItem> Reminders { get; } = new ConcurrentDictionary<string, ReminderItem>();
        public bool IsRecord { get; set; } = true;
        public YouTubeService yt;

        public Emote YouTubeEmote
        {
            get
            {
                if (youTubeEmote == null)
                {
                    try
                    {
                        youTubeEmote = _client.Guilds.FirstOrDefault((x) => x.Id == 1040482713213345872).Emotes.FirstOrDefault((x) => x.Id == 1041913109926903878);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"無法取得YouTube Emote: {ex}");
                        youTubeEmote = null;
                    }
                }
                return youTubeEmote;
            }
        }

        public Emote PatreonEmote
        {
            get
            {
                if (patreonEmote == null)
                {
                    try
                    {
                        patreonEmote = _client.Guilds.FirstOrDefault((x) => x.Id == 1040482713213345872).Emotes.FirstOrDefault((x) => x.Id == 1041988445830119464);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"無法取得Patreon Emote: {ex}");
                        patreonEmote = null;
                    }
                }
                return patreonEmote;
            }
        }

        public Emote PayPalEmote
        {
            get
            {
                if (payPalEmote == null)
                {
                    try
                    {
                        payPalEmote = _client.Guilds.FirstOrDefault((x) => x.Id == 1040482713213345872).Emotes.FirstOrDefault((x) => x.Id == 1042004146208899102);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"無法取得PayPal Emote: {ex}");
                        payPalEmote = null;
                    }
                }
                return payPalEmote;
            }
        }

        private Timer holoSchedule, nijisanjiSchedule, otherSchedule, checkScheduleTime, saveDateBase, subscribePubSub/*, checkHoloNowStream, holoScheduleEmoji*/;
        private SocketTextChannel noticeRecordChannel;
        private DiscordSocketClient _client;
        private readonly IHttpClientFactory _httpClientFactory;
        private string callbackUrl;
        private Polly.Retry.RetryPolicy<Task> pBreaker;
        private Emote youTubeEmote, patreonEmote, payPalEmote;

        public YoutubeStreamService(DiscordSocketClient client, IHttpClientFactory httpClientFactory, BotConfig botConfig)
        {
            _client = client;
            _httpClientFactory = httpClientFactory;
            yt = new YouTubeService(new BaseClientService.Initializer
            {
                ApplicationName = "DiscordStreamBot",
                ApiKey = botConfig.GoogleApiKey,
            });

            callbackUrl = botConfig.PubSubCallbackUrl;

            //https://blog.darkthread.net/blog/polly/
            //https://blog.darkthread.net/blog/polly-circuitbreakerpolicy/
            pBreaker = Policy<Task>
               .Handle<Exception>()
               .WaitAndRetry(new TimeSpan[]
               {
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2)
               });

            if (Program.Redis != null)
            {
                Program.RedisSub.Subscribe("youtube.startstream", async (channel, videoId) =>
                {
                    try
                    {
                        Log.Info($"{channel} - {videoId}");

                        var item = await GetVideoAsync(videoId).ConfigureAwait(false);
                        if (item == null)
                        {
                            Log.Warn($"{videoId} Delete");
                            await Program.RedisSub.PublishAsync("youtube.deletestream", videoId);
                            return;
                        }

                        DateTime startTime;
                        if (item.LiveStreamingDetails.ActualStartTime.HasValue)
                            startTime = item.LiveStreamingDetails.ActualStartTime.Value;
                        else
                            startTime = item.LiveStreamingDetails.ScheduledStartTime.Value;

                        EmbedBuilder embedBuilder = new EmbedBuilder();
                        embedBuilder.WithRecordColor()
                        .WithTitle(item.Snippet.Title)
                        .WithDescription(Format.Url(item.Snippet.ChannelTitle, $"https://www.youtube.com/channel/{item.Snippet.ChannelId}"))
                        .WithImageUrl($"https://i.ytimg.com/vi/{item.Id}/maxresdefault.jpg")
                        .WithUrl($"https://www.youtube.com/watch?v={item.Id}")
                        .AddField("直播狀態", "開台中")
                        .AddField("開台時間", startTime.ConvertDateTimeToDiscordMarkdown());

                        await SendStreamMessageAsync(item.Id, embedBuilder, NoticeType.Start).ConfigureAwait(false);
                        await ChangeGuildBannerAsync(item.Snippet.ChannelId, item.Id);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Record-StartStream {ex}");
                    }
                });

                Program.RedisSub.Subscribe("youtube.endstream", async (channel, videoId) =>
                {
                    try
                    {
                        Log.Info($"{channel} - {videoId}");

                        var item = await GetVideoAsync(videoId.ToString()).ConfigureAwait(false);
                        if (item == null)
                        {
                            Log.Warn($"{videoId} Delete");
                            await Program.RedisSub.PublishAsync("youtube.deletestream", videoId);
                            return;
                        }

                        if (!item.LiveStreamingDetails.ActualEndTime.HasValue)
                        {
                            Log.Warn("還沒關台");
                            return;
                        }

                        var startTime = item.LiveStreamingDetails.ActualStartTime.Value;
                        var endTime = item.LiveStreamingDetails.ActualEndTime.Value;

                        EmbedBuilder embedBuilder = new EmbedBuilder();
                        embedBuilder.WithErrorColor()
                        .WithTitle(item.Snippet.Title)
                        .WithDescription(Format.Url(item.Snippet.ChannelTitle, $"https://www.youtube.com/channel/{item.Snippet.ChannelId}"))
                        .WithImageUrl($"https://i.ytimg.com/vi/{item.Id}/maxresdefault.jpg")
                        .WithUrl($"https://www.youtube.com/watch?v={item.Id}")
                        .AddField("直播狀態", "已關台")
                        .AddField("直播時間", $"{endTime.Subtract(startTime):hh'時'mm'分'ss'秒'}")
                        .AddField("關台時間", endTime.ConvertDateTimeToDiscordMarkdown());

                        await SendStreamMessageAsync(item.Id, embedBuilder, NoticeType.End).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Record-EndStream {ex}");
                    }
                });

                Program.RedisSub.Subscribe("youtube.deletestream", async (channel, videoId) =>
                {
                    Log.Info($"{channel} - {videoId}");

                    using (var db = DataBase.DBContext.GetDbContext())
                    {
                        try
                        {
                            if (Extensions.HasStreamVideoByVideoId(videoId))
                            {
                                var streamVideo = Extensions.GetStreamVideoByVideoId(videoId);

                                EmbedBuilder embedBuilder = new EmbedBuilder();
                                embedBuilder.WithErrorColor()
                                .WithTitle(streamVideo.VideoTitle)
                                .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                .AddField("直播狀態", "已刪除直播")
                                .AddField("排定開台時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.Delete).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Record-DeleteStream {ex}");
                        }
                    }
                });

                Program.RedisSub.Subscribe("youtube.memberonly", async (channel, videoId) =>
                {
                    Log.Info($"{channel} - {videoId}");

                    using (var db = DataBase.DBContext.GetDbContext())
                    {
                        try
                        {
                            if (Extensions.HasStreamVideoByVideoId(videoId))
                            {
                                var streamVideo = Extensions.GetStreamVideoByVideoId(videoId);
                                var item = await GetVideoAsync(videoId).ConfigureAwait(false);

                                if (item == null)
                                {
                                    Log.Warn($"{videoId} Delete");
                                    await Program.RedisSub.PublishAsync("youtube.deletestream", videoId);
                                    return;
                                }

                                if (!item.LiveStreamingDetails.ActualEndTime.HasValue)
                                {
                                    Log.Warn("還沒關台");
                                    return;
                                }

                                var startTime = item.LiveStreamingDetails.ActualStartTime.Value;
                                var endTime = item.LiveStreamingDetails.ActualEndTime.Value;

                                EmbedBuilder embedBuilder = new EmbedBuilder();
                                embedBuilder.WithErrorColor()
                                .WithTitle(streamVideo.VideoTitle)
                                .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                .AddField("直播狀態", "已關台並變更為會限影片")
                                .AddField("直播時間", $"{endTime.Subtract(startTime):hh'時'mm'分'ss'秒'}")
                                .AddField("關台時間", endTime.ConvertDateTimeToDiscordMarkdown());

                                if (Program.ApplicatonOwner != null) await Program.ApplicatonOwner.SendMessageAsync("已關台並變更為會限影片", false, embedBuilder.Build()).ConfigureAwait(false);
                                await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.End).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Record-MemberOnly {ex}");
                        }
                    }
                });

                Program.RedisSub.Subscribe("youtube.unarchived", async (channel, videoId) =>
                {
                    Log.Info($"{channel} - {videoId}");

                    using (var db = DataBase.DBContext.GetDbContext())
                    {
                        try
                        {
                            if (Extensions.HasStreamVideoByVideoId(videoId))
                            {
                                var streamVideo = Extensions.GetStreamVideoByVideoId(videoId);
                                EmbedBuilder embedBuilder = new EmbedBuilder();
                                embedBuilder.WithTitle(streamVideo.VideoTitle)
                                .WithOkColor()
                                .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                .AddField("直播狀態", "已關台並變更為私人存檔")
                                .AddField("排定開台時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                if (Program.ApplicatonOwner != null) await Program.ApplicatonOwner.SendMessageAsync("已關台並變更為私人存檔", false, embedBuilder.Build()).ConfigureAwait(false);
                                await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.Delete).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Record-UnArchived {ex}");
                        }
                    }
                });

                Program.RedisSub.Subscribe("youtube.429error", async (channel, videoId) =>
                {
                    Log.Info($"{channel} - {videoId}");
                    IsRecord = false;

                    using (var db = DataBase.DBContext.GetDbContext())
                    {
                        try
                        {
                            if (Extensions.HasStreamVideoByVideoId(videoId))
                            {
                                var streamVideo = Extensions.GetStreamVideoByVideoId(videoId);
                                EmbedBuilder embedBuilder = new EmbedBuilder();
                                embedBuilder.WithTitle(streamVideo.VideoTitle)
                                .WithOkColor()
                                .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                .AddField("直播狀態", "開台中")
                                .AddField("排定開台時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                if (Program.ApplicatonOwner != null) await Program.ApplicatonOwner.SendMessageAsync("429錯誤", false, embedBuilder.Build()).ConfigureAwait(false);
                                await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.Start).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Record-429Error {ex}");
                        }
                    }
                });

                Program.RedisSub.Subscribe("youtube.pubsub.CreateOrUpdate", async (channel, youtubeNotificationJson) =>
                {
                    YoutubePubSubNotification youtubePubSubNotification = JsonConvert.DeserializeObject<YoutubePubSubNotification>(youtubeNotificationJson.ToString());

                    try
                    {
                        using (var db = DataBase.DBContext.GetDbContext())
                        {
                            if (!addNewStreamVideo.ContainsKey(youtubePubSubNotification.VideoId) && !Extensions.HasStreamVideoByVideoId(youtubePubSubNotification.VideoId))
                            {
                                Log.Info($"{channel} - (新影片) {youtubePubSubNotification.ChannelId}: {youtubePubSubNotification.VideoId}");

                                DataBase.Table.Video streamVideo;
                                var youtubeChannelSpider = db.YoutubeChannelSpider.FirstOrDefault((x) => x.ChannelId == youtubePubSubNotification.ChannelId);

                                using var db2 = DataBase.NijisanjiVideoContext.GetDbContext();

                                if (db.RecordYoutubeChannel.Any((x) => x.YoutubeChannelId == youtubePubSubNotification.ChannelId) || db2.Video.Any((x) => x.ChannelId == youtubePubSubNotification.ChannelId) || (youtubeChannelSpider != null && youtubeChannelSpider.IsTrustedChannel))
                                {
                                    var item = await GetVideoAsync(youtubePubSubNotification.VideoId).ConfigureAwait(false);
                                    if (item == null)
                                    {
                                        Log.Warn($"{youtubePubSubNotification.VideoId} Delete");
                                        return;
                                    }

                                    try
                                    {
                                        await AddOtherDataAsync(item);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error($"PubSub_AddData_CreateOrUpdate: {item.Id}");
                                        Log.Error($"{ex}");
                                    }
                                }
                                else
                                {
                                    streamVideo = new DataBase.Table.Video()
                                    {
                                        ChannelId = youtubePubSubNotification.ChannelId,
                                        ChannelTitle = db.GetNotVTuberChannelTitleByChannelId(youtubePubSubNotification.ChannelId),
                                        VideoId = youtubePubSubNotification.VideoId,
                                        VideoTitle = youtubePubSubNotification.Title,
                                        ScheduledStartTime = youtubePubSubNotification.Published,
                                        ChannelType = DataBase.Table.Video.YTChannelType.NotVTuber
                                    };

                                    Log.New($"(非已認可的新影片) {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                    EmbedBuilder embedBuilder = new EmbedBuilder();
                                    embedBuilder.WithOkColor()
                                    .WithTitle(streamVideo.VideoTitle)
                                    .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                    .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                    .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                    .AddField("上傳時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                    if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo))
                                        await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.NewVideo).ConfigureAwait(false);
                                }
                            }
                            else Log.Info($"{channel} - (編輯或關台) {youtubePubSubNotification.ChannelId}: {youtubePubSubNotification.VideoId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"PubSub-CreateOrUpdate {ex}");
                    }
                });

                Program.RedisSub.Subscribe("youtube.pubsub.Deleted", async (channel, youtubeNotificationJson) =>
                {
                    YoutubePubSubNotification youtubePubSubNotification = JsonConvert.DeserializeObject<YoutubePubSubNotification>(youtubeNotificationJson.ToString());

                    Log.Info($"{channel} - {youtubePubSubNotification.VideoId}");

                    try
                    {
                        using (var db = DataBase.DBContext.GetDbContext())
                        {
                            if (Extensions.HasStreamVideoByVideoId(youtubePubSubNotification.VideoId))
                            {
                                DataBase.Table.Video streamVideo = Extensions.GetStreamVideoByVideoId(youtubePubSubNotification.VideoId);
                                if (streamVideo == null)
                                {
                                    EmbedBuilder embedBuilder = new EmbedBuilder();
                                    embedBuilder.WithOkColor()
                                    .WithTitle(streamVideo.VideoTitle)
                                    .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                    .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                    .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                    .AddField("狀態", "已刪除")
                                    .AddField("排定開台/上傳時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                    await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.Delete).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"PubSub-Deleted {ex}");
                    }
                });

                Program.RedisSub.Subscribe("youtube.pubsub.NeedRegister", async (channel, channelId) =>
                {
                    using (var db = DataBase.DBContext.GetDbContext())
                    {
                        if (db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId.ToString()))
                        {
                            var youtubeChannelSpider = db.YoutubeChannelSpider.Single((x) => x.ChannelId == channelId.ToString());

                            if (await PostSubscribeRequestAsync(channelId.ToString()))
                            {
                                Log.Info($"已重新註冊YT PubSub: {youtubeChannelSpider.ChannelTitle} ({channelId})");
                                youtubeChannelSpider.LastSubscribeTime = DateTime.Now;
                                db.Update(youtubeChannelSpider);
                                db.SaveChanges();
                            }
                        }
                        else
                        {
                            Log.Error($"後端通知須重新註冊但資料庫無該ChannelId的資料: {channelId}");
                        }
                    }
                });

                #region Nope
                //Program.redisSub.Subscribe("youtube.changestreamtime", async (channel, videoId) =>
                //{
                //    Log.Info($"{channel} - {videoId}");

                //    var item = await GetVideoAsync(videoId).ConfigureAwait(false);
                //    if (item == null)
                //    {
                //        Log.Warn($"{videoId} Delete");
                //        return;
                //    }

                //    try
                //    {
                //        var startTime = item.LiveStreamingDetails.ActualStartTime.Value;

                //        using (var uow = new DBContext())
                //        {
                //            var stream = uow.GetStreamVideoByVideoId(videoId);

                //            EmbedBuilder embedBuilder = new EmbedBuilder();
                //            embedBuilder.WithErrorColor()
                //            .WithTitle(item.Snippet.Title)
                //            .WithDescription(item.Snippet.ChannelTitle)
                //            .WithImageUrl($"https://i.ytimg.com/vi/{videoId}/maxresdefault.jpg")
                //            .WithUrl($"https://www.youtube.com/watch?v={videoId}")
                //            .AddField("直播狀態", "尚未開台(已更改時間)", true)
                //            .AddField("排定開台時間", stream.ScheduledStartTime, true)
                //            .AddField("更改開台時間", startTime, true);

                //            await SendStreamMessageAsync(item.Id, embedBuilder, NoticeType.ChangeTime).ConfigureAwait(false);

                //            stream.ScheduledStartTime = startTime;
                //            uow.OtherStreamVideo.Update(stream);
                //            await uow.SaveChangesAsync();
                //        }
                //    }
                //    catch (Exception ex) { Log.Error("ChangeStreamTime"); Log.Error(ex.Message); }
                //});

                //Program.redisSub.Subscribe("youtube.newstream", async (channel, videoId) =>
                //{
                //    using (var uow = new DBContext())
                //    {                        
                //        if (!uow.HasStreamVideoByVideoId(videoId))
                //        {
                //            var item = await GetVideoAsync(videoId).ConfigureAwait(false);
                //            if (item == null)
                //            {
                //                Log.Warn($"{videoId} Delete");
                //                return;
                //            }

                //            var startTime = item.LiveStreamingDetails.ScheduledStartTime.Value;
                //            var streamVideo = new StreamVideo()
                //            {
                //                ChannelId = item.Snippet.ChannelId,
                //                ChannelTitle = item.Snippet.ChannelTitle,
                //                VideoId = item.Id,
                //                VideoTitle = item.Snippet.Title,
                //                ScheduledStartTime = startTime,
                //                ChannelType = StreamVideo.YTChannelType.Other
                //            };

                //            Log.NewStream($"{channel} - {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                //            uow.OtherStreamVideo.Add(streamVideo.ConvertToOtherStreamVideo());
                //            await uow.SaveChangesAsync().ConfigureAwait(false);

                //            StartReminder(streamVideo, StreamVideo.YTChannelType.Other);

                //            EmbedBuilder embedBuilder = new EmbedBuilder();
                //            embedBuilder.WithErrorColor()
                //            .WithTitle(item.Snippet.Title)
                //            .WithDescription(item.Snippet.ChannelTitle)
                //            .WithImageUrl($"https://i.ytimg.com/vi/{item.Id}/maxresdefault.jpg")
                //            .WithUrl($"https://www.youtube.com/watch?v={item.Id}")
                //            .AddField("所屬", streamVideo.GetProduction())
                //            .AddField("直播狀態", "尚未開台", true)
                //            .AddField("排定開台時間", startTime, true)
                //            .AddField("是否記錄直播", "是", true);

                //            await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.New).ConfigureAwait(false);
                //        }
                //    }
                //});
                #endregion

                Log.Info("已建立Redis訂閱");
            }

            using (var db = DataBase.HoloVideoContext.GetDbContext())
            {
                foreach (var streamVideo in db.Video.Where((x) => x.ScheduledStartTime > DateTime.Now && !x.IsPrivate))
                {
                    StartReminder(streamVideo, DataBase.Table.Video.YTChannelType.Holo);
                }
            }
            using (var db = DataBase.NijisanjiVideoContext.GetDbContext())
            {
                foreach (var streamVideo in db.Video.Where((x) => x.ScheduledStartTime > DateTime.Now && !x.IsPrivate))
                {
                    StartReminder(streamVideo, DataBase.Table.Video.YTChannelType.Nijisanji);
                }
            }
            using (var db = DataBase.OtherVideoContext.GetDbContext())
            {
                foreach (var streamVideo in db.Video.Where((x) => x.ScheduledStartTime > DateTime.Now && !x.IsPrivate))
                {
                    StartReminder(streamVideo, DataBase.Table.Video.YTChannelType.Other);
                }
            }

            foreach (var item in new string[] { "nijisanji", "nijisanjien", "virtuareal" })
            {
                Task.Run(async () => await GetOrCreateNijisanjiLiverListAsync(item));
            }

            holoSchedule = new Timer(async (objState) => await HoloScheduleAsync(), null, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(5));

            nijisanjiSchedule = new Timer(async (objState) => await NijisanjiScheduleAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5));

            otherSchedule = new Timer(async (objState) => await OtherScheduleAsync(), null, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(5));

#if DEBUG
            return;
#endif

            checkScheduleTime = new Timer(async (objState) =>
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

                        var video = yt.Videos.List("snippet,liveStreamingDetails");
                        video.Id = string.Join(",", remindersList.Select((x) => x.Key));
                        var videoResult = await video.ExecuteAsync(); // 如果直播被刪除的話該直播Id不會回傳資訊，但API會返回200狀態

                        foreach (var reminder in remindersList) // 直接使用Reminders來做迴圈
                        {
                            try
                            {
                                // 如果viderResult內沒有該VideoId直播的話，則判定該直播已刪除
                                if (!videoResult.Items.Any((x) => x.Id == reminder.Key))
                                {
                                    Log.Warn($"CheckScheduleTime-VideoResult-{reminder.Key}: 已刪除直播");

                                    EmbedBuilder embedBuilder = new EmbedBuilder();
                                    embedBuilder.WithErrorColor()
                                    .WithTitle(reminder.Value.StreamVideo.VideoTitle)
                                    .WithDescription(Format.Url(reminder.Value.StreamVideo.ChannelTitle, $"https://www.youtube.com/channel/{reminder.Value.StreamVideo.ChannelId}"))
                                    .WithUrl($"https://www.youtube.com/watch?v={reminder.Key}")
                                    .AddField("直播狀態", "已刪除直播")
                                    .AddField("排定開台時間", reminder.Value.StreamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown(), true);

                                    await SendStreamMessageAsync(reminder.Value.StreamVideo, embedBuilder, NoticeType.Delete).ConfigureAwait(false);
                                    Reminders.TryRemove(reminder.Key, out var reminderItem);

                                    reminder.Value.StreamVideo.IsPrivate = true;

                                    switch (reminder.Value.ChannelType)
                                    {
                                        case DataBase.Table.Video.YTChannelType.Holo:
                                            DataBase.HoloVideoContext.GetDbContext().UpdateAndSave(reminder.Value.StreamVideo);
                                            break;
                                        case DataBase.Table.Video.YTChannelType.Nijisanji:
                                            DataBase.NijisanjiVideoContext.GetDbContext().UpdateAndSave(reminder.Value.StreamVideo);
                                            break;
                                        default:
                                            DataBase.OtherVideoContext.GetDbContext().UpdateAndSave(reminder.Value.StreamVideo);
                                            break;
                                    }

                                    continue;
                                }

                                var item = videoResult.Items.First((x) => x.Id == reminder.Key);

                                if (!item.LiveStreamingDetails.ScheduledStartTime.HasValue)
                                {
                                    Reminders.TryRemove(reminder.Key, out var reminderItem);

                                    EmbedBuilder embedBuilder = new EmbedBuilder();
                                    embedBuilder.WithTitle(reminder.Value.StreamVideo.VideoTitle)
                                    .WithOkColor()
                                    .WithDescription(Format.Url(reminder.Value.StreamVideo.ChannelTitle, $"https://www.youtube.com/channel/{reminder.Value.StreamVideo.ChannelId}"))
                                    .WithImageUrl($"https://i.ytimg.com/vi/{reminder.Key}/maxresdefault.jpg")
                                    .WithUrl($"https://www.youtube.com/watch?v={reminder.Key}")
                                    .AddField("直播狀態", "無開始時間")
                                    .AddField("開台時間", reminder.Value.StreamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                    if (Program.ApplicatonOwner != null) await Program.ApplicatonOwner.SendMessageAsync(null, false, embedBuilder.Build()).ConfigureAwait(false);
                                    //await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.Start).ConfigureAwait(false);
                                    continue;
                                }

                                if (reminder.Value.StreamVideo.ScheduledStartTime != item.LiveStreamingDetails.ScheduledStartTime.Value)
                                {
                                    changeVideoNum++;
                                    try
                                    {
                                        if (Reminders.TryRemove(reminder.Key, out var t))
                                        {
                                            t.Timer.Change(Timeout.Infinite, Timeout.Infinite);
                                            t.Timer.Dispose();
                                        }

                                        var startTime = item.LiveStreamingDetails.ScheduledStartTime.Value;
                                        var streamVideo = new DataBase.Table.Video()
                                        {
                                            ChannelId = item.Snippet.ChannelId,
                                            ChannelTitle = item.Snippet.ChannelTitle,
                                            VideoId = item.Id,
                                            VideoTitle = item.Snippet.Title,
                                            ScheduledStartTime = startTime,
                                            ChannelType = reminder.Value.StreamVideo.ChannelType
                                        };

                                        switch (streamVideo.ChannelType)
                                        {
                                            case DataBase.Table.Video.YTChannelType.Holo:
                                                DataBase.HoloVideoContext.GetDbContext().UpdateAndSave(streamVideo);
                                                break;
                                            case DataBase.Table.Video.YTChannelType.Nijisanji:
                                                DataBase.NijisanjiVideoContext.GetDbContext().UpdateAndSave(streamVideo);
                                                break;
                                            default:
                                                DataBase.OtherVideoContext.GetDbContext().UpdateAndSave(streamVideo);
                                                break;
                                        }

                                        Log.Info($"時間已更改 {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                        if (startTime > DateTime.Now && startTime < DateTime.Now.AddDays(7))
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

                                            await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.ChangeTime).ConfigureAwait(false);
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
            }, null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));

            saveDateBase = new Timer((objState) => SaveDateBase(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3));

            subscribePubSub = new Timer((objState) => SubscribePubSub(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30));
        }

        private async Task GetOrCreateNijisanjiLiverListAsync(string affiliation)
        {
            try
            {
                if (await Program.RedisDb.KeyExistsAsync($"youtube.nijisanji.liver.{affiliation}"))
                {
                    var liver = JsonConvert.DeserializeObject<List<Content>>(await Program.RedisDb.StringGetAsync($"youtube.nijisanji.liver.{affiliation}"));
                    NijisanjiLiverContents.AddRange(liver);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"GetOrCreateNijisanjiLiverListAsync-GetRedisData-{affiliation}: {ex}");
            }

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var json = await httpClient.GetStringAsync($"https://www.nijisanji.jp/api/livers?limit=300&offset=0&orderKey=subscriber_count&order=desc&affiliation={affiliation}&locale=ja&includeHidden=true");
                var liver = JsonConvert.DeserializeObject<NijisanjiLiverJson>(json).contents;
                await Program.RedisDb.StringSetAsync($"youtube.nijisanji.liver.{affiliation}", JsonConvert.SerializeObject(liver), TimeSpan.FromDays(1));
                NijisanjiLiverContents.AddRange(liver);
                Log.New($"GetOrCreateNijisanjiLiverListAsync: {affiliation}已刷新");
            }
            catch (Exception ex)
            {
                Log.Error($"GetOrCreateNijisanjiLiverListAsync-GetLiver-{affiliation}: {ex}");
            }
        }

        public async Task<Embed> GetNowStreamingChannel(NowStreamingHost host)
        {
            try
            {
                List<string> idList = new List<string>();
                switch (host)
                {
                    case NowStreamingHost.Holo:
                        {
                            HtmlWeb htmlWeb = new HtmlWeb();
                            HtmlDocument htmlDocument = htmlWeb.Load("https://schedule.hololive.tv/lives/all");
                            idList.AddRange(htmlDocument.DocumentNode.Descendants()
                                .Where((x) => x.Name == "a" &&
                                    x.Attributes["href"].Value.StartsWith("https://www.youtube.com/watch") &&
                                    x.Attributes["style"].Value.Contains("border: 3px"))
                                .Select((x) => x.Attributes["href"].Value.Split("?v=")[1]));
                        }
                        break;
                    case NowStreamingHost.Niji: //Todo: 實作2434現正直播查詢
                        return null;
                        break;
                }

                var video = yt.Videos.List("snippet");
                video.Id = string.Join(",", idList);
                var videoResult = await video.ExecuteAsync().ConfigureAwait(false);

                EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor()
                    .WithTitle("正在直播的清單")
                    .WithThumbnailUrl("https://schedule.hololive.tv/dist/images/logo.png")
                    .WithCurrentTimestamp()
                    .WithDescription(string.Join("\n", videoResult.Items.Select((x) => $"{x.Snippet.ChannelTitle} - {Format.Url(x.Snippet.Title, $"https://www.youtube.com/watch?v={x.Id}")}")));

                return embedBuilder.Build();
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
                return null;
            }
        }

        private bool CanRecord(DataBase.DBContext db, DataBase.Table.Video streamVideo) =>
             IsRecord && db.RecordYoutubeChannel.Any((x) => x.YoutubeChannelId.Trim() == streamVideo.ChannelId.Trim());

        public async Task<string> GetChannelIdAsync(string channelUrl)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new ArgumentNullException(channelUrl);

            channelUrl = channelUrl.Trim();

            switch (channelUrl.ToLower())
            {
                case "all":
                case "holo":
                case "2434":
                case "other":
                    return channelUrl.ToLower();
            }

            if (channelUrl.StartsWith("UC") && channelUrl.Length == 24)
                return channelUrl;

            string channelId;

            Regex regexOldFormat = new Regex(@"(http[s]{0,1}://){0,1}(www\.){0,1}(?'Host'[^/]+)/(?'Type'[^/]+)/(?'ChannelName'[\w%\-]+)");
            Regex regexNewFormat = new Regex(@"(http[s]{0,1}://){0,1}(www\.){0,1}(?'Host'[^/]+)/@(?'CustomId'[^/]+)");
            Match matchOldFormat = regexOldFormat.Match(channelUrl);
            Match matchNewFormat = regexNewFormat.Match(channelUrl);
            if (matchOldFormat.Success)
            {
                string host = matchOldFormat.Groups["Host"].Value.ToLower();
                if (host != "youtube.com")
                    throw new FormatException("錯誤，請確認是否輸入YouTube頻道網址");

                string type = matchOldFormat.Groups["Type"].Value.ToLower();
                if (type == "channel")
                {
                    channelId = matchOldFormat.Groups["ChannelName"].Value;
                    if (!channelId.StartsWith("UC")) throw new FormatException("錯誤，頻道Id格式不正確");
                    if (channelId.Length != 24) throw new FormatException("錯誤，頻道Id字元數不正確");
                }
                else if (type == "c" || type == "user")
                {
                    string channelName = WebUtility.UrlDecode(matchOldFormat.Groups["ChannelName"].Value);

                    if (await Program.RedisDb.KeyExistsAsync($"discord_stream_bot:ChannelNameToId:{channelName}"))
                    {
                        channelId = await Program.RedisDb.StringGetAsync($"discord_stream_bot:ChannelNameToId:{channelName}");
                    }
                    else
                    {
                        try
                        {
                            channelId = await GetChannelIdByUrlAsync($"https://www.youtube.com/{type}/{channelName}");
                            await Program.RedisDb.StringSetAsync($"discord_stream_bot:ChannelNameToId:{channelName}", channelId);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(channelUrl);
                            Log.Error(ex.ToString());
                            throw;
                        }
                    }
                }
                else throw new FormatException("錯誤，網址格式不正確");
            }
            else if (matchNewFormat.Success)
            {
                string channelName = matchNewFormat.Groups["CustomId"].Value;

                if (await Program.RedisDb.KeyExistsAsync($"discord_stream_bot:ChannelNameToId:{channelName}"))
                {
                    channelId = await Program.RedisDb.StringGetAsync($"discord_stream_bot:ChannelNameToId:{channelName}");
                }
                else
                {
                    try
                    {
                        channelId = await GetChannelIdByUrlAsync($"https://www.youtube.com/@{channelName}");
                        await Program.RedisDb.StringSetAsync($"discord_stream_bot:ChannelNameToId:{channelName}", channelId);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(channelUrl);
                        Log.Error(ex.ToString());
                        throw;
                    }
                }
            }
            else throw new FormatException("錯誤，請確認是否輸入YouTube頻道網址");

            return channelId;
        }

        private async Task<string> GetChannelIdByUrlAsync(string channelUrl)
        {
            try
            {
                string channelId = "";

                //https://stackoverflow.com/a/36559834
                HtmlWeb htmlWeb = new HtmlWeb();
                var htmlDocument = await htmlWeb.LoadFromWebAsync(channelUrl);
                var node = htmlDocument.DocumentNode.Descendants().FirstOrDefault((x) => x.Name == "meta" && x.Attributes.Any((x2) => x2.Name == "itemprop" && x2.Value == "channelId"));
                if (node == null)
                    throw new UriFormatException("錯誤，找不到節點\n" +
                        "請確認是否輸入正確的YouTube頻道網址\n" +
                        "或確認該頻道是否存在");

                channelId = node.Attributes.FirstOrDefault((x) => x.Name == "content").Value;
                if (string.IsNullOrEmpty(channelId))
                    throw new UriFormatException("錯誤，找不到頻道Id\n" +
                        "請確認是否輸入正確的YouTube頻道網址\n" +
                        "或確認該頻道是否存在");

                return channelId;
            }
            catch { throw; }
        }

        public string GetVideoId(string videoUrl)
        {
            if (string.IsNullOrEmpty(videoUrl))
                throw new ArgumentNullException(videoUrl);

            videoUrl = videoUrl.Trim();

            if (videoUrl.Length == 11)
                return videoUrl;

            Regex regex = new Regex(@"(?:https?:)?(?:\/\/)?(?:[0-9A-Z-]+\.)?(?:youtu\.be\/|youtube(?:-nocookie)?\.com\S*?[^\w\s-])(?'VideoId'[\w-]{11})(?=[^\w-]|$)(?![?=&+%\w.-]*(?:['""][^<>]*>|<\/a>))[?=&+%\w.-]*"); //https://regex101.com/r/OY96XI/1
            Match match = regex.Match(videoUrl);
            if (!match.Success)
                throw new UriFormatException("錯誤，請確認是否輸入YouTube影片網址");

            return match.Groups["VideoId"].Value;
        }

        public async Task<string> GetChannelTitle(string channelId)
        {
            try
            {
                var channel = yt.Channels.List("snippet");
                channel.Id = channelId;
                var response = await channel.ExecuteAsync().ConfigureAwait(false);
                return response.Items[0].Snippet.Title;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message + "\n" + ex.StackTrace);
                return "";
            }
        }

        public async Task<List<string>> GetChannelTitle(IEnumerable<string> channelId, bool formatUrl)
        {
            try
            {
                var channel = yt.Channels.List("snippet");
                channel.Id = string.Join(",", channelId);
                var response = await channel.ExecuteAsync().ConfigureAwait(false);
                if (formatUrl) return response.Items.Select((x) => Format.Url(x.Snippet.Title, $"https://www.youtube.com/channel/{x.Id}")).ToList();
                else return response.Items.Select((x) => x.Snippet.Title).ToList();
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message + "\n" + ex.StackTrace);
                return null;
            }
        }

        private async void SubscribePubSub()
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                foreach (var item in db.YoutubeChannelSpider.Where((x) => x.LastSubscribeTime < DateTime.Now.AddDays(-7)))
                {
                    if (await PostSubscribeRequestAsync(item.ChannelId))
                    {
                        Log.Info($"已註冊YT PubSub: {item.ChannelTitle} ({item.ChannelId})");
                        item.LastSubscribeTime = DateTime.Now;
                        db.Update(item);
                    }
                }
                db.SaveChanges();
            }
        }

        //https://github.com/JulianusIV/PubSubHubBubReciever/blob/master/DefaultPlugins/YouTubeConsumer/YouTubeConsumerPlugin.cs
        public async Task<bool> PostSubscribeRequestAsync(string channelId, bool subscribe = true)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage();

                request.RequestUri = new("https://pubsubhubbub.appspot.com/subscribe");
                request.Method = HttpMethod.Post;
                string guid = Guid.NewGuid().ToString();

                var formList = new Dictionary<string, string>()
                {
                    { "hub.mode", subscribe ? "subscribe" : "unsubscribe" },
                    { "hub.topic", $"https://www.youtube.com/xml/feeds/videos.xml?channel_id={channelId}" },
                    { "hub.callback", callbackUrl },
                    { "hub.verify", "async" },
                    { "hub.secret", guid },
                    { "hub.verify_token", guid },
                    { "hub.lease_seconds", "864000"}
                };

                request.Content = new FormUrlEncodedContent(formList);
                var response = await httpClient.SendAsync(request);
                var result = response.StatusCode == HttpStatusCode.Accepted;
                if (!result)
                {
                    Log.Error($"{channelId} PubSub註冊失敗");
                    Log.Error(response.StatusCode + " - " + await response.Content.ReadAsStringAsync());
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"{channelId} PubSub註冊失敗");
                Log.Error(ex.ToString());
                return false;
            }
        }

        #region scnse
        //holoScheduleEmoji = new Timer(async (objState) =>
        //{
        //    try
        //    {
        //        HtmlWeb htmlWeb = new HtmlWeb();
        //        HtmlDocument htmlDocument = htmlWeb.Load("https://schedule.hololive.tv/lives/all");
        //        List<string> idList = new List<string>(htmlDocument.DocumentNode.Descendants()
        //            .Where((x) => x.Name == "a" &&
        //                x.Attributes["href"].Value.StartsWith("https://www.youtube.com/watch") &&
        //                x.Attributes["style"].Value.Contains("border: 3px"))
        //            .Select((x) => x.Attributes["href"].Value));

        //        htmlDocument = htmlWeb.Load("https://schedule.hololive.tv/simple");
        //        List<string> channelList = new List<string>(htmlDocument.DocumentNode.Descendants()
        //            .Where((x) => x.Name == "a" && idList.Contains(x.Attributes["href"].Value))
        //            .Select((x) => x.InnerText));

        //        List<string> emojiList = new List<string>();
        //        foreach (var item in channelList)
        //        {
        //            try
        //            {
        //                emojiList.Add(char.ConvertFromUtf32(Convert.ToInt32(item.Replace(" ", "").Split(new string[] { "\n" }, StringSplitOptions.RemoveEmptyEntries)[2].Split(new char[] { ';' })[0].Substring(2))));
        //            }
        //            catch { }
        //        }

        //        if (emojiList.Count == 0) await ModifyAsync("現在無直播");
        //        else await ModifyAsync(string.Join(string.Empty, emojiList));
        //    }
        //    catch (Exception ex)
        //    {
        //        if (!ex.Message.Contains("EOF or 0 bytes") && !ex.Message.Contains("The SSL connection"))
        //            Log.Error("Emoji\n" + ex.Message + "\n" + ex.StackTrace);
        //    }
        //}, null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3));

        //checkHoloNowStream = new Timer(async (objState) =>
        //{
        //    try
        //    {
        //        List<string> nowRecordList = new List<string>();
        //        HtmlWeb htmlWeb = new HtmlWeb();
        //        HtmlDocument htmlDocument = htmlWeb.Load("https://schedule.hololive.tv/lives/all");
        //        List<string> idList = new List<string>(htmlDocument.DocumentNode.Descendants()
        //            .Where((x) => x.Name == "a" &&
        //                x.Attributes["href"].Value.StartsWith("https://www.youtube.com/watch") &&
        //                x.Attributes["style"].Value.Contains("border: 3px"))
        //            .Select((x) => x.Attributes["href"].Value));

        //        foreach (var item in Process.GetProcessesByName("streamlink"))
        //        {
        //            try
        //            {
        //                string cmdLine = (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? item.GetCommandLine() : File.ReadAllText($"/proc/{item.Id}/cmdline"));

        //                if (cmdLine.Contains("UC"))
        //                {
        //                    try
        //                    {
        //                        cmdLine = cmdLine.Substring(cmdLine.IndexOf("UC"), 24);
        //                        nowRecordList.Add(cmdLine);
        //                    }
        //                    catch { }
        //                }
        //            }
        //            catch { }
        //        }

        //        using (var uow = new DBContext())
        //        {
        //            for (int i = 0; i < idList.Count; i += 50)
        //            {
        //                var video = yt.Videos.List("snippet,liveStreamingDetails");
        //                video.Id = string.Join(",", idList.Skip(i).Take(50));
        //                var videoResult = await video.ExecuteAsync().ConfigureAwait(false);

        //                foreach (var item in videoResult.Items)
        //                {
        //                    if (CanRecord(uow, new StreamVideo() { ChannelId = item.Snippet.ChannelId }))
        //                    {
        //                        if (item.LiveStreamingDetails.ActualEndTime == null && !nowRecordList.Contains(item.Snippet.ChannelId))
        //                        {
        //                            var streamVideo = new StreamVideo()
        //                            {
        //                                ChannelId = item.Snippet.ChannelId,
        //                                ChannelTitle = item.Snippet.ChannelTitle,
        //                                VideoId = item.Id,
        //                                VideoTitle = item.Snippet.Title,
        //                                ScheduledStartTime = item.LiveStreamingDetails.ScheduledStartTime.Value,
        //                                ChannelType = StreamVideo.YTChannelType.Holo
        //                            };
        //                            uow.HoloStreamVideo.Add(streamVideo.ConvertToHoloStreamVideo());
        //                            await uow.SaveChangesAsync().ConfigureAwait(false);

        //                            await Program.redisSub.PublishAsync("youtube.record", item.Snippet.ChannelId);
        //                        }
        //                    }
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error($"CheckHoloNowStream\n{ex}");
        //    }
        //}, null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3));

        //private async Task ModifyAsync(string text)
        //{
        //    using (var db = new DBContext())
        //    {
        //        var guildConfig = Queryable.Where(db.GuildConfig, (x) => x.ChangeNowStreamerEmojiToNoticeChannel);

        //        foreach (var item in guildConfig)
        //        {
        //            try
        //            {
        //                var guild = _client.GetGuild(item.GuildId);
        //                if (guild == null) continue;
        //                var channel = guild.GetTextChannel(item.NoticeGuildChannelId);
        //                if (channel == null) continue;

        //                if (_client.GetGuild(item.GuildId).GetUser(_client.CurrentUser.Id).GetPermissions(channel).ManageChannel)
        //                    await channel.ModifyAsync((x) => x.Name = text).ConfigureAwait(false);
        //                else
        //                    await channel.SendConfirmAsync("警告\n" +
        //                        "Bot無 `管理影片` 權限，無法變更影片名稱\n" +
        //                        "請修正權限或是關閉現在直播表情顯示功能").ConfigureAwait(false);
        //            }
        //            catch (Exception ex)
        //            {
        //                Log.Error($"Modify {item.GuildId} / {item.NoticeGuildChannelId}\n{ex.Message}");
        //                item.ChangeNowStreamerEmojiToNoticeChannel = false;
        //                db.GuildConfig.Update(item);
        //                db.SaveChanges();
        //            }
        //        }
        //    }
        //}
        #endregion
    }

    public class ReminderItem
    {
        public DataBase.Table.Video StreamVideo { get; set; }
        public Timer Timer { get; set; }
        public DataBase.Table.Video.YTChannelType ChannelType { get; set; }
    }

    public class YoutubePubSubNotification
    {
        public enum YTNotificationType { CreateOrUpdated, Deleted }

        public YTNotificationType NotificationType { get; set; } = YTNotificationType.CreateOrUpdated;
        public string VideoId { get; set; }
        public string ChannelId { get; set; }
        public string Title { get; set; }
        public string Link { get; set; }
        public DateTime Published { get; set; }
        public DateTime Updated { get; set; }

        public override string ToString()
        {
            switch (NotificationType)
            {
                case YTNotificationType.CreateOrUpdated:
                    return $"({NotificationType} at {Updated}) {ChannelId} - {VideoId} | {Title}";
                case YTNotificationType.Deleted:
                    return $"({NotificationType} at {Published}) {ChannelId} - {VideoId}";
            }
            return "";
        }
    }

    public static class Ext
    {
        public static DateTime? ConvertDateTime(this string text)
        {
            try
            {
                return Convert.ToDateTime(text);
            }
            catch
            {
                return new DateTime();
            }
        }
    }
}