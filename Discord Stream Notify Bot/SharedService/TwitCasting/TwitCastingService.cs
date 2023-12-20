using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.HttpClients;
using Discord_Stream_Notify_Bot.Interaction;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Discord_Stream_Notify_Bot.SharedService.Twitcasting
{
    public class TwitcastingService : IInteractionService
    {
        private readonly HashSet<int> hashSet = new HashSet<int>();
        private readonly DiscordSocketClient _client;
        private readonly TwitcastingClient _twitcastingClient;
        private readonly EmojiService _emojiService;
        private readonly Timer _timer;

        private string twitcastingRecordPath = "";
        private bool isRuning = false;

        public TwitcastingService(DiscordSocketClient client, TwitcastingClient twitcastingClient, BotConfig botConfig, EmojiService emojiService)
        {
            _client = client;
            _twitcastingClient = twitcastingClient;
            twitcastingRecordPath = botConfig.TwitcastingRecordPath;
            if (string.IsNullOrEmpty(twitcastingRecordPath)) twitcastingRecordPath = Program.GetDataFilePath("");
            if (!twitcastingRecordPath.EndsWith(Program.GetPlatformSlash())) twitcastingRecordPath += Program.GetPlatformSlash();
            _emojiService = emojiService;
            _timer = new Timer(async (obj) => { await TimerHandel(); },
                null, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1));
        }

        public async Task<(string ChannelId, string ChannelTitle)> GetChannelIdAndTitleAsync(string channelUrl)
        {
            string channelId = channelUrl.Split('?')[0].Replace("https://twitcasting.tv/", "").Split('/')[0];
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

        private async Task TimerHandel()
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

                        if (twitcastingDb.TwitcastingStreams.AsNoTracking().Any((x) => x.StreamId == data.Movie.Id))
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
                            else if (streamToken == "403") // 回傳403代表該直播有密碼，拿不到後續的資料所以僅需回傳有人開台就好
                            {
                                var needPasswordTwitcastingStream = new TwitcastingStream()
                                {
                                    ChannelId = item.ChannelId,
                                    ChannelTitle = item.ChannelTitle,
                                    StreamId = data.Movie.Id,
                                    StreamTitle = "(私人直播)"
                                };
                                twitcastingDb.TwitcastingStreams.Add(needPasswordTwitcastingStream);
                                await SendStreamMessageAsync(needPasswordTwitcastingStream, true, false);
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
                                StreamTitle = streamData.Movie.Title ?? "無標題",
                                StreamSubTitle = streamData.Movie.Telop,
                                Category = streamData.Movie.Category?.Name,
                                StreamStartAt = startAt
                            };

                            twitcastingDb.TwitcastingStreams.Add(twitcastingStream);

                            await SendStreamMessageAsync(twitcastingStream, false, item.IsRecord && RecordTwitcasting(twitcastingStream));
                        }
                        catch (Exception ex) { Log.Error($"TwitcastingService-GetData {item.ChannelId}: {ex}"); }

                        await Task.Delay(1000); // 等個一秒鐘避免觸發429之類的錯誤，雖然也不知道有沒有用
                    }
                }
                catch (Exception ex) { Log.Error($"TwitcastingService-Timer {ex}"); }
                finally { isRuning = false; }
            }
            twitcastingDb.SaveChanges();
        }

        private async Task SendStreamMessageAsync(TwitcastingStream twitcastingStream, bool isPrivate = false, bool isRecord = false)
        {
#if DEBUG || DEBUG_DONTREGISTERCOMMAND
            Log.New($"Twitcasting開台通知: {twitcastingStream.ChannelTitle} - {twitcastingStream.StreamTitle} (isPrivate: {isPrivate})");
#else
            using (var db = DBContext.GetDbContext())
            {
                var noticeGuildList = db.NoticeTwitcastingStreamChannels.Where((x) => x.ChannelId == twitcastingStream.ChannelId).ToList();
                Log.New($"發送Twitcasting開台通知 ({noticeGuildList.Count}): {twitcastingStream.ChannelTitle} - {twitcastingStream.StreamTitle} (私人直播: {isPrivate})");

                EmbedBuilder embedBuilder = new EmbedBuilder()
                    .WithTitle(twitcastingStream.StreamTitle)
                    .WithDescription(Format.Url($"{twitcastingStream.ChannelTitle}", $"https://twitcasting.tv/{twitcastingStream.ChannelId}"))
                    .WithUrl($"https://twitcasting.tv/{twitcastingStream.ChannelId}/movie/{twitcastingStream.StreamId}")
                    .AddField("需要密碼的私人直播", isPrivate ? "是" : "否", true);

                if (!string.IsNullOrEmpty(twitcastingStream.StreamSubTitle)) embedBuilder.AddField("副標題", twitcastingStream.StreamSubTitle, true);
                if (!string.IsNullOrEmpty(twitcastingStream.Category)) embedBuilder.AddField("分類", twitcastingStream.Category, true);

                embedBuilder.AddField("開始時間", twitcastingStream.StreamStartAt.ConvertDateTimeToDiscordMarkdown());

                if (isPrivate) embedBuilder.WithErrorColor();
                if (isRecord) embedBuilder.WithRecordColor();
                else embedBuilder.WithOkColor();

                MessageComponent comp = new ComponentBuilder()
                        .WithButton("贊助小幫手 (Patreon) #ad", style: ButtonStyle.Link, emote: _emojiService.PatreonEmote, url: Utility.PatreonUrl, row: 1)
                        .WithButton("贊助小幫手 (Paypal) #ad", style: ButtonStyle.Link, emote: _emojiService.PayPalEmote, url: Utility.PaypalUrl, row: 1).Build();

                foreach (var item in noticeGuildList)
                {
                    try
                    {
                        var guild = _client.GetGuild(item.GuildId);
                        if (guild == null) continue;
                        var channel = guild.GetTextChannel(item.DiscordChannelId);
                        if (channel == null) continue;

                        var message = await channel.SendMessageAsync(item.StartStreamMessage, false, embedBuilder.Build(), components: comp);

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

        private bool RecordTwitcasting(TwitcastingStream twitcastingStream)
        {
            Log.Info($"{twitcastingStream.ChannelTitle} ({twitcastingStream.StreamId}): {twitcastingStream.StreamTitle}");

            try
            {
                if (!Directory.Exists(twitcastingRecordPath))
                    Directory.CreateDirectory(twitcastingRecordPath);
            }
            catch (Exception ex)
            {
                Log.Error($"Twitcasting保存路徑不存在且不可建立: {twitcastingRecordPath}");
                Log.Error($"更改保存路徑至Data資料夾: {Program.GetDataFilePath("")}");
                Log.Error(ex.ToString());

                twitcastingRecordPath = Program.GetDataFilePath("");
            }

            // 自幹Tc錄影能錄但時間會出問題，還是用StreamLink方案好了
            string procArgs = $"streamlink https://twitcasting.tv/{twitcastingStream.ChannelId} best --output \"{twitcastingRecordPath}[{twitcastingStream.ChannelId}]{twitcastingStream.StreamStartAt:yyyyMMdd} - {twitcastingStream.StreamId}.ts\"";
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) Process.Start("tmux", $"new-window -d -n \"Twitcasting {twitcastingStream.ChannelId}\" {procArgs}");
                else Process.Start(new ProcessStartInfo()
                {
                    FileName = "streamlink",
                    Arguments = procArgs.Replace("streamlink", ""),
                    CreateNoWindow = false,
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "RecordTwitcasting 失敗，請確認是否已安裝 StreamLink");
                return false;
            }
        }
    }
}