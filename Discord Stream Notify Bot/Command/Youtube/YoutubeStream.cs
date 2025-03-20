using Discord.Commands;
using Discord_Stream_Notify_Bot.Command.Attribute;
using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Discord_Stream_Notify_Bot.Command.Youtube
{
    public partial class YoutubeStream : TopLevelModule, ICommandService
    {
        private readonly DiscordSocketClient _client;
        private readonly SharedService.Youtube.YoutubeStreamService _service;
        private readonly MainDbService _dbService;

        public YoutubeStream(DiscordSocketClient client, SharedService.Youtube.YoutubeStreamService service, MainDbService dbService)
        {
            _client = client;
            _service = service;
            _dbService = dbService;
        }

        [RequireContext(ContextType.DM)]
        [Command("RightNowRecordStream")]
        [Summary("馬上錄影")]
        [Alias("RNRS")]
        [RequireOwner]
        public async Task RightNowRecordStream(string videoId)
        {
            await Context.Channel.TriggerTypingAsync();

            if (videoId.Length != 11)
            {
                var match = Regex.Match(videoId, @"(?<=youtu\.be\/|youtube\.com\/(?:watch\?.*v=|live\/))(?'VideoId'[\w-]{11})");

                if (match.Success)
                {
                    videoId = match.Groups["VideoId"].Value;
                }
                else
                {
                    await Context.Channel.SendConfirmAsync("Regex 驗證失敗，請確認是否輸入正確的網址").ConfigureAwait(false);
                    return;
                }

                if (videoId.Length != 11)
                {
                    await Context.Channel.SendConfirmAsync("VideoId 錯誤錯誤，需為 11 字數").ConfigureAwait(false);
                    return;
                }
            }

            var nowRecordStreamList = Utility.GetNowRecordStreamList();

            if (nowRecordStreamList.Contains(videoId) &&
                !await PromptUserConfirmAsync(new EmbedBuilder().WithErrorColor().WithDescription("已經在錄影了，確定繼續?")))
                return;

            Google.Apis.YouTube.v3.Data.Video video;
            try
            {
                video = await _service.GetVideoAsync(videoId);
            }
            catch (Exception ex)
            {
                await Context.Channel.SendErrorAsync(ex.ToString());
                return;
            }

            if (video == null)
            {
                await Context.Channel.SendConfirmAsync($"{videoId} 不存在").ConfigureAwait(false);
                return;
            }

            var description = $"{Format.Url(video.Snippet.Title, $"https://www.youtube.com/watch?v={videoId}")}\n" +
                    $"{Format.Url(video.Snippet.ChannelTitle, $"https://www.youtube.com/channel/{video.Snippet.ChannelId}")}";

            using var db = _dbService.GetDbContext();
            if (!db.HasStreamVideoByVideoId(videoId))
                await _service.AddOtherDataAsync(video, true);

            try
            {
                if (Bot.Redis != null)
                {
                    if (await Bot.RedisSub.PublishAsync(new RedisChannel("youtube.record", RedisChannel.PatternMode.Literal), videoId) != 0)
                    {
                        Log.Info($"已發送錄影請求: {videoId}");
                        await Context.Channel.SendConfirmAsync("已開始錄影", description).ConfigureAwait(false);

                        if (_service.Reminders.TryRemove(videoId, out _))
                            await Context.Channel.SendConfirmAsync("已從排程清單中移除該直播").ConfigureAwait(false);
                    }
                    else
                    {
                        Log.Warn($"Redis Sub 頻道不存在，請開啟錄影工具: {videoId}");
                        await Context.Channel.SendErrorAsync("Redis Sub 頻道不存在，請開啟錄影工具", description).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"RightNowRecordStream-Record: {videoId}\n{ex}");
                await Context.Channel.SendErrorAsync(ex.ToString()).ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.DM)]
        [Command("AddVideoData")]
        [Summary("新增影片資料並發送通知")]
        [Alias("aod")]
        [RequireOwner]
        public async Task AddVideoDataAsync(string videoId)
        {
            await Context.Channel.TriggerTypingAsync();

            if (videoId.Length != 11)
            {
                var match = Regex.Match(videoId, @"(?<=youtu\.be\/|youtube\.com\/(?:watch\?.*v=|live\/))(?'VideoId'[\w-]{11})");

                if (match.Success)
                {
                    videoId = match.Groups["VideoId"].Value;
                }
                else
                {
                    await Context.Channel.SendConfirmAsync("Regex 驗證失敗，請確認是否輸入正確的網址").ConfigureAwait(false);
                    return;
                }

                if (videoId.Length != 11)
                {
                    await Context.Channel.SendConfirmAsync("VideoId 錯誤錯誤，需為 11 字數").ConfigureAwait(false);
                    return;
                }
            }

            Google.Apis.YouTube.v3.Data.Video video;
            try
            {
                video = await _service.GetVideoAsync(videoId);
            }
            catch (Exception ex)
            {
                await Context.Channel.SendErrorAsync(ex.ToString());
                return;
            }

            if (video == null)
            {
                await Context.Channel.SendConfirmAsync($"{videoId} 不存在").ConfigureAwait(false);
                return;
            }

            using var db = _dbService.GetDbContext();
            if (!db.HasStreamVideoByVideoId(videoId))
            {
                await _service.AddOtherDataAsync(video, false);
                await Context.Channel.SendConfirmAsync($"已添加資料: {video.Snippet.ChannelTitle} - {video.Snippet.Title}");
            }
            else
            {
                await Context.Channel.SendErrorAsync($"資料已存在於資料庫內，忽略");
            }
        }

        [RequireContext(ContextType.DM)]
        [Command("ForceReSubscribeSpider")]
        [Summary("強制重新註冊爬蟲 (all or channelUrl)")]
        [Alias("frss")]
        [CommandExample("all", "998rrr", "UCs5FNYPHeZz5f7N1BDExxfg")]
        [RequireOwner]
        public async Task ForceReSubscribeSpider(string channelUrl)
        {
            await Context.Channel.TriggerTypingAsync();

            string channelId = "";
            try
            {
                channelId = await _service.GetChannelIdAsync(channelUrl).ConfigureAwait(false);
            }
            catch (FormatException fex)
            {
                await Context.Channel.SendErrorAsync(fex.Message);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Channel.SendErrorAsync("網址不可空白");
                return;
            }

            using var db = _dbService.GetDbContext();

            if (channelId == "all")
            {
                if (await PromptUserConfirmAsync(new EmbedBuilder().WithOkColor().WithDescription("是否要重新註冊全部的爬蟲?")))
                {
                    foreach (var item in db.YoutubeChannelSpider)
                    {
                        item.LastSubscribeTime = DateTime.MinValue;
                    }
                }
            }
            else
            {
                var youtubeChannelSpider = db.YoutubeChannelSpider.FirstOrDefault((x) => x.ChannelId == channelId);
                if (youtubeChannelSpider == null)
                {
                    await Context.Channel.SendErrorAsync($"資料庫中找不到 {channelId} 的爬蟲");
                    return;
                }
                else
                {
                    youtubeChannelSpider.LastSubscribeTime = DateTime.MinValue;
                }
            }

            db.SaveChanges();

            await Context.Channel.SendConfirmAsync("已變更，等待爬蟲註冊中...");
            await _service.SubscribePubSubAsync();
        }

        [RequireContext(ContextType.DM)]
        [Command("GetNotionGuild")]
        [Summary("取得已設定通知的伺服器")]
        [Alias("gng")]
        [CommandExample("998rrr", "UCs5FNYPHeZz5f7N1BDExxfg")]
        [RequireOwner]
        public async Task GetNotionGuild(string channelUrl)
        {
            await Context.Channel.TriggerTypingAsync();

            string channelId = "";
            try
            {
                channelId = await _service.GetChannelIdAsync(channelUrl).ConfigureAwait(false);
            }
            catch (FormatException fex)
            {
                await Context.Channel.SendErrorAsync(fex.Message);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Channel.SendErrorAsync("網址不可空白");
                return;
            }

            using var db = _dbService.GetDbContext();

            var youtubeChannelSpider = db.YoutubeChannelSpider.AsNoTracking().FirstOrDefault((x) => x.ChannelId == channelId);

            var guildList = new List<string>();
            foreach (var item in db.NoticeYoutubeStreamChannel.AsNoTracking().Where((x) => x.YouTubeChannelId == channelId))
            {
                var guild = _client.GetGuild(item.GuildId);
                if (guild == null)
                {
                    guildList.Add($"{item.GuildId}: (已離開)");
                }
                else
                {
                    guildList.Add($"{item.GuildId}: {guild.Name}");
                }
            }

            await Context.SendPaginatedConfirmAsync(0, (page) =>
            {
                return new EmbedBuilder()
                   .WithOkColor()
                   .WithTitle($"設定 `" + (youtubeChannelSpider != null ? youtubeChannelSpider.ChannelTitle : channelId) + "` 通知的伺服器清單")
                   .WithDescription(string.Join('\n', guildList.Skip(page * 20).Take(20)));
            }, guildList.Count, 20);
        }

        [RequireContext(ContextType.DM)]
        [Command("AddRecordChannel")]
        [Summary("新增直播記錄頻道")]
        [Alias("ARC")]
        [RequireOwner]
        public async Task AddRecordChannel([Summary("頻道網址")] string channelUrl)
        {
            string channelId = "";
            try
            {
                channelId = await _service.GetChannelIdAsync(channelUrl).ConfigureAwait(false);
            }
            catch (FormatException fex)
            {
                await Context.Channel.SendErrorAsync(fex.Message);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Channel.SendErrorAsync("網址不可空白");
                return;
            }

            using (var db = _dbService.GetDbContext())
            {
                if (db.RecordYoutubeChannel.Any((x) => x.YoutubeChannelId == channelId))
                {
                    await Context.Channel.SendConfirmAsync($"{channelId} 已存在於直播記錄清單");
                    return;
                }

                string channelTitle = await GetChannelTitle(channelId);

                if (channelTitle == "")
                {
                    await Context.Channel.SendConfirmAsync($"頻道 {channelId} 不存在").ConfigureAwait(false);
                    return;
                }

                db.RecordYoutubeChannel.Add(new RecordYoutubeChannel() { YoutubeChannelId = channelId });
                db.SaveChanges();
                await Context.Channel.SendConfirmAsync($"已新增 {channelTitle} 至直播記錄清單").ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.DM)]
        [Command("RemoveRecordChannel")]
        [Summary("移除直播記錄頻道")]
        [Alias("RRC")]
        [RequireOwner]
        public async Task RemoveRecordChannel([Summary("頻道網址")] string channelUrl)
        {
            string channelId = "";
            try
            {
                channelId = await _service.GetChannelIdAsync(channelUrl).ConfigureAwait(false);
            }
            catch (FormatException fex)
            {
                await Context.Channel.SendErrorAsync(fex.Message);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Channel.SendErrorAsync("網址不可空白");
                return;
            }

            using (var db = _dbService.GetDbContext())
            {
                if (!db.RecordYoutubeChannel.Any((x) => x.YoutubeChannelId == channelId))
                {
                    await Context.Channel.SendConfirmAsync($"直播記錄清單中沒有 {channelId}").ConfigureAwait(false);
                    return;
                }

                string channelTitle = await GetChannelTitle(channelId);
                if (string.IsNullOrEmpty(channelTitle)) channelTitle = channelId;

                db.RecordYoutubeChannel.Remove(db.RecordYoutubeChannel.First((x) => x.YoutubeChannelId == channelId));
                db.SaveChanges();
                await Context.Channel.SendConfirmAsync($"已從直播記錄清單中移除 {channelTitle}").ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.DM)]
        [Command("ListRecordChannel")]
        [Summary("顯示直播記錄頻道")]
        [Alias("LRC")]
        public async Task ListRecordChannel()
        {
            await Context.Channel.TriggerTypingAsync().ConfigureAwait(false);

            using (var db = _dbService.GetDbContext())
            {
                var nowRecordList = db.RecordYoutubeChannel.Select((x) => x.YoutubeChannelId).ToList();

                if (nowRecordList.Count > 0)
                {
                    var list = new List<string>();

                    for (int i = 0; i < nowRecordList.Count; i += 50)
                    {
                        list.AddRange(await GetChannelTitle(nowRecordList.Skip(i).Take(50)));
                    }

                    list.Sort();
                    await Context.SendPaginatedConfirmAsync(0, page =>
                    {
                        return new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("直播記錄清單")
                            .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20)))
                            .WithFooter($"{Math.Min(list.Count, (page + 1) * 20)} / {list.Count}個頻道");
                    }, list.Count, 20, false);
                }
                else await Context.Channel.SendConfirmAsync($"直播記錄清單中沒有任何頻道").ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.DM)]
        [Command("ToggleRecord")]
        [Summary("切換直播記錄")]
        [Alias("TR")]
        [RequireOwner]
        public async Task ToggleRecord()
        {
            _service.IsRecord = !_service.IsRecord;

            await Context.Channel.SendConfirmAsync("直播錄影已" + (_service.IsRecord ? "開啟" : "關閉")).ConfigureAwait(false);
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("GetVideoInfo")]
        [Summary("從資料庫取得已刪檔的直播資訊")]
        [CommandExample("GzWDcutkMQw")]
        [Alias("GVI")]
        public async Task GetVideoInfo(string videoId = "")
        {
            videoId = videoId.Trim();

            if (string.IsNullOrWhiteSpace(videoId))
            {
                await ReplyAsync("VideoId 空白").ConfigureAwait(false);
                return;
            }

            videoId = _service.GetVideoId(videoId);

            var video = Extensions.GetStreamVideoByVideoId(videoId);
            if (video == null)
            {
                await ReplyAsync($"不存在 {videoId} 的影片").ConfigureAwait(false);
                return;
            }

            EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor()
                .WithTitle(video.VideoTitle)
                .WithUrl($"https://www.youtube.com/watch?v={videoId}")
                .WithDescription(Format.Url(video.ChannelTitle, $"https://www.youtube.com/channel/{video.ChannelId}"))
                .AddField("排定開台時間", video.ScheduledStartTime, true);

            await ReplyAsync(embed: embedBuilder.Build()).ConfigureAwait(false);
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("SetChannelType")]
        [Summary("設定頻道的所屬\n" +
            "0: Hololive\n" +
            "1: 彩虹社\n" +
            "2: 其他")]
        [CommandExample("https://www.youtube.com/channel/UCXRlIK3Cw_TJIQC5kSJJQMg 1")]
        [Alias("SCT")]
        public async Task SetChannelType([Summary("頻道網址")] string channelUrl = "", DataBase.Table.Video.YTChannelType channelType = DataBase.Table.Video.YTChannelType.Other)
        {
            string channelId = "";
            try
            {
                channelId = await _service.GetChannelIdAsync(channelUrl).ConfigureAwait(false);
            }
            catch (FormatException fex)
            {
                await Context.Channel.SendErrorAsync(fex.Message);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Channel.SendErrorAsync("網址不可空白");
                return;
            }

            if (string.IsNullOrWhiteSpace(channelId))
            {
                await Context.Channel.SendErrorAsync("ChannelId空白").ConfigureAwait(false);
                return;
            }

            var title = await GetChannelTitle(channelId);
            if (string.IsNullOrWhiteSpace(title))
            {
                await Context.Channel.SendErrorAsync($"{channelId} 不存在頻道").ConfigureAwait(false);
                return;
            }

            using (var db = _dbService.GetDbContext())
            {
                var channel = db.YoutubeChannelOwnedType.FirstOrDefault((x) => x.ChannelId == channelId);
                if (channel == null)
                {
                    db.YoutubeChannelOwnedType.Add(new YoutubeChannelOwnedType() { ChannelId = channelId, ChannelTitle = title, ChannelType = channelType });
                }
                else
                {
                    channel.ChannelTitle = title;
                    channel.ChannelType = channelType;
                    db.YoutubeChannelOwnedType.Update(channel);
                }

                db.SaveChanges();
                await Context.Channel.SendConfirmAsync($"`{title}` 的所屬已改為 `{channelType.GetProductionName()}`");
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("FixNijisanjiDatabase")]
        [Alias("FixND")]
        public async Task FixNijisanjiDatabase()
        {
            using (var db = _dbService.GetDbContext())
            {
                try
                {
                    var needFixList = db.NijisanjiVideos.Where((x) => x.ChannelId.StartsWith("https"));

                    foreach (var item in needFixList)
                    {
                        item.ChannelId = await _service.GetChannelIdAsync(item.ChannelId);
                        db.NijisanjiVideos.Update(item);
                    }

                    int result = await db.SaveChangesAsync();

                    await Context.Channel.SendConfirmAsync($"已修正 {result} 個影片資料");
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Demystify(), "FixNijisanjiDatabase");
                }
            }
        }

        private async Task<string> GetChannelTitle(string channelId)
        {
            try
            {
                var channel = _service.YouTubeService.Channels.List("snippet");
                channel.Id = channelId;
                var response = await channel.ExecuteAsync().ConfigureAwait(false);
                return response.Items[0].Snippet.Title;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "GetChannelTitle");
                return "";
            }
        }

        private async Task<List<string>> GetChannelTitle(IEnumerable<string> channelId)
        {
            try
            {
                var channel = _service.YouTubeService.Channels.List("snippet");
                channel.Id = string.Join(",", channelId);
                var response = await channel.ExecuteAsync().ConfigureAwait(false);
                return response.Items.Select((x) => Format.Url(x.Snippet.Title, $"https://www.youtube.com/channel/{x.Id}")).ToList();
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "GetChannelTitle");
                return null;
            }
        }
    }
}