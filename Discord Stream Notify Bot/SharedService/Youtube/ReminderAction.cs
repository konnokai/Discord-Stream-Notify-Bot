using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction;
using Microsoft.EntityFrameworkCore;
using Polly;

namespace Discord_Stream_Notify_Bot.SharedService.Youtube
{
    public partial class YoutubeStreamService
    {
        private void StartReminder(DataBase.Table.Video streamVideo, DataBase.Table.Video.YTChannelType channelType)
        {
            if (streamVideo.ScheduledStartTime > DateTime.Now.AddDays(14)) return;

            try
            {
                TimeSpan ts = streamVideo.ScheduledStartTime.AddMinutes(-1).Subtract(DateTime.Now);

                if (ts <= TimeSpan.Zero)
                {
                    Task.Run(async () => { await ReminderTimerActionAsync(streamVideo); });
                }
                else
                {
                    var remT = new Timer(async (obj) => { await ReminderTimerActionAsync(streamVideo); }, streamVideo, Math.Max(1000, (long)ts.TotalMilliseconds), Timeout.Infinite);

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

        private async Task ReminderTimerActionAsync(object rObj)
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

                        await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.Delete).ConfigureAwait(false);
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
                                        var data = videoDb.Video.FirstOrDefault((x) => x.VideoId == streamVideo.VideoId);
                                        if (data != null)
                                        {
                                            data.VideoTitle = streamVideo.VideoTitle;
                                            videoDb.UpdateAndSave(data);
                                        }
                                        else if (addNewStreamVideo.ContainsKey(streamVideo.VideoId))
                                        {
                                            addNewStreamVideo[streamVideo.VideoId] = streamVideo;
                                        }
                                        else
                                        {
                                            Log.Error($"({streamVideo.ChannelType}) 直播標題變更保存失敗，找不到資料: {streamVideo.VideoId}");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(ex, $"({streamVideo.ChannelType}) 直播標題變更保存失敗: {streamVideo.VideoId}");
                                    }
                                }
                                break;
                            case DataBase.Table.Video.YTChannelType.Nijisanji:
                                using (var videoDb = DataBase.NijisanjiVideoContext.GetDbContext())
                                {
                                    try
                                    {
                                        var data = videoDb.Video.FirstOrDefault((x) => x.VideoId == streamVideo.VideoId);
                                        if (data != null)
                                        {
                                            data.VideoTitle = streamVideo.VideoTitle;
                                            videoDb.UpdateAndSave(data);
                                        }
                                        else if (addNewStreamVideo.ContainsKey(streamVideo.VideoId))
                                        {
                                            addNewStreamVideo[streamVideo.VideoId] = streamVideo;
                                        }
                                        else
                                        {
                                            Log.Error($"({streamVideo.ChannelType}) 直播標題變更保存失敗，找不到資料: {streamVideo.VideoId}");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(ex, $"({streamVideo.ChannelType}) 直播標題變更保存失敗: {streamVideo.VideoId}");
                                    }
                                }
                                break;
                            case DataBase.Table.Video.YTChannelType.Other:
                                using (var videoDb = DataBase.OtherVideoContext.GetDbContext())
                                {
                                    try
                                    {
                                        var data = videoDb.Video.FirstOrDefault((x) => x.VideoId == streamVideo.VideoId);
                                        if (data != null)
                                        {
                                            data.VideoTitle = streamVideo.VideoTitle;
                                            videoDb.UpdateAndSave(data);
                                        }
                                        else if (addNewStreamVideo.ContainsKey(streamVideo.VideoId))
                                        {
                                            addNewStreamVideo[streamVideo.VideoId] = streamVideo;
                                        }
                                        else
                                        {
                                            Log.Error($"({streamVideo.ChannelType}) 直播標題變更保存失敗，找不到資料: {streamVideo.VideoId}");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(ex, $"({streamVideo.ChannelType}) 直播標題變更保存失敗: {streamVideo.VideoId}");
                                    }
                                }
                                break;
                        }
                    }

#if RELEASE
                    try
                    {
                        using (var db = DataBase.MainDbContext.GetDbContext())
                        {
                            if (CanRecord(db, streamVideo))
                            {
                                if (Program.Redis != null)
                                {
                                    if (await Program.RedisSub.PublishAsync(new RedisChannel("youtube.record", RedisChannel.PatternMode.Literal), streamVideo.VideoId) != 0)
                                    {
                                        Log.Info($"已發送 YouTube 錄影請求: {streamVideo.VideoId}");
                                        isRecord = true;
                                    }
                                    else
                                    {
                                        Log.Warn($"Redis Sub 頻道不存在，請開啟錄影工具: {streamVideo.VideoId}");
                                    }
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

                        await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.Start).ConfigureAwait(false);
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
                                var data = videoDb.Video.FirstOrDefault((x) => x.VideoId == streamVideo.VideoId);
                                if (data != null)
                                {
                                    data.ScheduledStartTime = streamVideo.ScheduledStartTime;
                                    videoDb.UpdateAndSave(data);
                                }
                                else if (addNewStreamVideo.ContainsKey(streamVideo.VideoId))
                                {
                                    addNewStreamVideo[streamVideo.VideoId] = streamVideo;
                                }
                                else
                                {
                                    Log.Error($"({streamVideo.ChannelType}) 直播時間變更保存失敗，找不到資料: {streamVideo.VideoId}");
                                }
                            }
                            break;
                        case DataBase.Table.Video.YTChannelType.Nijisanji:
                            using (var videoDb = DataBase.NijisanjiVideoContext.GetDbContext())
                            {
                                var data = videoDb.Video.FirstOrDefault((x) => x.VideoId == streamVideo.VideoId);
                                if (data != null)
                                {
                                    data.ScheduledStartTime = streamVideo.ScheduledStartTime;
                                    videoDb.UpdateAndSave(data);
                                }
                                else if (addNewStreamVideo.ContainsKey(streamVideo.VideoId))
                                {
                                    addNewStreamVideo[streamVideo.VideoId] = streamVideo;
                                }
                                else
                                {
                                    Log.Error($"({streamVideo.ChannelType}) 直播時間變更保存失敗，找不到資料: {streamVideo.VideoId}");
                                }
                            }
                            break;
                        case DataBase.Table.Video.YTChannelType.Other:
                            using (var videoDb = DataBase.OtherVideoContext.GetDbContext())
                            {
                                try
                                {
                                    // 查看紀錄後發現，會遇到 Pub/Sub 先發送新影片通知且排程時間非常接近接收到通知的時間
                                    // 小幫手進入 ReminderTimerActionAsync 後檢查發現排程時間被更改了，所以要 Call Db 來更新資料
                                    // 但因為這過程太短，Db 還沒保存就觸發更新機制，導致 Db 回傳找不到資料
                                    // 通常這情況下 addNewStreamVideo 的清單內還會有這筆 VideoId 資料，故直接變更該筆資料
                                    var data = videoDb.Video.FirstOrDefault((x) => x.VideoId == streamVideo.VideoId);
                                    if (data != null)
                                    {
                                        data.ScheduledStartTime = streamVideo.ScheduledStartTime;
                                        videoDb.UpdateAndSave(data);
                                    }
                                    else if (addNewStreamVideo.ContainsKey(streamVideo.VideoId))
                                    {
                                        addNewStreamVideo[streamVideo.VideoId] = streamVideo;
                                    }
                                    else
                                    {
                                        Log.Error($"({streamVideo.ChannelType}) 直播時間變更保存失敗，找不到資料: {streamVideo.VideoId}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, $"({streamVideo.ChannelType}) 直播時間變更保存失敗: {streamVideo.VideoId}");
                                }
                            }
                            break;
                    }

                    await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.ChangeTime).ConfigureAwait(false);

                    if (Reminders.TryRemove(streamVideo.VideoId, out var t))
                        t.Timer.Change(Timeout.Infinite, Timeout.Infinite);

                    StartReminder(streamVideo, streamVideo.ChannelType);
                }
            }
            catch (Exception ex) { Log.Error(ex, $"ReminderAction: {streamVideo.VideoId}"); }
        }

        private async Task SendStreamMessageAsync(string videolId, EmbedBuilder embedBuilder, NoticeType noticeType)
        {
            using (var db = DataBase.MainDbContext.GetDbContext())
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

                await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), noticeType).ConfigureAwait(false);
            }
        }

        private async Task SendStreamMessageAsync(DataBase.Table.Video streamVideo, Embed embed, NoticeType noticeType)
        {
            if (!Program.IsConnect)
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

            List<NoticeYoutubeStreamChannel> noticeYoutubeStreamChannels = new List<NoticeYoutubeStreamChannel>();
            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                try
                {
                    // 有設定該頻道的通知就不用過濾，他們肯定是要這頻道的通知
                    noticeYoutubeStreamChannels.AddRange(db.NoticeYoutubeStreamChannel.AsNoTracking().Where((x) => x.YouTubeChannelId == streamVideo.ChannelId));
                }
                catch (Exception ex)
                {
                    // 原則上不會有錯，我也不知道加這幹嘛
                    Log.Error(ex, $"SendStreamMessageAsyncChannel: {streamVideo.VideoId}");
                }

                //類型檢查，其他類型的頻道要特別檢查，確保必須是認可的頻道才可被添加到其他類型通知
                try
                {
                    if (type != "other" || //如果不是其他類的頻道，直接添加到對應的類型通知即可
                        !db.YoutubeChannelSpider.AsNoTracking().Any((x) => x.ChannelId == streamVideo.ChannelId) || //若該頻道非在爬蟲清單內，那也沒有認不認可的問題
                        db.YoutubeChannelSpider.AsNoTracking().First((x) => x.ChannelId == streamVideo.ChannelId).IsTrustedChannel) //最後該爬蟲必須是已認可的頻道，才可添加至其他類型的通知
                    {
                        noticeYoutubeStreamChannels.AddRange(db.NoticeYoutubeStreamChannel.AsNoTracking().Where((x) => x.YouTubeChannelId == type));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"SendStreamMessageAsyncOtherChannel: {streamVideo.VideoId}");
                }

                Log.New($"發送 YouTube 通知 ({noticeYoutubeStreamChannels.Count} / {noticeType}): {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

#if DEBUG || DEBUG_DONTREGISTERCOMMAND
                return;
#endif

                Image? coverImage = null;
                if (noticeType == NoticeType.NewStream && noticeYoutubeStreamChannels.Any((x) => x.IsCreateEventForNewStream))
                {
                    Log.Info($"YouTube 通知 ({streamVideo.VideoId}) | 嘗試下載封面: {embed.Image.Value.Url}");
                    try
                    {
                        var stream = await Policy.Handle<TimeoutException>()
                            .Or<Discord.Net.HttpException>((httpEx) => ((int)httpEx.HttpCode).ToString().StartsWith("50"))
                            .WaitAndRetryAsync(3, (retryAttempt) =>
                            {
                                var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                                Log.Warn($"YouTube 通知 ({streamVideo.VideoId}) | 封面下載失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                                return timeSpan;
                            })
                            .ExecuteAsync(async () =>
                            {
                                HttpClient httpClient = new HttpClient();
                                return await httpClient.GetStreamAsync(embed.Image.Value.Url);
                            });

                        coverImage = new Image(stream);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"YouTube 通知 ({streamVideo.VideoId}) | 封面下載失敗，可能是找不到圖檔");
                    }
                }

                foreach (var item in noticeYoutubeStreamChannels)
                {
                    try
                    {
                        var guild = _client.GetGuild(item.GuildId);
                        if (guild == null)
                        {
                            Log.Warn($"YouTube 通知 ({streamVideo.VideoId}) | 找不到伺服器 {item.GuildId}");
                            db.NoticeYoutubeStreamChannel.RemoveRange(db.NoticeYoutubeStreamChannel.Where((x) => x.GuildId == item.GuildId));
                            db.SaveChanges();
                            continue;
                        }

                        // 只有新影片會發到影片通知頻道，首播類的影片歸類在直播類型
                        // 原則上 DiscordNoticeVideoChannelId 預設會跟 DiscordNoticeStreamChannelId 一樣，不該為空
                        var channel = guild.GetTextChannel(noticeType == NoticeType.NewVideo ? item.DiscordNoticeVideoChannelId : item.DiscordNoticeStreamChannelId);
                        if (channel == null) continue;

                        // 如果是新直播的話就建立活動，或更改活動開始時間
                        try
                        {
                            if (item.IsCreateEventForNewStream)
                            {
                                if (!guild.GetUser(_client.CurrentUser.Id).GuildPermissions.ManageEvents)
                                {
                                    Log.Warn($"YouTube 通知 ({streamVideo.VideoId}) | {item.GuildId} 無權限可建立活動，關閉此功能");
                                    item.IsCreateEventForNewStream = false;
                                    db.NoticeYoutubeStreamChannel.Update(item);
                                    db.SaveChanges();

                                    try
                                    {
                                        await Policy.Handle<TimeoutException>()
                                            .Or<Discord.Net.HttpException>((httpEx) => ((int)httpEx.HttpCode).ToString().StartsWith("50"))
                                            .WaitAndRetryAsync(3, (retryAttempt) =>
                                            {
                                                var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                                                Log.Warn($"YouTube 通知 ({streamVideo.VideoId}) | {item.GuildId} / {channel.Id} 無權限提示發送失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                                                return timeSpan;
                                            })
                                            .ExecuteAsync(async () =>
                                            {
                                                await channel.SendMessageAsync(embed: new EmbedBuilder().WithErrorColor().WithDescription("我在伺服器沒有 `管理活動` 的權限\n" +
                                                    "請給予權限後再次執行 `/youtube toggle-create-event` 來開啟自動建立活動的功能").Build());
                                            });
                                    }
                                    catch (Exception) { }
                                }
                                else
                                {
                                    if (noticeType == NoticeType.NewStream)
                                    {
                                        Log.Info($"YouTube 通知 ({streamVideo.VideoId}) | {item.GuildId} 嘗試建立活動");
                                        await Policy.Handle<TimeoutException>()
                                            .Or<Discord.Net.HttpException>((httpEx) => ((int)httpEx.HttpCode).ToString().StartsWith("50"))
                                            .WaitAndRetryAsync(3, (retryAttempt) =>
                                            {
                                                var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                                                Log.Warn($"YouTube 通知 ({streamVideo.VideoId}) | {item.GuildId} 建立活動失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                                                return timeSpan;
                                            })
                                            .ExecuteAsync(async () =>
                                            {
                                                await guild.CreateEventAsync(streamVideo.VideoTitle,
                                                    streamVideo.ScheduledStartTime.ToUniversalTime(),
                                                    GuildScheduledEventType.External,
                                                    description: Format.Url(streamVideo.ChannelTitle, $"https://youtube.com/channel/{streamVideo.ChannelId}"),
                                                    endTime: streamVideo.ScheduledStartTime.ToUniversalTime().AddHours(1),
                                                    location: $"https://youtube.com/watch?v={streamVideo.VideoId}",
                                                    coverImage: coverImage);
                                            });
                                    }
                                    else if (noticeType == NoticeType.ChangeTime)
                                    {
                                        Log.Info($"YouTube 通知 ({streamVideo.VideoId}) | {item.GuildId} 嘗試更改活動開始時間");
                                        await Policy.Handle<TimeoutException>()
                                            .Or<Discord.Net.HttpException>((httpEx) => ((int)httpEx.HttpCode).ToString().StartsWith("50"))
                                            .WaitAndRetryAsync(3, (retryAttempt) =>
                                            {
                                                var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                                                Log.Warn($"YouTube 通知 ({streamVideo.VideoId}) | {item.GuildId} 更改活動時間失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                                                return timeSpan;
                                            })
                                            .ExecuteAsync(async () =>
                                            {
                                                var @event = (await guild.GetEventsAsync()).FirstOrDefault((x) => x.Creator.Id == _client.CurrentUser.Id && x.Location.EndsWith(streamVideo.VideoId));

                                                if (@event == null)
                                                {
                                                    Log.Warn($"YouTube 通知 ({streamVideo.VideoId}) | {item.GuildId} 更改活動時間失敗，找不到對應的活動");
                                                }
                                                else
                                                {
                                                    await @event.ModifyAsync((act) =>
                                                    {
                                                        act.Name = streamVideo.VideoTitle;
                                                        act.StartTime = (DateTimeOffset)streamVideo.ScheduledStartTime.ToUniversalTime();
                                                        act.EndTime = (DateTimeOffset)streamVideo.ScheduledStartTime.ToUniversalTime().AddHours(1);
                                                    });
                                                }
                                            });
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, $"YouTube 通知 ({streamVideo.VideoId}) | {item.GuildId} 建立活動失敗");
                        }

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

                        await Policy.Handle<TimeoutException>()
                            .Or<Discord.Net.HttpException>((httpEx) => ((int)httpEx.HttpCode).ToString().StartsWith("50"))
                            .WaitAndRetryAsync(3, (retryAttempt) =>
                            {
                                var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                                Log.Warn($"YouTube 通知 ({streamVideo.VideoId}) | {item.GuildId} / {channel.Id} 發送失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                                return timeSpan;
                            })
                            .ExecuteAsync(async () =>
                            {
                                var message = await channel.SendMessageAsync(text: sendMessage, embed: embed, components: noticeType == NoticeType.Start ? _messageComponent : null, options: new RequestOptions() { RetryMode = RetryMode.AlwaysRetry });

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
                            Log.Warn($"YouTube 通知 ({streamVideo.VideoId}) | {item.GuildId} / {item.DiscordNoticeVideoChannelId} 遺失權限");
                            db.NoticeYoutubeStreamChannel.RemoveRange(db.NoticeYoutubeStreamChannel.Where((x) => x.DiscordNoticeVideoChannelId == item.DiscordNoticeVideoChannelId));
                            db.SaveChanges();
                        }
                        else if (((int)httpEx.HttpCode).ToString().StartsWith("50"))
                        {
                            Log.Warn($"YouTube 通知 ({streamVideo.VideoId}) | {item.GuildId} / {item.DiscordNoticeVideoChannelId} Discord 50X 錯誤: {httpEx.HttpCode}");
                        }
                        else
                        {

                            Log.Error(httpEx, $"YouTube 通知 ({streamVideo.VideoId}) | {item.GuildId} / {item.DiscordNoticeVideoChannelId} Discord 未知錯誤");
                        }
                    }
                    catch (TimeoutException)
                    {
                        Log.Warn($"YouTube 通知 ({streamVideo.VideoId}) | {item.GuildId} / {item.DiscordNoticeVideoChannelId} Timeout");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"YouTube 通知 ({streamVideo.VideoId}) | {item.GuildId} / {item.DiscordNoticeVideoChannelId} 未知錯誤");
                    }
                }
            }
        }

        public async Task<Google.Apis.YouTube.v3.Data.Video> GetVideoAsync(string videoId)
        {
            var pBreaker = Policy<Google.Apis.YouTube.v3.Data.Video>
                .Handle<Exception>()
                .WaitAndRetryAsync(3, (retryAttempt) =>
                {
                    var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                    Log.Warn($"YouTube GetVideoAsync ({videoId}) 失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                    return timeSpan;
                });

            return await pBreaker.ExecuteAsync(async () =>
             {
                 var video = YouTubeService.Videos.List("snippet,liveStreamingDetails");
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
                .WaitAndRetryAsync(3, (retryAttempt) =>
                {
                    var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                    Log.Warn($"YouTube GetVideoAsync ({videoIds.Count()}) 失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                    return timeSpan;
                });

            return await pBreaker.ExecuteAsync(async () =>
            {
                var video = YouTubeService.Videos.List("snippet,liveStreamingDetails");
                video.Id = string.Join(',', videoIds);
                var videoResult = await video.ExecuteAsync().ConfigureAwait(false);
                if (videoResult.Items.Count == 0) return null;
                return videoResult.Items;
            });
        }
    }
}
