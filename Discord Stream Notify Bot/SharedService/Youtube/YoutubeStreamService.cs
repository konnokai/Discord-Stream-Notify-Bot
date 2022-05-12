using Discord;
using Discord.WebSocket;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using HtmlAgilityPack;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.SharedService.Youtube
{
    public partial class YoutubeStreamService : IInteractionService
    {
        public enum NoticeType
        {
            NewStream, NewVideo, Start, End, ChangeTime, Delete
        }

        public ConcurrentDictionary<StreamVideo, ReminderItem> Reminders { get; } = new ConcurrentDictionary<StreamVideo, ReminderItem>();
        public bool IsRecord { get; set; } = true;
        public YouTubeService yt;

        private Timer holoSchedule, nijisanjiSchedule, otherSchedule, checkScheduleTime, saveDateBase/*, checkHoloNowStream, holoScheduleEmoji*/;
        private SocketTextChannel noticeRecordChannel;
        private DiscordSocketClient _client;
        private readonly IHttpClientFactory _httpClientFactory;

        public YoutubeStreamService(DiscordSocketClient client, IHttpClientFactory httpClientFactory, BotConfig botConfig)
        {
            _client = client;
            _httpClientFactory = httpClientFactory;
            yt = new YouTubeService(new BaseClientService.Initializer
            {
                ApplicationName = "DiscordStreamBot",
                ApiKey = botConfig.GoogleApiKey,
            });

            if (Program.Redis != null)
            {
                Program.RedisSub.Subscribe("youtube.startstream", async (channel, json) =>
                {
                    StreamRecordJson streamRecordJson = JsonConvert.DeserializeObject<StreamRecordJson>(json.ToString());

                    try
                    {
                        Log.Info($"{channel} - {streamRecordJson.VideoId}");

                        var item = await GetVideoAsync(streamRecordJson.VideoId).ConfigureAwait(false);
                        if (item == null)
                        {
                            Log.Warn($"{streamRecordJson.VideoId} Delete");
                            await Program.RedisSub.PublishAsync("youtube.deletestream", streamRecordJson.VideoId);
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
                        .AddField("直播狀態", "開台中", true)
                        .AddField("開台時間", startTime.ConvertDateTimeToDiscordMarkdown());
                        //.AddField("是否記錄直播", "是", true)
                        //.AddField("存檔名稱", streamRecordJson.RecordFileName, false);

                        //if (streamRecordJson.IsReRecord)
                        //{
                        //    if (noticeRecordChannel == null) noticeRecordChannel = _client.GetGuild(744593681587241041).GetTextChannel(752815296452231238); //Todo: 要自訂義
                        //    await Program.ApplicatonOwner.SendMessageAsync(text: "重新錄影", embed: embedBuilder.Build()).ConfigureAwait(false);
                        //}
                        //else
                        //{
                            await SendStreamMessageAsync(item.Id, embedBuilder.Build(), NoticeType.Start).ConfigureAwait(false);
                            await ChangeGuildBannerAsync(item.Snippet.ChannelId, item.Id);
                        //}
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Record-StartStream {ex}");
                    }
                });

                Program.RedisSub.Subscribe("youtube.endstream", async (channel, json) =>
                {
                    try
                    {
                        StreamRecordJson streamRecordJson = JsonConvert.DeserializeObject<StreamRecordJson>(json.ToString());

                        Log.Info($"{channel} - {streamRecordJson.VideoId}");

                        var item = await GetVideoAsync(streamRecordJson.VideoId).ConfigureAwait(false);
                        if (item == null)
                        {
                            Log.Warn($"{streamRecordJson.VideoId} Delete");
                            await Program.RedisSub.PublishAsync("youtube.deletestream", streamRecordJson.VideoId);
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
                        .AddField("直播狀態", "已關台", true)
                        .AddField("直播時間", $"{endTime.Subtract(startTime):hh'時'mm'分'ss'秒'}", true)
                        .AddField("關台時間", endTime.ConvertDateTimeToDiscordMarkdown());
                        // .AddField("存檔名稱", streamRecordJson.RecordFileName, false);

                        await SendStreamMessageAsync(item.Id, embedBuilder.Build(), NoticeType.End).ConfigureAwait(false);
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
                            if (db.HasStreamVideoByVideoId(videoId))
                            {
                                var streamVideo = db.GetStreamVideoByVideoId(videoId);

                                EmbedBuilder embedBuilder = new EmbedBuilder();
                                embedBuilder.WithErrorColor()
                                .WithTitle(streamVideo.VideoTitle)
                                .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                .AddField("直播狀態", "已刪除直播", true)
                                .AddField("排定開台時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.Delete).ConfigureAwait(false);
                            }

                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Record-DeleteStream {ex}");
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
                            if (db.HasStreamVideoByVideoId(videoId))
                            {
                                var streamVideo = db.GetStreamVideoByVideoId(videoId);
                                EmbedBuilder embedBuilder = new EmbedBuilder();
                                embedBuilder.WithTitle(streamVideo.VideoTitle)
                                .WithOkColor()
                                .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                .AddField("直播狀態", "開台中", true)
                                .AddField("排定開台時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                if (Program.ApplicatonOwner != null) await Program.ApplicatonOwner.SendMessageAsync("429錯誤", false, embedBuilder.Build()).ConfigureAwait(false); 
                                await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.Start).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Record-429Error {ex}");
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

                //            await SendStreamMessageAsync(item.Id, embedBuilder.Build(), NoticeType.ChangeTime).ConfigureAwait(false);

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

                //            await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.New).ConfigureAwait(false);
                //        }
                //    }
                //});
                #endregion

                Log.Info("已建立Redis訂閱");
            }


            using (var db = DataBase.DBContext.GetDbContext())
            {
                foreach (var streamVideo in db.HoloStreamVideo.Where((x) => x.ScheduledStartTime > DateTime.Now))
                {
                    StartReminder(streamVideo, StreamVideo.YTChannelType.Holo);
                }
                foreach (var streamVideo in db.NijisanjiStreamVideo.Where((x) => x.ScheduledStartTime > DateTime.Now))
                {
                    StartReminder(streamVideo, StreamVideo.YTChannelType.Nijisanji);
                }
                foreach (var streamVideo in db.OtherStreamVideo.Where((x) => x.ScheduledStartTime > DateTime.Now))
                {
                    StartReminder(streamVideo, StreamVideo.YTChannelType.Other);
                }
            }

            holoSchedule = new Timer(async (objState) => await HoloScheduleAsync(), null, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(5));

            nijisanjiSchedule = new Timer(async (objState) => await NijisanjiScheduleAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5));

            otherSchedule = new Timer(async (onjState) => await OtherScheduleAsync(), null, TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(15));

            checkScheduleTime = new Timer(async (objState) =>
            {
                using (var db = DataBase.DBContext.GetDbContext())
                {
                    for (int i = 0; i < Reminders.Count; i += 50)
                    {
                        var video = yt.Videos.List("snippet,liveStreamingDetails");
                        video.Id = string.Join(",", Reminders.Skip(i).Take(50).Select((x) => x.Value.StreamVideo.VideoId));
                        var videoResult = await video.ExecuteAsync().ConfigureAwait(false);

                        foreach (var item in videoResult.Items)
                        {
                            var stream = Reminders.First((x) => x.Key.VideoId == item.Id).Key;
                            if (!item.LiveStreamingDetails.ScheduledStartTime.HasValue)
                            {
                                Reminders.TryRemove(stream, out var reminderItem);

                                EmbedBuilder embedBuilder = new EmbedBuilder();
                                embedBuilder.WithTitle(stream.VideoTitle)
                                .WithOkColor()
                                .WithDescription(Format.Url(stream.ChannelTitle, $"https://www.youtube.com/channel/{stream.ChannelId}"))
                                .WithImageUrl($"https://i.ytimg.com/vi/{stream.VideoId}/maxresdefault.jpg")
                                .WithUrl($"https://www.youtube.com/watch?v={stream.VideoId}")
                                .AddField("直播狀態", "無開始時間", true)
                                .AddField("開台時間", stream.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                if (Program.ApplicatonOwner != null) await Program.ApplicatonOwner.SendMessageAsync(null, false, embedBuilder.Build()).ConfigureAwait(false);
                                //await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.Start).ConfigureAwait(false);
                                continue;
                            }

                            if (stream.ScheduledStartTime != item.LiveStreamingDetails.ScheduledStartTime.Value)
                            {
                                try
                                {
                                    if (Reminders.TryRemove(stream, out var t))
                                    {
                                        t.Timer.Change(Timeout.Infinite, Timeout.Infinite);
                                        t.Timer.Dispose();
                                    }

                                    var startTime = item.LiveStreamingDetails.ScheduledStartTime.Value;
                                    var streamVideo = new StreamVideo()
                                    {
                                        ChannelId = item.Snippet.ChannelId,
                                        ChannelTitle = item.Snippet.ChannelTitle,
                                        VideoId = item.Id,
                                        VideoTitle = item.Snippet.Title,
                                        ScheduledStartTime = startTime,
                                        ChannelType = stream.ChannelType
                                    };

                                    switch (stream.ChannelType)
                                    {
                                        case StreamVideo.YTChannelType.Holo:
                                            db.HoloStreamVideo.Update(streamVideo.ConvertToHoloStreamVideo());
                                            break;
                                        case StreamVideo.YTChannelType.Nijisanji:
                                            db.NijisanjiStreamVideo.Update(streamVideo.ConvertToNijisanjiStreamVideo());
                                            break;
                                        case StreamVideo.YTChannelType.Other:
                                            db.OtherStreamVideo.Update(streamVideo.ConvertToOtherStreamVideo());
                                            break;
                                    }
                                    db.SaveChanges();

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
                                        .AddField("排定開台時間", stream.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown())
                                        .AddField("更改開台時間", streamVideo.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());

                                        await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.ChangeTime).ConfigureAwait(false);
                                        StartReminder(streamVideo, stream.ChannelType);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"CheckScheduleTime\n{ex}");
                                }
                            }
                        }
                    }
                }
            }, null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));

            saveDateBase = new Timer((onjState) => SaveDateBase(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3));
        }

        public async Task<Embed> GetNowStreamingChannel()
        {
            try
            {
                HtmlWeb htmlWeb = new HtmlWeb();
                HtmlDocument htmlDocument = htmlWeb.Load("https://schedule.hololive.tv/lives/all");
                List<string> idList = new List<string>(htmlDocument.DocumentNode.Descendants()
                    .Where((x) => x.Name == "a" &&
                        x.Attributes["href"].Value.StartsWith("https://www.youtube.com/watch") &&
                        x.Attributes["style"].Value.Contains("border: 3px"))
                    .Select((x) => x.Attributes["href"].Value.Split("?v=")[1]));

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

        private bool CanRecord(DataBase.DBContext db ,StreamVideo streamVideo) =>
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

            string channelId = "";

            Regex regex = new Regex(@"(http[s]{0,1}://){0,1}(www\.){0,1}(?'Host'[^/]+)/(?'Type'[^/]+)/(?'ChannelName'[\w%\-]+)");
            Match match = regex.Match(channelUrl);
            if (!match.Success)
                throw new UriFormatException("錯誤，請確認是否輸入YouTube頻道網址");

            if (match.Groups["Type"].Value == "channel")
            {
                channelId = match.Groups["ChannelName"].Value;
                if (!channelId.StartsWith("UC")) throw new UriFormatException("錯誤，頻道Id格式不正確");
                if (channelId.Length != 24) throw new UriFormatException("錯誤，頻道Id字元數不正確");
            }
            else if (match.Groups["Type"].Value == "c")
            {
                string channelName = System.Net.WebUtility.UrlDecode(match.Groups["ChannelName"].Value);

                if (await Program.RedisDb.KeyExistsAsync($"discord_stream_bot:ChannelNameToId:{channelName}"))
                {
                    channelId = await Program.RedisDb.StringGetAsync($"discord_stream_bot:ChannelNameToId:{channelName}");
                }
                else
                {
                    try
                    {
                        //https://stackoverflow.com/a/36559834
                        HtmlWeb htmlWeb = new HtmlWeb();
                        var htmlDocument = await htmlWeb.LoadFromWebAsync($"https://www.youtube.com/c/{channelName}");
                        var node = htmlDocument.DocumentNode.Descendants().FirstOrDefault((x) => x.Name == "meta" && x.Attributes.Any((x2) => x2.Name == "itemprop" && x2.Value == "channelId"));
                        if (node == null)
                            throw new UriFormatException("錯誤，請確認是否輸入正確的YouTube頻道網址\n" +
                                "或確認該頻道是否存在");

                        channelId = node.Attributes.FirstOrDefault((x) => x.Name == "content").Value;
                        if (string.IsNullOrEmpty(channelId))
                            throw new UriFormatException("錯誤，請確認是否輸入正確的YouTube頻道網址\n" +
                                "或確認該頻道是否存在");

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

            return channelId;
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
        //                        "Bot無 `管理頻道` 權限，無法變更頻道名稱\n" +
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

    class StreamRecordJson
    {
        public string VideoId { get; set; }
        public string RecordFileName { get; set; }
    }

    public class ReminderItem
    {
        public StreamVideo StreamVideo { get; set; }
        public Timer Timer { get; set; }
        public StreamVideo.YTChannelType ChannelType { get; set; }
    }
}