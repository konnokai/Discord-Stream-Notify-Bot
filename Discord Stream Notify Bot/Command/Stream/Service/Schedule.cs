﻿using Discord;
using Discord_Stream_Notify_Bot.DataBase;
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

namespace Discord_Stream_Notify_Bot.Command.Stream.Service
{
    public partial class StreamService
    {
        private static Dictionary<StreamVideo, ChannelType> addNewStreamVideo = new();

        private async Task HoloScheduleAsync()
        {
            if (Program.CheckSpiderList.HasFlag(Program.ChannelSpider.Holo)) return;
            Program.CheckSpiderList |= Program.ChannelSpider.Holo;

            try
            {
                HtmlWeb htmlWeb = new HtmlWeb();
                HtmlDocument htmlDocument = htmlWeb.Load("https://schedule.hololive.tv/simple");
                var aList = htmlDocument.DocumentNode.Descendants().Where((x) => x.Name == "a");
                List<string> idList = new List<string>();

                using (var uow = new DBContext())
                {
                    foreach (var item in aList)
                    {
                        string url = item.Attributes["href"].Value;
                        if (url.StartsWith("https://www.youtube.com/watch"))
                        {
                            string videoId = url.Split("?v=")[1].Trim();
                            if (!uow.HasStreamVideoByVideoId(videoId)) idList.Add(videoId);
                        }
                    }

                    if (idList.Count > 0)
                    {
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

                                    Log.NewStream($"{streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                    EmbedBuilder embedBuilder = new EmbedBuilder();
                                    embedBuilder.WithOkColor()
                                    .WithTitle(streamVideo.VideoTitle)
                                    .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                    .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                    .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                    .AddField("所屬", streamVideo.GetProductionType().GetProductionName())
                                    .AddField("上傳時間", item.Snippet.PublishedAt.Value, true);

                                   if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
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

                                    Log.NewStream($"{streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

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
                                        //.AddField("是否記錄直播", (CanRecord(uow, streamVideo) ? "是" : "否"), true);

                                        if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
                                        {
                                            await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.NewStream).ConfigureAwait(false);
                                            StartReminder(streamVideo, ChannelType.Holo);
                                        }
                                    }
                                    else if (startTime < DateTime.Now && item.Snippet.LiveBroadcastContent == "live")
                                    {
                                        if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
                                            StartReminder(streamVideo, ChannelType.Holo);
                                    }
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

                                    Log.NewStream($"{streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                    if (item.Snippet.LiveBroadcastContent == "live" && addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType)) 
                                        StartReminder(streamVideo, ChannelType.Holo);
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
            
            if (Program.CheckSpiderList.HasFlag(Program.ChannelSpider.Holo))
                Program.CheckSpiderList ^= Program.ChannelSpider.Holo;
            Log.Info("Holo影片清單整理完成");
        }

        private async Task NijisanjiScheduleAsync()
        {
            if (Program.CheckSpiderList.HasFlag(Program.ChannelSpider.Nijisanji)) return;
            Program.CheckSpiderList |= Program.ChannelSpider.Nijisanji;

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
                        return;
                    }

                    List<string> idList = new List<string>();
                    using (var uow = new DBContext())
                    {
                        foreach (var item in nijisanjiJson.data.events)
                        {
                            string videoId = item.url.Split("?v=")[1].Trim();
                            if (!uow.HasStreamVideoByVideoId(videoId)) idList.Add(videoId);
                        }

                        if (idList.Count > 0)
                        {
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

                                        Log.NewStream($"{streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                        EmbedBuilder embedBuilder = new EmbedBuilder();
                                        embedBuilder.WithOkColor()
                                        .WithTitle(streamVideo.VideoTitle)
                                        .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                        .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                        .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                        .AddField("所屬", streamVideo.GetProductionType().GetProductionName())
                                        .AddField("上傳時間", item.Snippet.PublishedAt.Value, true);

                                        if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
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

                                        Log.NewStream($"{streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

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
                                            //.AddField("是否記錄直播", (CanRecord(uow, streamVideo) ? "是" : "否"), true);

                                            if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
                                            {
                                                await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.NewStream).ConfigureAwait(false);
                                                StartReminder(streamVideo, ChannelType.Nijisanji);
                                            }
                                        }
                                        else if (startTime < DateTime.Now && item.Snippet.LiveBroadcastContent == "live")
                                        {
                                            if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
                                                StartReminder(streamVideo, ChannelType.Nijisanji);
                                        }
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

                                        Log.NewStream($"{streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                        if (item.Snippet.LiveBroadcastContent == "live" && addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
                                            StartReminder(streamVideo, ChannelType.Nijisanji);
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

            if (Program.CheckSpiderList.HasFlag(Program.ChannelSpider.Nijisanji))
                Program.CheckSpiderList ^= Program.ChannelSpider.Nijisanji;
            Log.Info("彩虹社影片清單整理完成");
        }

        private async Task OtherScheduleAsync()
        {
            if (Program.CheckSpiderList.HasFlag(Program.ChannelSpider.Other)) return;
            Program.CheckSpiderList |= Program.ChannelSpider.Other;

            Dictionary<string, List<string>> otherVideoDic = new Dictionary<string, List<string>>();

            using (var db = new DBContext())
            {
                foreach (var item in db.ChannelSpider)
                {
                    try
                    {
                        if (item.ChannelTitle == null)
                        {
                            var ytChannel = yt.Channels.List("snippet");
                            ytChannel.Id = item.ChannelId;
                            item.ChannelTitle = (await ytChannel.ExecuteAsync().ConfigureAwait(false)).Items[0].Snippet.Title;
                            db.ChannelSpider.Update(item);
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
                                        if (!db.OtherStreamVideo.Any((x) => x.VideoId == videoId)) addVideoIdList.Add(videoId);
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

                                            Log.NewStream($"{streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                            EmbedBuilder embedBuilder = new EmbedBuilder();
                                            embedBuilder.WithOkColor()
                                            .WithTitle(streamVideo.VideoTitle)
                                            .WithDescription(Format.Url(streamVideo.ChannelTitle, $"https://www.youtube.com/channel/{streamVideo.ChannelId}"))
                                            .WithImageUrl($"https://i.ytimg.com/vi/{streamVideo.VideoId}/maxresdefault.jpg")
                                            .WithUrl($"https://www.youtube.com/watch?v={streamVideo.VideoId}")
                                            .AddField("所屬", streamVideo.GetProductionType().GetProductionName())
                                            .AddField("上傳時間", streamVideo.ScheduledStartTime, true);

                                            if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
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

                                            Log.NewStream($"{streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

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
                                                    await SendStreamMessageAsync(streamVideo, embedBuilder.Build(), NoticeType.NewStream).ConfigureAwait(false);
                                                    StartReminder(streamVideo, ChannelType.Other);
                                                }
                                            }
                                            else if (startTime < DateTime.Now && item2.Snippet.LiveBroadcastContent == "live")
                                            {
                                                if (addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
                                                    StartReminder(streamVideo, ChannelType.Other);
                                            }
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

                                            Log.NewStream($"{streamVideo.ChannelTitle} - {streamVideo.VideoTitle}");

                                            if (item2.Snippet.LiveBroadcastContent == "live" && addNewStreamVideo.TryAdd(streamVideo, streamVideo.ChannelType))
                                                StartReminder(streamVideo, ChannelType.Nijisanji);
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

            if (Program.CheckSpiderList.HasFlag(Program.ChannelSpider.Other))
                Program.CheckSpiderList ^= Program.ChannelSpider.Other;
            Log.Info("其他勢影片清單整理完成");
        }

        public static async Task SaveDateBase()
        {
            int saveNum = 0;

            using (var db = new DBContext())
            {
                try
                {
                    if (addNewStreamVideo.Any((x) => x.Value == ChannelType.Holo))
                    {
                        foreach (var item in addNewStreamVideo.Where((x) => x.Value == ChannelType.Holo))
                        {
                            if (!db.HoloStreamVideo.Any((x) => x.VideoId == item.Key.VideoId))
                            {
                                await db.HoloStreamVideo.AddAsync(item.Key.ConvertToHoloStreamVideo()); saveNum++;
                            }

                            addNewStreamVideo.Remove(item.Key);
                        }

                        Log.Info("Holo資料庫已儲存");
                    }

                    if (addNewStreamVideo.Any((x) => x.Value == ChannelType.Nijisanji))
                    {
                        foreach (var item in addNewStreamVideo.Where((x) => x.Value == ChannelType.Nijisanji))
                        {
                            if (!db.NijisanjiStreamVideo.Any((x) => x.VideoId == item.Key.VideoId))
                            {
                                await db.NijisanjiStreamVideo.AddAsync(item.Key.ConvertToNijisanjiStreamVideo()); saveNum++;
                            }

                            addNewStreamVideo.Remove(item.Key);
                        }

                        Log.Info("2434資料庫已儲存");
                    }

                    if (addNewStreamVideo.Any((x) => x.Value == ChannelType.Other))
                    {
                        foreach (var item in addNewStreamVideo.Where((x) => x.Value == ChannelType.Other))
                        {
                            if (!db.OtherStreamVideo.Any((x) => x.VideoId == item.Key.VideoId))
                            {
                                await db.OtherStreamVideo.AddAsync(item.Key.ConvertToOtherStreamVideo()); saveNum++;
                            }

                            addNewStreamVideo.Remove(item.Key);
                        }

                        Log.Info("Other資料庫已儲存");
                    }

                    if (saveNum != 0) Log.Info($"資料庫已儲存完畢: {await db.SaveChangesAsync()}筆");
                }

                catch (Exception ex)
                {
                    Log.Error($"SaveDateBase {ex.Message}\r\n{ex.StackTrace}");
                    Log.Error($"{ex.InnerException.Message}\r\n{ex.InnerException.StackTrace}");
                }
            }
        }
    }
}