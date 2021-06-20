using Discord;
using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Google.Apis.YouTube.v3.Data;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Command.Stream.Service
{
    public partial class StreamService
    {
        private void StartReminder(StreamVideo streamVideo, ChannelType channelType)
        {
            if (streamVideo.ScheduledStartTime > DateTime.Now.AddDays(7)) return;

            try
            {
                TimeSpan ts = streamVideo.ScheduledStartTime.AddMinutes(-1).Subtract(DateTime.Now);

                var remT = new Timer(ReminderTimerAction, streamVideo, Math.Max(0, (long)ts.TotalMilliseconds), Timeout.Infinite);

                if (!Reminders.TryAdd(streamVideo, new ReminderItem() { StreamVideo = streamVideo, Timer = remT, ChannelType = channelType }))
                {
                    remT.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                Log.Error(streamVideo.VideoTitle + " - " + streamVideo.ScheduledStartTime);
                Log.Error(ex.Message);
                throw;
            }
        }

        private async void ReminderTimerAction(object rObj)
        {
            var streamVideo = (StreamVideo)rObj;

            try
            {
                var videoResult = await GetVideoAsync(streamVideo.VideoId);

                if (videoResult == null)
                {
                    Log.Info($"{streamVideo.VideoId} 待機所被刪了");

                    EmbedBuilder embedBuilder = new EmbedBuilder();
                    embedBuilder.WithErrorColor()
                    .WithTitle(streamVideo.VideoTitle)
                    .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                    .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                    .AddField("直播狀態", "已刪除直播", true)
                    .AddField("排定開台時間", streamVideo.ScheduledStartTime, true);

                    await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.Delete).ConfigureAwait(false);
                    return;
                }

                DateTime startTime;
                if (videoResult.LiveStreamingDetails.ScheduledStartTime.HasValue) startTime = videoResult.LiveStreamingDetails.ScheduledStartTime.Value;
                else startTime = videoResult.LiveStreamingDetails.ActualStartTime.Value;

                using (var uow = new DBContext())
                {
                    if (startTime.AddMinutes(-2) < DateTime.Now)
                    {
                        bool isRecord = false;
                        streamVideo.VideoTitle = videoResult.Snippet.Title;

                        if (uow.HasStreamVideoByVideoId(streamVideo.VideoId))
                        {
                            switch (streamVideo.ChannelType)
                            {
                                case ChannelType.Holo:
                                    try
                                    {
                                        var data = uow.HoloStreamVideo.First((x) => x.VideoId == streamVideo.VideoId);
                                        data.VideoTitle = streamVideo.VideoTitle;
                                        uow.HoloStreamVideo.Update(data);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(ex.Message + "\r\n" + ex.StackTrace);
                                        uow.HoloStreamVideo.Update(streamVideo.ConvertToHoloStreamVideo());
                                    }
                                    break;
                                case ChannelType.Nijisamji:
                                    try
                                    {
                                        var data1 = uow.NijisanjiStreamVideo.First((x) => x.VideoId == streamVideo.VideoId);
                                        data1.VideoTitle = streamVideo.VideoTitle;
                                        uow.NijisanjiStreamVideo.Update(data1);
                                    }
                                    catch (Exception ex)
                                    {

                                        Log.Error(ex.Message + "\r\n" + ex.StackTrace);
                                        uow.NijisanjiStreamVideo.Update(streamVideo.ConvertToNijisanjiStreamVideo());
                                    }
                                    break;
                                case ChannelType.Other:
                                    try
                                    {
                                        var data1 = uow.OtherStreamVideo.First((x) => x.VideoId == streamVideo.VideoId);
                                        data1.VideoTitle = streamVideo.VideoTitle;
                                        uow.OtherStreamVideo.Update(data1);
                                    }
                                    catch (Exception ex)
                                    {

                                        Log.Error(ex.Message + "\r\n" + ex.StackTrace);
                                        uow.OtherStreamVideo.Update(streamVideo.ConvertToOtherStreamVideo());
                                    }
                                    break;
                            }
                        }

#if RELEASE
                        try
                        {
                            if (CanRecord(uow, streamVideo))
                            {
                                if (noticeRecordChannel == null) noticeRecordChannel = _client.GetGuild(738734668882640938).GetTextChannel(805134765191462942);

                                if (Program.Redis != null)
                                {
                                    if (await Program.RedisSub.PublishAsync("youtube.record", streamVideo.VideoId) != 0)
                                    {
                                        Log.Info($"已發送錄影請求: {streamVideo.VideoId}");
                                        isRecord = true;

                                        await noticeRecordChannel.SendConfirmAsync($"{Format.Url(streamVideo.VideoTitle, $"https://www.youtube.com/watch?v={streamVideo.VideoId}")}\n" +
                                            $"{Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}")}\n\n" +
                                            $"{$"youtube_{streamVideo.ChannelId}_{streamVideo.ScheduledStartTime:yyyyMMdd_HHmmss}_{streamVideo.VideoId}.ts"}");
                                    }
                                    else Log.Warn($"Redis Sub頻道不存在，請開啟錄影工具: {streamVideo.VideoId}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"ReminderTimerAction-Record {ex.Message}\r\n{ex.StackTrace}");
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
                            .AddField("排定開台時間", streamVideo.ScheduledStartTime, true)
                            .AddField("直播狀態", "開台中", true)
                            .AddField("是否記錄直播", "否", true);

                            await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.Start).ConfigureAwait(false);
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
                        .AddField("排定開台時間", streamVideo.ScheduledStartTime, true)
                        .AddField("更改開台時間", startTime, true);

                        streamVideo.ScheduledStartTime = startTime;
                        switch (streamVideo.ChannelType)
                        {
                            case ChannelType.Holo:
                                try
                                {
                                    var data = uow.HoloStreamVideo.First((x) => x.VideoId == streamVideo.VideoId);
                                    data.ScheduledStartTime = streamVideo.ScheduledStartTime;
                                    uow.HoloStreamVideo.Update(data);
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex.Message + "\r\n" + ex.StackTrace);
                                    uow.HoloStreamVideo.Update(streamVideo.ConvertToHoloStreamVideo());
                                }
                                break;
                            case ChannelType.Nijisamji:
                                try
                                {
                                    var data1 = uow.NijisanjiStreamVideo.First((x) => x.VideoId == streamVideo.VideoId);
                                    data1.ScheduledStartTime = streamVideo.ScheduledStartTime;
                                    uow.NijisanjiStreamVideo.Update(data1);
                                }
                                catch (Exception ex)
                                {

                                    Log.Error(ex.Message + "\r\n" + ex.StackTrace);
                                    uow.NijisanjiStreamVideo.Update(streamVideo.ConvertToNijisanjiStreamVideo());
                                }
                                break;
                            case ChannelType.Other:
                                try
                                {
                                    var data1 = uow.OtherStreamVideo.First((x) => x.VideoId == streamVideo.VideoId);
                                    data1.ScheduledStartTime = streamVideo.ScheduledStartTime;
                                    uow.OtherStreamVideo.Update(data1);
                                }
                                catch (Exception ex)
                                {

                                    Log.Error(ex.Message + "\r\n" + ex.StackTrace);
                                    uow.OtherStreamVideo.Update(streamVideo.ConvertToOtherStreamVideo());
                                }
                                break;
                        }

                        await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.ChangeTime).ConfigureAwait(false);

                        if (Reminders.TryRemove(streamVideo, out var t))
                            t.Timer.Change(Timeout.Infinite, Timeout.Infinite);

                        StartReminder(streamVideo, streamVideo.ChannelType);
                    }

                    await uow.SaveChangesAsync();
                }
            }
            catch (Exception ex) { Log.Error($"ReminderAction {ex.Message} ({streamVideo.VideoId})\r\n{ex.StackTrace}"); }
        }

        private async Task SendStreamMessageAsync(string videolId, Embed embed, NoticeType noticeType)
        {
            using (var uow = new DBContext())
            {
                StreamVideo streamVideo = uow.GetStreamVideoByVideoId(videolId);

                if (streamVideo == null)
                {
                    try
                    {
                        var item = await GetVideoAsync(videolId).ConfigureAwait(false);

                        var addNewStreamVideo = new List<OtherStreamVideo>();
                        string redisStr;
                        if ((redisStr = await Program.RedisDb.StringGetAsync("streambot.save.schedule.other")) != "[]")
                            addNewStreamVideo = JsonConvert.DeserializeObject<List<OtherStreamVideo>>(redisStr);

                        streamVideo = new StreamVideo()
                        {
                            ChannelId = item.Snippet.ChannelId,
                            ChannelTitle = item.Snippet.ChannelTitle,
                            VideoId = item.Id,
                            VideoTitle = item.Snippet.Title,
                            ScheduledStartTime = item.LiveStreamingDetails.ActualStartTime.Value,
                            ChannelType = ChannelType.Other
                        };

                        addNewStreamVideo.Add(streamVideo.ConvertToOtherStreamVideo());
                        await Program.Redis.GetDatabase().StringSetAsync("streambot.save.schedule.other", JsonConvert.SerializeObject(addNewStreamVideo));
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex.Message + "\r\n" + ex.StackTrace);
                        return;
                    }
                }

                await SendStreamMessageAsync(streamVideo, embed, noticeType).ConfigureAwait(false);
            }
        }

        private async Task SendStreamMessageAsync(StreamVideo streamVideo, Embed embed, NoticeType noticeType)
        {
            string type = streamVideo.ChannelType == ChannelType.Holo ? "holo" : streamVideo.ChannelType == ChannelType.Nijisamji ? "2434" : "other";
            List<NoticeStreamChannel> noticeGuildList = new List<NoticeStreamChannel>();

            using (var db = new DBContext())
            {
                try
                {
                    db.NoticeStreamChannel.ToList().ForEach((item) =>
                    {
                        if (item.NoticeStreamChannelId == streamVideo.ChannelId)
                            noticeGuildList.Add(item);
                    });
                }
                catch (Exception ex)
                {
                    Log.Error($"SendStreamMessageAsyncChannel {streamVideo.VideoId} - {ex.Message}\r\n{ex.StackTrace}");
                }

                try
                {
                    if (type != "other" || 
                        !db.ChannelSpider.Any((x) => x.ChannelId == streamVideo.ChannelId) ||
                        !db.ChannelSpider.FirstOrDefault((x) => x.ChannelId == streamVideo.ChannelId).IsWarningChannel)
                    {
                        db.NoticeStreamChannel.ToList().ForEach((item) =>
                        {
                            if (item.NoticeStreamChannelId == "all" || item.NoticeStreamChannelId == type)
                                noticeGuildList.Add(item);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"SendStreamMessageAsyncOtherChannel {streamVideo.VideoId} - {ex.Message}\r\n{ex.StackTrace}");
                }

                foreach (var item in noticeGuildList)
                {
                    try
                    {
                        var guild = _client.GetGuild(item.GuildId);
                        if (guild == null) continue;
                        var channel = guild.GetTextChannel(item.ChannelId);
                        if (channel == null) continue;

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

                        await channel.SendMessageAsync(sendMessage, false, embed);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Notice {item.GuildId} / {item.ChannelId}\r\n{ex.Message}");
                        if (ex.Message.Contains("50013")) db.NoticeStreamChannel.Remove(db.NoticeStreamChannel.First((x) => x.ChannelId == item.ChannelId));
                        await db.SaveChangesAsync();
                    }
                }
            }
        }

        public async Task<Video> GetVideoAsync(string videoId)
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
                Log.Error($"GetVideoAsync {ex.Message}\r\n{ex.StackTrace}");
                return null;
            }
        }

        private async Task<IEnumerable<Video>> GetVideosAsync(IEnumerable<string> videoIds)
        {
            try
            {
                var video = yt.Videos.List("snippet,liveStreamingDetails");
                video.Id = string.Join(',', videoIds);
                var videoResult = await video.ExecuteAsync().ConfigureAwait(false);
                return videoResult.Items;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
