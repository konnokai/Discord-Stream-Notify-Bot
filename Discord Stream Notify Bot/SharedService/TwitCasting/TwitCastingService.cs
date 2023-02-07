﻿using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.HttpClients;
using Discord_Stream_Notify_Bot.Interaction;
using System.Threading;

namespace Discord_Stream_Notify_Bot.SharedService.Twitcasting
{
    public class TwitcastingService : IInteractionService
    {
        private readonly HashSet<int> hashSet = new HashSet<int>();
        private readonly DiscordSocketClient _client;
        private readonly TwitcastingClient _twitcastingClient;

        private bool isRuning = false;

        public TwitcastingService(DiscordSocketClient client, TwitcastingClient twitcastingClient)
        {
            _client = client;
            _twitcastingClient = twitcastingClient;
            var _ = new Timer(async (obj) => { await TimerHandel(obj); },
                null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public async Task<(string ChannelId, string ChannelTitle)> GetChannelIdAndTitleAsync(string channelUrl)
        {
            string channelId = channelUrl.Split('?')[0].Replace("https://twitcasting.tv/", "");
            if (string.IsNullOrEmpty(channelId))            
                return (string.Empty, string.Empty);

            string channelTitle = await GetChannelTitleAsync(channelId).ConfigureAwait(false);
            if (string.IsNullOrEmpty(channelTitle))            
                return (string.Empty, string.Empty);
            
            return (channelId, channelTitle);
        }

        public async Task<string> GetChannelTitleAsync(string channelId)
        {
            try
            {
                HtmlAgilityPack.HtmlWeb htmlWeb = new HtmlAgilityPack.HtmlWeb();
                var htmlDocument = await htmlWeb.LoadFromWebAsync($"https://twitcasting.tv/{channelId}");
                var htmlNodes = htmlDocument.DocumentNode.Descendants();
                var htmlNode = htmlNodes.SingleOrDefault((x) => x.Name == "span" && x.HasClass("tw-user-nav-name"));

                if (htmlNode != null)
                {
                    return htmlNode.InnerText.Trim();
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Error($"TwitcastingService-GetChannelNameAsync: {ex}");
                return null;
            }
        }

        private async Task TimerHandel(object stats)
        {
            if (isRuning) return; isRuning = true;

            using var twitcastingDb = TwitcastingStreamContext.GetDbContext();
            using (var db = DBContext.GetDbContext())
            {
                try
                {
                    foreach (var item in db.TwitcastingSpider.Distinct((x) => x.ChannelId))
                    {
                        var data = await _twitcastingClient.GetNewStreamDataAsync(item.ChannelId);
                        if (data == null || !data.Movie.Live)
                            continue;

                        if (hashSet.Contains(data.Movie.Id))
                            continue;

                        if (twitcastingDb.TwitcastingStreams.Any((x) => x.StreamId == data.Movie.Id))
                        {
                            hashSet.Add(data.Movie.Id);
                            continue;
                        }

                        try
                        {
                            hashSet.Add(data.Movie.Id);

                            var streamToken = await _twitcastingClient.GetHappyTokenAsync(data.Movie.Id);
                            if (string.IsNullOrEmpty(streamToken))
                            {
                                Log.Error($"TwitcastingService-GetHappyTokenError: {item.ChannelId} / {data.Movie.Id}");
                                continue;
                            }

                            var streamData = await _twitcastingClient.GetStreamStatusDataAsync(data.Movie.Id, streamToken);
                            if (streamData == null)
                            {
                                Log.Error($"TwitcastingService-GetStreamStatusDataAsync: {item.ChannelId} / {data.Movie.Id}");
                                continue;
                            }

                            var startAt = await _twitcastingClient.GetStreamStartAtAsync(data.Movie.Id, streamToken);
                            var twitcastingStream = new TwitcastingStream()
                            {
                                ChannelId = item.ChannelId,
                                ChannelTitle = item.ChannelTitle,
                                StreamId = data.Movie.Id,
                                StreamTitle = streamData.Movie.Title,
                                StreamSubTitle = streamData.Movie.Telop,
                                Category = streamData.Movie.Category.Name,
                                StreamStartAt = startAt
                            };

                            twitcastingDb.TwitcastingStreams.Add(twitcastingStream);

                            if (item.IsRecord)
                            {
                                try
                                {
                                    string url;
                                    if (!string.IsNullOrEmpty(data.Llfmp4.Streams.Main)) url = data.Llfmp4.Streams.Main;
                                    else if (!string.IsNullOrEmpty(data.Llfmp4.Streams.Mobilesource)) url = data.Llfmp4.Streams.Mobilesource;
                                    else url = data.Llfmp4.Streams.Base;

                                    RecordTwitcasting(twitcastingStream, url);
                                    await SendStreamMessageAsync(twitcastingStream, true);
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"TwitcastingService-Record {item.ChannelId} - {data.Movie.Id}: {ex}");
                                    await SendStreamMessageAsync(twitcastingStream, false);
                                }
                            }
                            else await SendStreamMessageAsync(twitcastingStream, false);
                        }
                        catch (Exception ex) { Log.Error($"TwitcastingService-GetData {item.ChannelId}: {ex}"); }
                    }
                }
                catch (Exception ex) { Log.Error($"TwitcastingService-Timer {ex}"); }
                finally { isRuning = false; }

                await Task.Delay(1000); // 等個一秒鐘避免觸發429之類的錯誤，雖然也不知道有沒有用
            }
            twitcastingDb.SaveChanges();
        }

        private async Task SendStreamMessageAsync(TwitcastingStream twitcastingStream, bool isRecord = false)
        {
#if DEBUG
            Log.New($"Twitcasting開台通知: {twitcastingSpider.ChannelTitle} - {twitcastingStream.StreamTitle}");
#else
            using (var db = DBContext.GetDbContext())
            {
                var noticeGuildList = db.NoticeTwitcastingStreamChannels.Where((x) => x.ChannelId == twitcastingStream.ChannelId).ToList();
                Log.New($"發送Twitcasting開台通知 ({noticeGuildList.Count}): {twitcastingStream.ChannelTitle} - {twitcastingStream.StreamTitle}");

                EmbedBuilder embedBuilder = new EmbedBuilder()
                    .WithTitle(twitcastingStream.StreamTitle)
                    .WithDescription(Format.Url($"{twitcastingStream.ChannelTitle}", $"https://twitcasting.com/{twitcastingStream.ChannelId}"))
                    .WithUrl($"https://twitcasting.com/{twitcastingStream.ChannelId}/movie/{twitcastingStream.StreamId}");

                if (!string.IsNullOrEmpty(twitcastingStream.StreamSubTitle)) embedBuilder.AddField("副標題", twitcastingStream.StreamSubTitle, true);
                if (!string.IsNullOrEmpty(twitcastingStream.Category)) embedBuilder.AddField("分類", twitcastingStream.Category, true);
                embedBuilder.AddField("開始時間", twitcastingStream.StreamStartAt.ConvertDateTimeToDiscordMarkdown());

                if (isRecord) embedBuilder.WithRecordColor();
                else embedBuilder.WithOkColor();

                string description = embedBuilder.Description;
                embedBuilder.WithDescription(description + $"\n\n您可以透過 {Format.Url("Patreon", Utility.PatreonUrl)} 或 {Format.Url("Paypal", Utility.PaypalUrl)} 來贊助直播小幫手");

                foreach (var item in noticeGuildList)
                {
                    try
                    {
                        var guild = _client.GetGuild(item.GuildId);
                        if (guild == null) continue;
                        var channel = guild.GetTextChannel(item.DiscordChannelId);
                        if (channel == null) continue;

                        await channel.SendMessageAsync(item.StartStreamMessage, false, embedBuilder.Build());
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Notice Twitcasting {item.GuildId} / {item.DiscordChannelId}\n{ex.Message}");
                        if (ex.Message.Contains("50013") || ex.Message.Contains("50001")) db.NoticeTwitcastingStreamChannels.RemoveRange(db.NoticeTwitcastingStreamChannels.Where((x) => x.DiscordChannelId == item.DiscordChannelId));
                        db.SaveChanges();
                    }
                }
            }
#endif
        }

        private void RecordTwitcasting(TwitcastingStream twitcastingStream, string webSocketUrl)
        {
            Log.Info($"{twitcastingStream.ChannelTitle} ({twitcastingStream.StreamTitle}): {webSocketUrl}");
            // Todo: 實作錄影

            //try
            //{
            //    if (!System.IO.Directory.Exists(twitterSpaceRecordPath)) System.IO.Directory.CreateDirectory(twitterSpaceRecordPath);
            //}
            //catch (Exception ex)
            //{
            //    Log.Error($"推特語音保存路徑不存在且不可建立: {twitterSpaceRecordPath}");
            //    Log.Error($"更改保存路徑至Data資料夾: {Program.GetDataFilePath("")}");
            //    Log.Error(ex.ToString());

            //    twitterSpaceRecordPath = Program.GetDataFilePath("");
            //}

            //string procArgs = $"ffmpeg -i \"{masterUrl}\" \"{twitterSpaceRecordPath}[{twitterSpace.UserScreenName}]{twitterSpace.SpaecActualStartTime:yyyyMMdd} - {twitterSpace.SpaecId}.m4a\"";
            //if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) Process.Start("tmux", $"new-window -d -n \"Twitter Space @{twitterSpace.UserScreenName}\" {procArgs}");
            //else Process.Start(new ProcessStartInfo()
            //{
            //    FileName = "ffmpeg",
            //    Arguments = procArgs.Replace("ffmpeg", ""),
            //    CreateNoWindow = false,
            //    UseShellExecute = true
            //});
        }
    }
}