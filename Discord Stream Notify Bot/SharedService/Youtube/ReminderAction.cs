using Discord;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

                    if (!Reminders.TryAdd(streamVideo, new ReminderItem() { StreamVideo = streamVideo, Timer = remT, ChannelType = channelType }))
                    {
                        remT.Change(Timeout.Infinite, Timeout.Infinite);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"StartReminder: {streamVideo.VideoTitle} - {streamVideo.ScheduledStartTime}\n{ex}");
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
                        .AddField("直播狀態", "已刪除直播", true)
                        .AddField("排定開台時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown(), true);

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
                if (videoResult.LiveStreamingDetails.ScheduledStartTime.HasValue) startTime = videoResult.LiveStreamingDetails.ScheduledStartTime.Value;
                else startTime = videoResult.LiveStreamingDetails.ActualStartTime.Value;                

                using (var db = DataBase.DBContext.GetDbContext())
                {
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
                                            videoDb.Video.Update(streamVideo);
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
                                            videoDb.Video.Update(streamVideo);
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
                                            videoDb.Video.Update(streamVideo);
                                        }
                                    }
                                    break;
                            }
                        }

#if RELEASE
                        try
                        {
                            if (CanRecord(db, streamVideo))
                            {
                                try
                                {
                                    //Todo: 自定義化
                                    if (noticeRecordChannel == null) noticeRecordChannel = _client.GetGuild(738734668882640938).GetTextChannel(805134765191462942);
                                }
                                catch { }

                                if (Program.Redis != null)
                                {
                                    if (Utility.GetNowRecordStreamList().Contains(streamVideo.VideoId))
                                    {
                                        Log.Warn($"{streamVideo.VideoId} 已經在錄影了");
                                        return;
                                    }

                                    if (await Program.RedisSub.PublishAsync("youtube.record", streamVideo.VideoId) != 0)
                                    {
                                        Log.Info($"已發送錄影請求: {streamVideo.VideoId}");
                                        isRecord = true;

                                        if (noticeRecordChannel != null) await noticeRecordChannel.SendMessageAsync(embeds: new Embed[] { new EmbedBuilder().WithOkColor()
                                                .WithDescription($"{Format.Url(streamVideo.VideoTitle, $"https://www.youtube.com/watch?v={streamVideo.VideoId}")}\n" +
                                                $"{Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}")}\n\n" +
                                                $"youtube_{streamVideo.ChannelId}_{streamVideo.ScheduledStartTime:yyyyMMdd_HHmmss}_{streamVideo.VideoId}.ts").Build() });
                                    }
                                    else Log.Warn($"Redis Sub頻道不存在，請開啟錄影工具: {streamVideo.VideoId}");
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
                            .AddField("排定開台時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown(), true)
                            .AddField("直播狀態", "開台中", true);
                            //.AddField("是否記錄直播", "否", true);

                            await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.Start).ConfigureAwait(false);
                        }

                        if (Reminders.TryRemove(streamVideo, out var t))
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
                        .AddField("直播狀態", "尚未開台(已更改時間)", true)
                        .AddField("排定開台時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown(), true)
                        .AddField("更改開台時間", startTime.ConvertDateTimeToDiscordMarkdown(), true);

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
                                        videoDb.UpdateAndSave(streamVideo);
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
                                        videoDb.UpdateAndSave(streamVideo);
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
                                        videoDb.UpdateAndSave(streamVideo);
                                    }
                                }
                                break;
                        }

                        await SendStreamMessageAsync(streamVideo, embedBuilder, NoticeType.ChangeTime).ConfigureAwait(false);

                        if (Reminders.TryRemove(streamVideo, out var t))
                            t.Timer.Change(Timeout.Infinite, Timeout.Infinite);

                        StartReminder(streamVideo, streamVideo.ChannelType);
                    }

                    db.SaveChanges();
                }
            }
            catch (Exception ex) { Log.Error($"ReminderAction: {streamVideo.VideoId}\n{ex}"); }
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

                            var startTime = item.LiveStreamingDetails.ActualStartTime.Value;
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
                            Log.Error(ex.Message + "\n" + ex.StackTrace);
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

            string type = streamVideo.ChannelType == DataBase.Table.Video.YTChannelType.Holo ? "holo" : streamVideo.ChannelType == DataBase.Table.Video.YTChannelType.Nijisanji ? "2434" : "other";
            List<NoticeYoutubeStreamChannel> noticeGuildList = new List<NoticeYoutubeStreamChannel>();

            using (var db = DataBase.DBContext.GetDbContext())
            {
                try
                {
                    noticeGuildList.AddRange(db.NoticeYoutubeStreamChannel.Where((x) => x.NoticeStreamChannelId == streamVideo.ChannelId));
                }
                catch (Exception ex)
                {
                    Log.Error($"SendStreamMessageAsyncChannel: {streamVideo.VideoId}\n{ex}");
                }

                //類型檢查
                try
                {
                    if (type != "other" || //如果不是其他類的頻道
                        !db.YoutubeChannelSpider.Any((x) => x.ChannelId == streamVideo.ChannelId) || //或該頻道非在爬蟲清單內
                        db.YoutubeChannelSpider.First((x) => x.ChannelId == streamVideo.ChannelId).IsTrustedChannel) //該爬蟲是已認可的頻道
                    {
                        noticeGuildList.AddRange(db.NoticeYoutubeStreamChannel.Where((x) => x.NoticeStreamChannelId == "all" || x.NoticeStreamChannelId == type));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"SendStreamMessageAsyncOtherChannel: {streamVideo.VideoId}\n{ex}");
                }

                Log.Stream($"發送直播通知 ({noticeGuildList.Count} / {noticeType}): {streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                if (noticeType == NoticeType.Start)
                {
                    string description = embedBuilder.Description;
                    embedBuilder.WithDescription(description + $"\n\n您可以透過 {Format.Url("Patreon", Utility.PatreonUrl)} 或 {Format.Url("Paypal", Utility.PaypalUrl)} 來贊助直播小幫手");
                }

#if DEBUG
                return;
#endif

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

                        await pBreaker.Execute(() => channel.SendMessageAsync(sendMessage, false, embedBuilder.Build()));
                    }
                    catch (Discord.Net.HttpException httpEx)
                    {
                        if (httpEx.DiscordCode.HasValue && (httpEx.DiscordCode.Value == DiscordErrorCode.InsufficientPermissions || httpEx.DiscordCode.Value == DiscordErrorCode.MissingPermissions))
                        {
                            Log.Warn($"Youtube 通知 - 遺失權限 {item.GuildId} / {item.DiscordChannelId}");
                            db.NoticeYoutubeStreamChannel.RemoveRange(db.NoticeYoutubeStreamChannel.Where((x) => x.DiscordChannelId == item.DiscordChannelId));
                            db.SaveChanges();
                        }
                        else
                        {
                            Log.Error($"Youtube 通知 - Discord 未知錯誤 {item.GuildId} / {item.DiscordChannelId}");
                            Log.Error(httpEx.ToString());
                        }
                    }
                    catch (TimeoutException)
                    {
                        Log.Warn($"Youtube 通知 - Timeout {item.GuildId} / {item.DiscordChannelId}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Youtube 通知 - 未知錯誤 {item.GuildId} / {item.DiscordChannelId}");
                        Log.Error(ex.ToString());
                    }
                }
            }
        }

        public async Task<Google.Apis.YouTube.v3.Data.Video> GetVideoAsync(string videoId, int retryCount = 0)
        {
            try
            {
                var video = yt.Videos.List("snippet,liveStreamingDetails");
                video.Id = videoId;
                var videoResult = await video.ExecuteAsync().ConfigureAwait(false);
                if (videoResult.Items.Count == 0) return null;
                return videoResult.Items[0];
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Timeout") && retryCount <= 3)
                {
                    Log.Error($"ㄐㄐ，{videoId} Timeout了，嘗試重新執行第 {++retryCount} 次");
                    return await GetVideoAsync(videoId, retryCount);
                }

                throw;
            }
        }

        private async Task<IEnumerable<Google.Apis.YouTube.v3.Data.Video>> GetVideosAsync(IEnumerable<string> videoIds, int retryCount = 0)
        {
            try
            {
                var video = yt.Videos.List("snippet,liveStreamingDetails");
                video.Id = string.Join(',', videoIds);
                var videoResult = await video.ExecuteAsync().ConfigureAwait(false);
                return videoResult.Items;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Timeout"))
                {
                    Log.Error($"ㄐㄐ，Videos: `{string.Join(',', videoIds)}` Timeout了，嘗試重新執行第 {++retryCount} 次");
                    return await GetVideosAsync(videoIds, retryCount);
                }

                throw;
            }
        }
    }
}
