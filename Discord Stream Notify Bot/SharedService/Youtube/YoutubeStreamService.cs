using Discord.Interactions;
using Discord_Stream_Notify_Bot.Interaction;
using Discord_Stream_Notify_Bot.SharedService.Youtube.Json;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using HtmlAgilityPack;
using System.Collections.Concurrent;
using System.Data;
using System.Net;
using System.Text.RegularExpressions;

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

        public ConcurrentBag<NijisanjiLiverJson> NijisanjiLiverContents { get; } = new ConcurrentBag<NijisanjiLiverJson>();
        public ConcurrentDictionary<string, ReminderItem> Reminders { get; } = new ConcurrentDictionary<string, ReminderItem>();
        public bool IsRecord { get; set; } = true;
        public YouTubeService YouTubeService { get; set; }

        private Timer holoSchedule, nijisanjiSchedule, otherSchedule, checkScheduleTime, saveDateBase, subscribePubSub, reScheduleTime/*, checkHoloNowStream, holoScheduleEmoji*/;
        private readonly DiscordSocketClient _client;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly EmojiService _emojiService;
        private readonly ConcurrentDictionary<string, byte> _endLiveBag = new();
        private readonly string _callbackUrl;

        public YoutubeStreamService(DiscordSocketClient client, IHttpClientFactory httpClientFactory, BotConfig botConfig, EmojiService emojiService)
        {
            _client = client;
            _httpClientFactory = httpClientFactory;

            YouTubeService = new YouTubeService(new BaseClientService.Initializer
            {
                ApplicationName = "DiscordStreamBot",
                ApiKey = botConfig.GoogleApiKey,
            });

            _callbackUrl = botConfig.PubSubCallbackUrl;
            _emojiService = emojiService;

#if RELEASE
            if (botConfig.DetectGuildId != 0 && botConfig.DetectCategoryId != 0)
            {
                _client.ChannelCreated += async (channel) =>
                {
                    if (channel is not IVoiceChannel voiceChannel)
                        return;

                    if (voiceChannel.GuildId != botConfig.DetectGuildId)
                        return;

                    if (voiceChannel.CategoryId != botConfig.DetectCategoryId)
                        return;

                    await Program.ApplicatonOwner.SendMessageAsync($"`{voiceChannel.Guild}` 建立了新頻道 `{voiceChannel}`");
                    return;
                };
            }
#endif

            if (Program.Redis != null)
            {
                Program.RedisSub.Subscribe(new RedisChannel("youtube.startstream", RedisChannel.PatternMode.Literal), async (channel, videoData) =>
                {
                    try
                    {
                        string videoId = "";
                        bool isMemberOnly = false;
                        var tempData = videoData.ToString().Split(':');
                        if (tempData.Length != 2)
                        {
                            Log.Info($"{channel} - {videoData}: 資料數量不正確");
                            videoId = videoData.ToString().Substring(0, 11);
                        }
                        else
                        {
                            videoId = tempData[0];
                            isMemberOnly = tempData[1] == "1";
                        }

                        Log.Info($"{channel} - {videoId}");

                        var item = await GetVideoAsync(videoId).ConfigureAwait(false);
                        if (item == null)
                        {
                            Log.Warn($"{videoId} Delete");
                            await Program.RedisSub.PublishAsync(new RedisChannel("youtube.deletestream", RedisChannel.PatternMode.Literal), videoId);
                            return;
                        }

                        DateTime startTime;
                        if (!string.IsNullOrEmpty(item.LiveStreamingDetails.ActualStartTimeRaw))
                            startTime = DateTime.Parse(item.LiveStreamingDetails.ActualStartTimeRaw);
                        else
                            startTime = DateTime.Parse(item.LiveStreamingDetails.ScheduledStartTimeRaw);

                        EmbedBuilder embedBuilder = new EmbedBuilder()
                        .WithTitle(item.Snippet.Title)
                        .WithDescription(Format.Url(item.Snippet.ChannelTitle, $"https://www.youtube.com/channel/{item.Snippet.ChannelId}"))
                        .WithImageUrl($"https://i.ytimg.com/vi/{item.Id}/maxresdefault.jpg")
                        .WithUrl($"https://www.youtube.com/watch?v={item.Id}")
                        .AddField("直播狀態", "開台中")
                        .AddField("開台時間", startTime.ConvertDateTimeToDiscordMarkdown());

                        if (isMemberOnly) embedBuilder.WithOkColor();
                        else embedBuilder.WithRecordColor();

                        await SendStreamMessageAsync(item.Id, embedBuilder, NoticeType.Start).ConfigureAwait(false);
                        await ChangeGuildBannerAsync(item.Snippet.ChannelId, item.Id);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Record-StartStream {ex}");
                    }
                });

                Program.RedisSub.Subscribe(new RedisChannel("youtube.endstream", RedisChannel.PatternMode.Literal), async (channel, videoId) =>
                {
                    try
                    {
                        Log.Info($"{channel} - {videoId}");

                        if (_endLiveBag.ContainsKey(videoId))
                        {
                            Log.Warn("重複通知，略過");
                            return;
                        }

                        var item = await GetVideoAsync(videoId.ToString()).ConfigureAwait(false);
                        if (item == null)
                        {
                            Log.Warn($"{videoId} Delete");
                            await Program.RedisSub.PublishAsync(new RedisChannel("youtube.deletestream", RedisChannel.PatternMode.Literal), videoId);
                            return;
                        }

                        if (string.IsNullOrEmpty(item.LiveStreamingDetails.ActualEndTimeRaw))
                        {
                            Log.Warn("還沒關台");
                            return;
                        }

                        _endLiveBag.TryAdd(videoId, 1);

                        var startTime = DateTime.Parse(item.LiveStreamingDetails.ActualStartTimeRaw);
                        var endTime = DateTime.Parse(item.LiveStreamingDetails.ActualEndTimeRaw);

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

                Program.RedisSub.Subscribe(new RedisChannel("youtube.memberonly", RedisChannel.PatternMode.Literal), async (channel, videoId) =>
                {
                    Log.Info($"{channel} - {videoId}");

                    if (_endLiveBag.ContainsKey(videoId))
                    {
                        Log.Warn("重複通知，略過");
                        return;
                    }

                    using (var db = DataBase.MainDbContext.GetDbContext())
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
                                    await Program.RedisSub.PublishAsync(new RedisChannel("youtube.deletestream", RedisChannel.PatternMode.Literal), videoId);
                                    return;
                                }

                                if (string.IsNullOrEmpty(item.LiveStreamingDetails.ActualEndTimeRaw))
                                {
                                    Log.Warn("還沒關台");
                                    return;
                                }

                                _endLiveBag.TryAdd(videoId, 1);

                                var startTime = DateTime.Parse(item.LiveStreamingDetails.ActualStartTimeRaw);
                                var endTime = DateTime.Parse(item.LiveStreamingDetails.ActualEndTimeRaw);

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

                Program.RedisSub.Subscribe(new RedisChannel("youtube.deletestream", RedisChannel.PatternMode.Literal), async (channel, videoId) =>
                {
                    Log.Info($"{channel} - {videoId}");

                    if (_endLiveBag.ContainsKey(videoId))
                    {
                        Log.Warn("重複通知，略過");
                        return;
                    }

                    _endLiveBag.TryAdd(videoId, 1);

                    using (var db = DataBase.MainDbContext.GetDbContext())
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

                Program.RedisSub.Subscribe(new RedisChannel("youtube.unarchived", RedisChannel.PatternMode.Literal), async (channel, videoId) =>
                {
                    Log.Info($"{channel} - {videoId}");

                    if (_endLiveBag.ContainsKey(videoId))
                    {
                        Log.Warn("重複通知，略過");
                        return;
                    }

                    _endLiveBag.TryAdd(videoId, 1);

                    using (var db = DataBase.MainDbContext.GetDbContext())
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

                Program.RedisSub.Subscribe(new RedisChannel("youtube.429error", RedisChannel.PatternMode.Literal), async (channel, videoId) =>
                {
                    Log.Info($"{channel} - {videoId}");
                    IsRecord = false;

                    using (var db = DataBase.MainDbContext.GetDbContext())
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

                Program.RedisSub.Subscribe(new RedisChannel("youtube.addstream", RedisChannel.PatternMode.Literal), async (channel, videoId) =>
                {
                    videoId = GetVideoId(videoId);
                    Log.Info($"{channel} - (手動新增) {videoId}");

                    try
                    {
                        using (var db = DataBase.MainDbContext.GetDbContext())
                        {
                            if (!addNewStreamVideo.ContainsKey(videoId) && !Extensions.HasStreamVideoByVideoId(videoId))
                            {
                                var item = await GetVideoAsync(videoId).ConfigureAwait(false);
                                if (item == null)
                                {
                                    Log.Warn($"{videoId} Delete");
                                    return;
                                }

                                try
                                {
                                    await AddOtherDataAsync(item);
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, $"PubSub-AddStream: {item.Id}");
                                }
                            }
                            else
                            {
                                Log.Warn($"{videoId} 已存在，略過");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"PubSub-AddStream {ex}");
                    }
                });

                Program.RedisSub.Subscribe(new RedisChannel("youtube.pubsub.CreateOrUpdate", RedisChannel.PatternMode.Literal), async (channel, youtubeNotificationJson) =>
                {
                    YoutubePubSubNotification youtubePubSubNotification = JsonConvert.DeserializeObject<YoutubePubSubNotification>(youtubeNotificationJson.ToString());

                    try
                    {
                        using (var db = DataBase.MainDbContext.GetDbContext())
                        {
                            if (!addNewStreamVideo.ContainsKey(youtubePubSubNotification.VideoId) && !Extensions.HasStreamVideoByVideoId(youtubePubSubNotification.VideoId))
                            {
                                Log.Info($"{channel} - (新影片) {youtubePubSubNotification.ChannelId}: {youtubePubSubNotification.VideoId}");

                                DataBase.Table.Video streamVideo;
                                var youtubeChannelSpider = db.YoutubeChannelSpider.FirstOrDefault((x) => x.ChannelId == youtubePubSubNotification.ChannelId);

                                // 這邊要拿 2434 的資料庫，確認接進來的頻道 Id 是不是 2434 的
                                using var db2 = DataBase.NijisanjiVideoContext.GetDbContext();

                                if (db.RecordYoutubeChannel.Any((x) => x.YoutubeChannelId == youtubePubSubNotification.ChannelId) // 錄影頻道一律允許
                                    || db2.Video.Any((x) => x.ChannelId == youtubePubSubNotification.ChannelId) || // 可能是 2434 的頻道，允許
                                    (youtubeChannelSpider != null && youtubeChannelSpider.IsTrustedChannel)) // 否則就確認這是不是允許的爬蟲
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
                                        Log.Error(ex, $"PubSub_AddData_CreateOrUpdate: {item.Id}");
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
                                        ChannelType = DataBase.Table.Video.YTChannelType.NonApproved
                                    };

                                    Log.New($"(非已認可的新影片) | {youtubePubSubNotification.Published} | {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                    EmbedBuilder embedBuilder = new EmbedBuilder();
                                    embedBuilder.WithOkColor()
                                    .WithTitle(streamVideo.VideoTitle)
                                    .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                    .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                    .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                    .AddField("上傳時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                    if (addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo) && streamVideo.ScheduledStartTime > DateTime.Now.AddDays(-2))
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

                Program.RedisSub.Subscribe(new RedisChannel("youtube.pubsub.Deleted", RedisChannel.PatternMode.Literal), async (channel, youtubeNotificationJson) =>
                {
                    YoutubePubSubNotification youtubePubSubNotification = JsonConvert.DeserializeObject<YoutubePubSubNotification>(youtubeNotificationJson.ToString());

                    Log.Info($"{channel} - {youtubePubSubNotification.VideoId}");

                    try
                    {
                        using (var db = DataBase.MainDbContext.GetDbContext())
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

                Program.RedisSub.Subscribe(new RedisChannel("youtube.pubsub.NeedRegister", RedisChannel.PatternMode.Literal), async (channel, channelId) =>
                {
                    using (var db = DataBase.MainDbContext.GetDbContext())
                    {
                        if (db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId.ToString()))
                        {
                            var youtubeChannelSpider = db.YoutubeChannelSpider.Single((x) => x.ChannelId == channelId.ToString());

                            if (await PostSubscribeRequestAsync(channelId.ToString()))
                            {
                                Log.Info($"已重新註冊 YT PubSub: {youtubeChannelSpider.ChannelTitle} ({channelId})");
                                youtubeChannelSpider.LastSubscribeTime = DateTime.Now;
                                db.Update(youtubeChannelSpider);
                                db.SaveChanges();
                            }
                        }
                        else
                        {
                            Log.Error($"後端通知須重新註冊但資料庫無該 ChannelId 的資料: {channelId}");
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

                Log.Info("已建立 Redis 訂閱");
            }

            reScheduleTime = new Timer((objState) => ReScheduleReminder(), null, TimeSpan.FromSeconds(5), TimeSpan.FromDays(1));

            foreach (var item in new string[] { "nijisanji", "nijisanjien", "virtuareal" })
            {
                Task.Run(async () => await GetOrCreateNijisanjiLiverListAsync(item));
            }

            holoSchedule = new Timer(async (objState) => await HoloScheduleAsync(), null, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(5));

            nijisanjiSchedule = new Timer(async (objState) => await NijisanjiScheduleAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5));

            otherSchedule = new Timer(async (objState) => await OtherScheduleAsync(), null, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(5));

            checkScheduleTime = new Timer(async (objState) => await CheckScheduleTime(), null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));

            saveDateBase = new Timer((objState) => SaveDateBase(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3));

#if !RELEASE
            return;
#endif

            subscribePubSub = new Timer((objState) => SubscribePubSub(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30));
        }

        private async Task GetOrCreateNijisanjiLiverListAsync(string affiliation, bool forceRefresh = false)
        {
            if (!forceRefresh)
            {
                try
                {
                    if (await Program.RedisDb.KeyExistsAsync($"youtube.nijisanji.liver.{affiliation}"))
                    {
                        var liver = JsonConvert.DeserializeObject<List<NijisanjiLiverJson>>(await Program.RedisDb.StringGetAsync($"youtube.nijisanji.liver.{affiliation}"));
                        foreach (var item in liver)
                        {
                            NijisanjiLiverContents.Add(item);
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"GetOrCreateNijisanjiLiverListAsync-GetRedisData-{affiliation}: {ex}");
                }
            }

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var json = await httpClient.GetStringAsync($"https://www.nijisanji.jp/api/livers?limit=300&offset=0&orderKey=subscriber_count&order=desc&affiliation={affiliation}&locale=ja&includeAll=true&includeHidden=true");
                var liver = JsonConvert.DeserializeObject<List<NijisanjiLiverJson>>(json);
                await Program.RedisDb.StringSetAsync($"youtube.nijisanji.liver.{affiliation}", JsonConvert.SerializeObject(liver), TimeSpan.FromDays(1));
                foreach (var item in liver)
                {
                    NijisanjiLiverContents.Add(item);
                }
                Log.New($"GetOrCreateNijisanjiLiverListAsync: {affiliation} 已刷新");
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

                var video = YouTubeService.Videos.List("snippet");
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

        private bool CanRecord(DataBase.MainDbContext db, DataBase.Table.Video streamVideo) =>
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

            channelUrl = channelUrl.Replace("https://m.youtube.com", "https://www.youtube.com");
            channelUrl = channelUrl.Split('?')[0]; // 移除網址上的參數

            Regex regexOldFormat = new Regex(@"(http[s]{0,1}://){0,1}(www\.){0,1}(?'Host'[^/]+)/(?'Type'[^/]+)/(?'ChannelName'[\w%\-]+)");
            Regex regexNewFormat = new Regex(@"(http[s]{0,1}://){0,1}(www\.){0,1}(?'Host'[^/]+)/@(?'CustomId'[^/]+)");
            Match matchOldFormat = regexOldFormat.Match(channelUrl);
            Match matchNewFormat = regexNewFormat.Match(channelUrl);
            if (matchOldFormat.Success)
            {
                string host = matchOldFormat.Groups["Host"].Value.ToLower();
                if (host != "youtube.com")
                    throw new FormatException("錯誤，請確認是否輸入 YouTube 頻道網址");

                string type = matchOldFormat.Groups["Type"].Value.ToLower();
                if (type == "channel")
                {
                    channelId = matchOldFormat.Groups["ChannelName"].Value;
                    if (!channelId.StartsWith("UC")) throw new FormatException("錯誤，頻道 Id 格式不正確");
                    if (channelId.Length != 24) throw new FormatException("錯誤，頻道 Id 字元數不正確");
                }
                else if (type == "c" || type == "user")
                {
                    string channelName = WebUtility.UrlDecode(matchOldFormat.Groups["ChannelName"].Value);

                    using (var db = DataBase.MainDbContext.GetDbContext())
                    {
                        channelId = db.YoutubeChannelNameToId.SingleOrDefault((x) => x.ChannelName == channelName)?.ChannelId;

                        if (string.IsNullOrEmpty(channelId))
                        {
                            try
                            {
                                channelId = await GetChannelIdByUrlAsync($"https://www.youtube.com/{type}/{channelName}");
                                db.YoutubeChannelNameToId.Add(new DataBase.Table.YoutubeChannelNameToId() { ChannelName = channelName, ChannelId = channelId });
                                await db.SaveChangesAsync();
                            }
                            catch (UriFormatException)
                            {
                                Log.Error($"GetChannelIdAsync-GetChannelIdByUrlAsync: {channelUrl}");
                                throw;
                            }
                            catch (Exception ex)
                            {
                                Log.Error(channelUrl);
                                Log.Error(ex.ToString());
                                throw;
                            }
                        }
                    }
                }
                else throw new FormatException("錯誤，網址格式不正確");
            }
            else
            {
                string channelName = channelUrl.Replace("@", "");
                if (matchNewFormat.Success)
                    channelName = matchNewFormat.Groups["CustomId"].Value;

                using (var db = DataBase.MainDbContext.GetDbContext())
                {
                    channelId = db.YoutubeChannelNameToId.SingleOrDefault((x) => x.ChannelName == channelName)?.ChannelId;

                    if (string.IsNullOrEmpty(channelId))
                    {
                        try
                        {
                            channelId = await GetChannelIdByUrlAsync($"https://www.youtube.com/@{channelName}");
                            db.YoutubeChannelNameToId.Add(new DataBase.Table.YoutubeChannelNameToId() { ChannelName = channelName, ChannelId = channelId });
                            await db.SaveChangesAsync();
                        }
                        catch (UriFormatException)
                        {
                            Log.Error($"GetChannelIdAsync-GetChannelIdByUrlAsync-UriFormatException: {channelUrl}");
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, $"GetChannelIdAsync-GetChannelIdByUrlAsync-Exception: {channelUrl}");
                            throw;
                        }
                    }
                }
            }

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
                var node = htmlDocument.DocumentNode.Descendants().FirstOrDefault((x) => x.Name == "meta" && x.Attributes.Any((x2) => x2.Name == "itemprop" && x2.Value == "channelId" || x2.Value == "identifier"));

                // 已知阿喵喵的頻道 (https://www.youtube.com/@AmamiyaKokoro) 會被 YT 自動 303 轉址
                // 但只會轉一半 (Location: https://www.youtube.com/UCkIimWZ9gBJRamKF0rmPU8w ， 缺少 channel)
                // 這需要 HttpClient 把 Header 抓出來處理，等有頻道也會發生這情況時再處理

                // Vox 的頻道也會，但他只是從 https://www.youtube.com/@VoxAkuma 變成 https://www.youtube.com/voxakuma
                // 然後一樣 404 ，幹

                if (node == null)
                    throw new UriFormatException("錯誤，找不到節點\n" +
                        "請確認是否輸入正確的 YouTube 頻道網址，或確認該頻道是否存在\n" +
                        "部分頻道會有跳轉後遇到 404 錯誤的問題，你可以嘗試直接開啟連結確認是否會出現 404 錯誤\n" +
                        "有需要可直接向 Bot 擁有者詢問\n" +
                        "(你可以使用 `/utility send-message-to-bot-owner` 指令來聯絡 Bot 擁有者)");

                channelId = node.Attributes.FirstOrDefault((x) => x.Name == "content").Value;
                if (string.IsNullOrEmpty(channelId))
                    throw new UriFormatException("錯誤，找不到頻道 Id\n" +
                        "正常來說不該遇到這問題才對，請直接向 Bot 擁有者詢問\n" +
                        "(你可以使用 `/utility send-message-to-bot-owner` 指令來聯絡 Bot 擁有者)");

                return channelId;
            }
            catch { throw; }
        }

        public string GetVideoId(string videoUrl)
        {
            if (string.IsNullOrEmpty(videoUrl))
                throw new ArgumentNullException(videoUrl);

            videoUrl = videoUrl.Trim();

            if (videoUrl.Contains("www.youtube.com/watch")) //https://www.youtube.com/watch?v=7DqDRE_SW34
                videoUrl = videoUrl.Substring(videoUrl.IndexOf("?v=") + 3, 11);
            else if (videoUrl.Contains("https://youtu.be")) //https://youtu.be/Z-UJbyLqioM
                videoUrl = videoUrl.Substring(17, 11);
            else if (videoUrl.Contains("https://www.youtube.com/live/")) //https://www.youtube.com/live/MdmQgxffY6k?feature=share
                videoUrl = videoUrl.Substring(29, 11);

            if (videoUrl.Length == 11)
                return videoUrl;

            Regex regex = new Regex(@"(?:https?:)?(?:\/\/)?(?:[0-9A-Z-]+\.)?(?:youtu\.be\/|youtube(?:-nocookie)?\.com\S*?[^\w\s-])(?'VideoId'[\w-]{11})(?=[^\w-]|$)(?![?=&+%\w.-]*(?:['""][^<>]*>|<\/a>))[?=&+%\w.-]*"); //https://regex101.com/r/OY96XI/1
            Match match = regex.Match(videoUrl);
            if (!match.Success)
                throw new UriFormatException("錯誤，請確認是否輸入 YouTube 影片網址");

            return match.Groups["VideoId"].Value;
        }

        public async Task<string> GetChannelTitle(string channelId)
        {
            try
            {
                var channel = YouTubeService.Channels.List("snippet");
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
                var channel = YouTubeService.Channels.List("snippet");
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
            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                foreach (var item in db.YoutubeChannelSpider.Where((x) => x.LastSubscribeTime < DateTime.Now.AddDays(-7)))
                {
                    if (await PostSubscribeRequestAsync(item.ChannelId))
                    {
                        Log.Info($"已註冊 YT PubSub: {item.ChannelTitle} ({item.ChannelId})");
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
                    { "hub.callback", _callbackUrl },
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
                    Log.Error($"{channelId} PubSub 註冊失敗");
                    Log.Error(response.StatusCode + " - " + await response.Content.ReadAsStringAsync());
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"{channelId} PubSub 註冊失敗");
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