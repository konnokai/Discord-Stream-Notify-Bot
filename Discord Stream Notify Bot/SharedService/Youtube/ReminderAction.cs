using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction;
using Polly;

namespace Discord_Stream_Notify_Bot.SharedService.Youtube
{
    public partial class YoutubeStreamService
    {
        private void StartReminder(DataBase.Table.Video streamVideo, DataBase.Table.Video.YTChannelType channelType)
        {
            if (streamVideo.ScheduledStartTime > DateTime.Now.AddDays(7)) return;

            try
            {
                TimeSpan ts = streamVideo.ScheduledStartTime.AddMinutes(-1).Subtract(DateTime.Now);

                if (ts <= TimeSpan.Zero)
                {
                    ReminderTimerAction(streamVideo);
                }
                else
                {
                    var remT = new Timer(ReminderTimerAction, streamVideo, Math.Max(1000, (long)ts.TotalMilliseconds), Timeout.Infinite);

                    if (!Reminders.TryAdd(streamVideo.VideoId, new ReminderItem() { StreamVideo = streamVideo, Timer = remT, ChannelType = channelType }))
                    {
                        remT.Change(Timeout.Infinite, Timeout.Infinite);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"StartReminder: {streamVideo.VideoTitle} - {streamVideo.ScheduledStartTime}");
                throw;
            }
        }

        private async void ReminderTimerAction(object rObj)
        {
            var streamVideo = (DataBase.Table.Video)rObj;

            try
            {
                Google.Apis.YouTube.v3.Data.Video videoResult;
                try
                {
                    videoResult = await GetVideoAsync(streamVideo.VideoId);
                    if (videoResult == null)
                    {
                        Log.Info($"{streamVideo.VideoId} 待機所被刪了");

                        EmbedBuilder embedBuilder = new EmbedBuilder();
                        embedBuilder.WithErrorColor()
                        .WithTitle(streamVideo.VideoTitle)
                        .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                        .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                        .AddField("直播狀態", "已刪除直播")
                        .AddField("排定開台時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                        await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.Delete).ConfigureAwait(false);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"ReminderTimerAction-CheckVideoExist: {ex}");
                    return;
                }

                DateTime startTime;
                if (!string.IsNullOrEmpty(videoResult.LiveStreamingDetails.ScheduledStartTimeRaw)) startTime = DateTime.Parse(videoResult.LiveStreamingDetails.ScheduledStartTimeRaw);
                else startTime = DateTime.Parse(videoResult.LiveStreamingDetails.ActualStartTimeRaw);

                if (startTime.AddMinutes(-2) < DateTime.Now)
                {
                    bool isRecord = false;
                    streamVideo.VideoTitle = videoResult.Snippet.Title;

                    if (Extensions.HasStreamVideoByVideoId(streamVideo.VideoId))
                    {
                        switch (streamVideo.ChannelType)
                        {
                            case DataBase.Table.Video.YTChannelType.Holo:
                                using (var videoDb = DataBase.HoloVideoContext.GetDbContext())
                                {
                                    try
                                    {
                                        var data = videoDb.Video.First((x) => x.VideoId == streamVideo.VideoId);
                                        data.VideoTitle = streamVideo.VideoTitle;
                                        videoDb.Video.Update(data);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(ex.ToString());
                                    }
                                }
                                break;
                            case DataBase.Table.Video.YTChannelType.Nijisanji:
                                using (var videoDb = DataBase.NijisanjiVideoContext.GetDbContext())
                                {
                                    try
                                    {
                                        var data = videoDb.Video.First((x) => x.VideoId == streamVideo.VideoId);
                                        data.VideoTitle = streamVideo.VideoTitle;
                                        videoDb.Video.Update(data);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(ex.ToString());
                                    }
                                }
                                break;
                            case DataBase.Table.Video.YTChannelType.Other:
                                using (var videoDb = DataBase.OtherVideoContext.GetDbContext())
                                {
                                    try
                                    {
                                        var data = videoDb.Video.First((x) => x.VideoId == streamVideo.VideoId);
                                        data.VideoTitle = streamVideo.VideoTitle;
                                        videoDb.Video.Update(data);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(ex.ToString());
                                    }
                                }
                                break;
                        }
                    }

#if RELEASE
                    try
                    {
                        using (var db = DataBase.DBContext.GetDbContext())
                        {
                            if (CanRecord(db, streamVideo))
                            {
                                if (Program.Redis != null)
                                {
                                    if (await Program.RedisSub.PublishAsync(new RedisChannel("youtube.record", RedisChannel.PatternMode.Literal), streamVideo.VideoId) != 0)
                                    {
                                        Log.Info($"已發送錄影請求: {streamVideo.VideoId}");
                                        isRecord = true;

                                        if (noticeRecordChannel != null)
                                        {
                                            await noticeRecordChannel.SendMessageAsync(embeds: new Embed[] { new EmbedBuilder().WithOkColor()
                                                .WithDescription($"{Format.Url(streamVideo.VideoTitle, $"https://www.youtube.com/watch?v={streamVideo.VideoId}")}\n" +
                                                $"{Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}")}\n\n" +
                                                $"youtube_{streamVideo.ChannelId}_{streamVideo.ScheduledStartTime:yyyyMMdd_HHmmss}_{streamVideo.VideoId}.ts").Build() });
                                        }
                                    }
                                    else Log.Warn($"Redis Sub頻道不存在，請開啟錄影工具: {streamVideo.VideoId}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"ReminderTimerAction-Record: {streamVideo.VideoId}\n{ex}");
                    }
#endif

                    await ChangeGuildBannerAsync(streamVideo.ChannelId, streamVideo.VideoId);

                    if (!isRecord)
                    {
                        EmbedBuilder embedBuilder = new EmbedBuilder();
                        embedBuilder.WithTitle(streamVideo.VideoTitle)
                        .WithOkColor()
                        .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                        .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                        .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                        .AddField("直播狀態", "開台中")
                        .AddField("排定開台時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                        await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.Start).ConfigureAwait(false);
                    }

                    if (Reminders.TryRemove(streamVideo.VideoId, out var t))
                        t.Timer.Change(Timeout.Infinite, Timeout.Infinite);
                }
                else
                {
                    Log.Info($"時間已更改 {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                    EmbedBuilder embedBuilder = new EmbedBuilder();
                    embedBuilder.WithErrorColor()
                    .WithTitle(streamVideo.VideoTitle)
                    .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                    .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                    .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                    .AddField("直播狀態", "尚未開台(已更改時間)")
                    .AddField("排定開台時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown())
                    .AddField("更改開台時間", startTime.ConvertDateTimeToDiscordMarkdown());

                    streamVideo.ScheduledStartTime = startTime;
                    switch (streamVideo.ChannelType)
                    {
                        case DataBase.Table.Video.YTChannelType.Holo:
                            using (var videoDb = DataBase.HoloVideoContext.GetDbContext())
                            {
                                try
                                {
                                    var data = videoDb.Video.First((x) => x.VideoId == streamVideo.VideoId);
                                    data.ScheduledStartTime = streamVideo.ScheduledStartTime;
                                    videoDb.UpdateAndSave(data);
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex.Message + "\n" + ex.StackTrace);
                                }
                            }
                            break;
                        case DataBase.Table.Video.YTChannelType.Nijisanji:
                            using (var videoDb = DataBase.NijisanjiVideoContext.GetDbContext())
                            {
                                try
                                {
                                    var data = videoDb.Video.First((x) => x.VideoId == streamVideo.VideoId);
                                    data.ScheduledStartTime = streamVideo.ScheduledStartTime;
                                    videoDb.UpdateAndSave(data);
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex.Message + "\n" + ex.StackTrace);
                                }
                            }
                            break;
                        case DataBase.Table.Video.YTChannelType.Other:
                            using (var videoDb = DataBase.OtherVideoContext.GetDbContext())
                            {
                                try
                                {
                                    var data = videoDb.Video.First((x) => x.VideoId == streamVideo.VideoId);
                                    data.ScheduledStartTime = streamVideo.ScheduledStartTime;
                                    videoDb.UpdateAndSave(data);
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex.Message + "\n" + ex.StackTrace);
                                }
                            }
                            break;
                    }

                    await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.ChangeTime).ConfigureAwait(false);

                    if (Reminders.TryRemove(streamVideo.VideoId, out var t))
                        t.Timer.Change(Timeout.Infinite, Timeout.Infinite);

                    StartReminder(streamVideo, streamVideo.ChannelType);
                }
            }
            catch (Exception ex) { Log.Error(ex, $"ReminderAction: {streamVideo.VideoId}"); }
        }

        private async Task SendStreamMessageAsync(string videolId, EmbedBuilder embedBuilder, NoticeType noticeType)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                DataBase.Table.Video streamVideo = Extensions.GetStreamVideoByVideoId(videolId);

                if (streamVideo == null)
                {
                    if (addNewStreamVideo.ContainsKey(videolId))
                    {
                        streamVideo = addNewStreamVideo[videolId];
                    }
                    else
                    {
                        try
                        {
                            var item = await GetVideoAsync(videolId).ConfigureAwait(false);

                            var startTime = DateTime.Parse(item.LiveStreamingDetails.ActualStartTimeRaw);
                            streamVideo = new DataBase.Table.Video()
                            {
                                ChannelId = item.Snippet.ChannelId,
                                ChannelTitle = item.Snippet.ChannelTitle,
                                VideoId = item.Id,
                                VideoTitle = item.Snippet.Title,
                                ScheduledStartTime = startTime,
                                ChannelType = DataBase.Table.Video.YTChannelType.Other
                            };

                            if (!addNewStreamVideo.TryAdd(streamVideo.VideoId, streamVideo))
                                return;
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, $"SendStreamMessageAsync-GetVideoAsync: {videolId}");
                            return;
                        }
                    }
                }

                await SendStreamMessageAsync(streamVideo, embedBuilder, noticeType).ConfigureAwait(false);
            }
        }

        private async Task SendStreamMessageAsync(DataBase.Table.Video streamVideo, EmbedBuilder embedBuilder, NoticeType noticeType)
        {
            if (!Program.isConnect)
                return;

            string type;
            switch (streamVideo.ChannelType)
            {
                case DataBase.Table.Video.YTChannelType.Holo:
                    type = "holo";
                    break;
                case DataBase.Table.Video.YTChannelType.Nijisanji:
                    type = "2434";
                    break;
                default:
                    type = "other";
                    break;
            }

            List<NoticeYoutubeStreamChannel> noticeGuildList = new List<NoticeYoutubeStreamChannel>();
            using (var db = DataBase.DBContext.GetDbContext())
            {
                try
                {
                    // 有設定該頻道的通知就不用過濾，他們肯定是要這頻道的通知
                    noticeGuildList.AddRange(db.NoticeYoutubeStreamChannel.Where((x) => x.NoticeStreamChannelId == streamVideo.ChannelId));
                }
                catch (Exception ex)
                {
                    // 原則上不會有錯，我也不知道加這幹嘛
                    Log.Error($"SendStreamMessageAsyncChannel: {streamVideo.VideoId}\n{ex}");
                }

                //類型檢查，其他類型的頻道要特別檢查，確保必須是認可的頻道才可被添加到其他類型通知
                try
                {
                    if (type != "other" || //如果不是其他類的頻道，直接添加到對應的類型通知即可
                        !db.YoutubeChannelSpider.Any((x) => x.ChannelId == streamVideo.ChannelId) || //若該頻道非在爬蟲清單內，那也沒有認不認可的問題
                        db.YoutubeChannelSpider.First((x) => x.ChannelId == streamVideo.ChannelId).IsTrustedChannel) //最後該爬蟲必須是已認可的頻道，才可添加至其他類型的通知
                    {
                        noticeGuildList.AddRange(db.NoticeYoutubeStreamChannel.Where((x) => x.NoticeStreamChannelId == "all" || x.NoticeStreamChannelId == type));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"SendStreamMessageAsyncOtherChannel: {streamVideo.VideoId}\n{ex}");
                }

                Log.New($"發送直播通知 ({noticeGuildList.Count} / {noticeType}): {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

#if DEBUG || DEBUG_DONTREGISTERCOMMAND
                return;
#endif

                MessageComponent comp = null;
                if (noticeType == NoticeType.Start)
                {
                    comp = new ComponentBuilder()
                        .WithButton("好手氣，隨機帶你到一個影片或直播", style: ButtonStyle.Link, emote: _emojiService.YouTubeEmote, url: "https://api.konnokai.me/randomvideo")
                        .WithButton("贊助小幫手 (Patreon) #ad", style: ButtonStyle.Link, emote: _emojiService.PatreonEmote, url: Utility.PatreonUrl, row: 1)
                        .WithButton("贊助小幫手 (Paypal) #ad", style: ButtonStyle.Link, emote: _emojiService.PayPalEmote, url: Utility.PaypalUrl, row: 1).Build();
                }

                foreach (var item in noticeGuildList)
                {
                    try
                    {
                        string sendMessage = "";
                        switch (noticeType)
                        {
                            case NoticeType.NewStream:
                                sendMessage = item.NewStreamMessage;
                                break;
                            case NoticeType.NewVideo:
                                sendMessage = item.NewVideoMessage;
                                break;
                            case NoticeType.Start:
                                sendMessage = item.StratMessage;
                                break;
                            case NoticeType.End:
                                sendMessage = item.EndMessage;
                                break;
                            case NoticeType.ChangeTime:
                                sendMessage = item.ChangeTimeMessage;
                                break;
                            case NoticeType.Delete:
                                sendMessage = item.DeleteMessage;
                                break;
                        }
                        if (sendMessage == "-") continue;

                        var guild = _client.GetGuild(item.GuildId);
                        if (guild == null)
                        {
                            Log.Warn($"Youtube 通知 - 找不到伺服器 {item.GuildId}");
                            db.NoticeYoutubeStreamChannel.RemoveRange(db.NoticeYoutubeStreamChannel.Where((x) => x.GuildId == item.GuildId));
                            db.SaveChanges();
                            continue;
                        }

                        var channel = guild.GetTextChannel(item.DiscordChannelId);
                        if (channel == null) continue;

                        var message = await channel.SendMessageAsync(sendMessage, false, embedBuilder.Build(), components: comp, options: new RequestOptions() { RetryMode = RetryMode.AlwaysRetry });

                        try
                        {
                            if (channel is INewsChannel)
                                await message.CrosspostAsync();
                        }
                        catch (Discord.Net.HttpException httpEx) when (httpEx.DiscordCode == DiscordErrorCode.MessageAlreadyCrossposted)
                        {
                            // ignore
                        }
                    }
                    catch (Discord.Net.HttpException httpEx)
                    {
                        if (httpEx.DiscordCode.HasValue && (httpEx.DiscordCode.Value == DiscordErrorCode.InsufficientPermissions || httpEx.DiscordCode.Value == DiscordErrorCode.MissingPermissions))
                        {
                            Log.Warn($"Youtube 通知 - 遺失權限 {item.GuildId} / {item.DiscordChannelId}");
                            db.NoticeYoutubeStreamChannel.RemoveRange(db.NoticeYoutubeStreamChannel.Where((x) => x.DiscordChannelId == item.DiscordChannelId));
                            db.SaveChanges();
                        }
                        else if (httpEx.HttpCode == System.Net.HttpStatusCode.InternalServerError || httpEx.HttpCode == System.Net.HttpStatusCode.BadGateway || httpEx.HttpCode == System.Net.HttpStatusCode.GatewayTimeout)
                        {
                            Log.Warn("Youtube 通知 - Discord 500錯誤");
                        }
                        else
                        {
                            Log.Error(httpEx, $"Youtube 通知 - Discord 未知錯誤 {item.GuildId} / {item.DiscordChannelId}");
                        }
                    }
                    catch (TimeoutException)
                    {
                        Log.Warn($"Youtube 通知 - Timeout {item.GuildId} / {item.DiscordChannelId}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Youtube 通知 - 未知錯誤 {item.GuildId} / {item.DiscordChannelId}");
                    }
                }
            }
        }

        public async Task<Google.Apis.YouTube.v3.Data.Video> GetVideoAsync(string videoId)
        {
            var pBreaker = Policy<Google.Apis.YouTube.v3.Data.Video>
                .Handle<Exception>()
                .WaitAndRetryAsync(new TimeSpan[]
                {
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(4)
                });

            return await pBreaker.ExecuteAsync(async () =>
             {
                 var video = yt.Videos.List("snippet,liveStreamingDetails");
                 video.Id = videoId;
                 var videoResult = await video.ExecuteAsync().ConfigureAwait(false);
                 if (videoResult.Items.Count == 0) return null;
                 return videoResult.Items[0];
             });
        }

        private async Task<IEnumerable<Google.Apis.YouTube.v3.Data.Video>> GetVideosAsync(IEnumerable<string> videoIds, int retryCount = 0)
        {
            var pBreaker = Policy<IEnumerable<Google.Apis.YouTube.v3.Data.Video>>
                .Handle<Exception>()
                .WaitAndRetryAsync(new TimeSpan[]
                {
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(4)
                });

            return await pBreaker.ExecuteAsync(async () =>
            {
                var video = yt.Videos.List("snippet,liveStreamingDetails");
                video.Id = string.Join(',', videoIds);
                var videoResult = await video.ExecuteAsync().ConfigureAwait(false);
                if (videoResult.Items.Count == 0) return null;
                return videoResult.Items;
            });
        }
    }
}
