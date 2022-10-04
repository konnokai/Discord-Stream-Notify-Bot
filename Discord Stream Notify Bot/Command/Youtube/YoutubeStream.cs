using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Discord_Stream_Notify_Bot.Command.Attribute;
using Discord_Stream_Notify_Bot.DataBase.Table;
using System.Runtime.InteropServices;

namespace Discord_Stream_Notify_Bot.Command.Youtube
{
    public partial class YoutubeStream : TopLevelModule, ICommandService
    {
        #region Send Ctrl + C to process
        // https://blog.csdn.net/u014070086/article/details/121562185
        // 导入Win32 Console函数
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        delegate Boolean ConsoleCtrlDelegate(CtrlTypes type);

        // 控制消息
        enum CtrlTypes : uint
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT,
            CTRL_CLOSE_EVENT,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GenerateConsoleCtrlEvent(CtrlTypes dwCtrlEvent, uint dwProcessGroupId);
        #endregion

        private readonly DiscordSocketClient _client;
        private readonly HttpClients.DiscordWebhookClient _discordWebhookClient;
        private readonly SharedService.Youtube.YoutubeStreamService _service;

        public YoutubeStream(DiscordSocketClient client, HttpClients.DiscordWebhookClient discordWebhookClient, SharedService.Youtube.YoutubeStreamService service)
        {
            _client = client;
            _discordWebhookClient = discordWebhookClient;
            _service = service;
        }

        [RequireContext(ContextType.DM)]
        [Command("RightNowRecordStream")]
        [Summary("馬上錄影")]
        [Alias("RNRS")]
        [RequireOwner]
        public async Task RightNowRecordStream(string videoId)
        {
            try
            {
                if (videoId.Contains("www.youtube.com/watch")) //https://www.youtube.com/watch?v=7DqDRE_SW34
                    videoId = videoId.Substring(videoId.IndexOf("?v=") + 3, 11);
                else if (videoId.Contains("https://youtu.be")) //https://youtu.be/Z-UJbyLqioM
                    videoId = videoId.Substring(17, 11);
            }
            catch (Exception)
            {
                await Context.Channel.SendConfirmAsync("VideoId錯誤，請確認是否輸入正確的網址").ConfigureAwait(false);
                return;
            }

            if (videoId.Length != 11)
            {
                await Context.Channel.SendConfirmAsync("VideoId錯誤錯誤，需為11字數").ConfigureAwait(false);
                return;
            }

            var nowRecordStreamList = Utility.GetNowRecordStreamList();

            if (nowRecordStreamList.Contains(videoId))
            {
                await Context.Channel.SendConfirmAsync($"{videoId} 已經在錄影了").ConfigureAwait(false);
                return;
            }

            Google.Apis.YouTube.v3.Data.Video video;
            try
            {
                video = await _service.GetVideoAsync(videoId, 3);
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
            if (await PromptUserConfirmAsync(new EmbedBuilder().WithTitle("現在錄影?").WithDescription(description)))
            {
                if (await Program.RedisSub.PublishAsync("youtube.record", videoId).ConfigureAwait(false) != 0)
                    await Context.Channel.SendConfirmAsync("已開始錄影", description).ConfigureAwait(false);
                else
                    await Context.Channel.SendConfirmAsync($"Redis錯誤，請確認後端狀態").ConfigureAwait(false);
            }
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

            using (var db = DataBase.DBContext.GetDbContext())
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

                if (await PromptUserConfirmAsync(new EmbedBuilder().WithTitle("新增頻道至直播記錄清單?").WithDescription(channelTitle)))
                {
                    db.RecordYoutubeChannel.Add(new DataBase.Table.RecordYoutubeChannel() { YoutubeChannelId = channelId });
                    db.SaveChanges();
                    await Context.Channel.SendConfirmAsync($"已新增 {channelTitle} 至直播記錄清單").ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.DM)]
        [Command("RemoveRecordChannel")]
        [Summary("移除直播記錄頻道")]
        [Alias("RRC")]
        [RequireOwner]
        public async Task RemoveRecordChannel([Summary("頻道網址")]string channelUrl)
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

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (!db.RecordYoutubeChannel.Any((x) => x.YoutubeChannelId == channelId))
                {
                    await Context.Channel.SendConfirmAsync($"直播記錄清單中沒有 {channelId}").ConfigureAwait(false);
                    return;
                }

                string channelTitle = await GetChannelTitle(channelId);
                if (string.IsNullOrEmpty(channelTitle)) channelTitle = channelId;

                if (await PromptUserConfirmAsync(new EmbedBuilder().WithTitle("從直播記錄清單移除頻道?").WithDescription(channelTitle)))
                {
                    db.RecordYoutubeChannel.Remove(db.RecordYoutubeChannel.First((x) => x.YoutubeChannelId == channelId));
                    db.SaveChanges();
                    await Context.Channel.SendConfirmAsync($"已從直播記錄清單中移除 {channelTitle}").ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.DM)]
        [Command("ListRecordChannel")]
        [Summary("顯示直播記錄頻道")]
        [Alias("LRC")]
        public async Task ListRecordChannel()
        {
            await Context.Channel.TriggerTypingAsync().ConfigureAwait(false);
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var nowRecordList = db.RecordYoutubeChannel.ToList().Select((x) => x.YoutubeChannelId).ToList();

                db.YoutubeChannelSpider.ToList().ForEach((item) => { if (!item.IsTrustedChannel && nowRecordList.Contains(item.ChannelId)) nowRecordList.Remove(item.ChannelId); });
                int warningChannelNum = db.YoutubeChannelSpider.Count((x) => x.IsTrustedChannel);

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
                            .WithFooter($"{Math.Min(list.Count, (page + 1) * 20)} / {list.Count}個頻道 ({warningChannelNum}個非VTuber的頻道)");
                    }, list.Count, 20, false);
                }
                else await Context.Channel.SendConfirmAsync($"直播記錄清單中沒有任何頻道").ConfigureAwait(false);
            }
        }

        [Command("NowRecordChannel")]
        [Summary("取得現在記錄直播的清單")]
        [Alias("NRC")]
        public async Task NowRecordChannel()
        {
            await Context.Channel.TriggerTypingAsync().ConfigureAwait(false);

            var newRecordStreamList = Utility.GetNowRecordStreamList();

            if (newRecordStreamList.Count == 0)
            {
                await Context.Channel.SendErrorAsync("現在沒有直播記錄").ConfigureAwait(false);
                return;
            }

            try
            {
                var yt = _service.yt.Videos.List("Snippet");
                yt.Id = string.Join(',', newRecordStreamList);
                var result = (await yt.ExecuteAsync().ConfigureAwait(false)).Items.ToList();

                var endStreamList = result.Where((x) => x.Snippet.LiveBroadcastContent == "none").ToList();
                foreach (var item in endStreamList)
                {
                    try
                    {
                        result.Remove(item);
                        await Program.RedisDb.SetRemoveAsync("youtube.nowRecord", item.Id);
                    }
                    catch (Exception ex)
                    {
                        await Context.Channel.SendErrorAsync(ex.Message).ConfigureAwait(false);
                    }
                }

                await Context.SendPaginatedConfirmAsync(0, (page) =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("正在錄影的直播")
                        .WithDescription(string.Join("\n\n",
                            result.Skip(page * 9).Take(9)
                            .Select((x) => $"{Format.Url(x.Snippet.Title, $"https://www.youtube.com/watch?v={x.Id}")}\n" +
                                $"{x.Snippet.ChannelTitle}")))
                        .WithFooter($"{result.Count}個頻道");
                }, result.Count, 9, false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Context.Channel.SendErrorAsync(ex.Message).ConfigureAwait(false);
            }
        }

        //[RequireContext(ContextType.DM)]
        //[Command("KillFFMPEGProcess")]
        //[Summary("停止指定FFMPEG ProcessId的直播記錄")]
        //[Alias("KFP")]
        //[RequireOwner]
        //public async Task KillFFMPEGProcess(int pId)
        //{
        //    var process = Process.GetProcessById(pId);

        //    if (process == null)
        //    {
        //        await Context.Channel.SendConfirmAsync("指定ProcessId不存在程序").ConfigureAwait(false);
        //        return;
        //    }

        //    if (process.ProcessName != "ffmpeg")
        //    {
        //        await Context.Channel.SendConfirmAsync("指定ProcessId非ffmpeg").ConfigureAwait(false);
        //        return;
        //    }

        //    FreeConsole();

        //    // 一个进程最多只能attach到一个Console，否则失败，返回0
        //    if (AttachConsole((uint)process.Id))
        //    {
        //        // 设置父进程属性，忽略Ctrl-C信号
        //        SetConsoleCtrlHandler(null, true);

        //        // 发出兩个Ctrl-C到共享该控制台的所有进程中
        //        GenerateConsoleCtrlEvent(CtrlTypes.CTRL_C_EVENT, 0);
        //        GenerateConsoleCtrlEvent(CtrlTypes.CTRL_C_EVENT, 0);

        //        // 父进程与控制台分离，此时子进程控制台收到Ctrl-C关闭
        //        FreeConsole();

        //        // 现在父进程没有Console，为它新建一个
        //        AllocConsole();

        //        // 等待子进程退出
        //        process.WaitForExit(2000);

        //        // 恢复父进程处理Ctrl-C信号
        //        SetConsoleCtrlHandler(null, false);

        //        // C#版的GetLastError()
        //        var lastError = Marshal.GetLastWin32Error();
        //    }


        //    process.CloseMainWindow();
        //    await Context.Channel.SendConfirmAsync($"已停止ProcessId {pId}").ConfigureAwait(false);
        //}

        [RequireContext(ContextType.DM)]
        [Command("ToggleRecord")]
        [Summary("切換直播記錄")]
        [Alias("TS")]
        [RequireOwner]
        public async Task ToggleRecord()
        {
            _service.IsRecord = !_service.IsRecord;

            await Context.Channel.SendConfirmAsync("直播錄影已" + (_service.IsRecord ? "開啟" : "關閉")).ConfigureAwait(false);
        }

        [Command("NowStreaming")]
        [Summary("取得現在直播的Holo成員")]
        [Alias("NS")]
        public async Task NowStreaming() //Todo: 加入2434
        {
            await Context.Channel.TriggerTypingAsync().ConfigureAwait(false);

            var embed = await _service.GetNowStreamingChannel().ConfigureAwait(false);

            if (embed == null) await Context.Channel.SendMessageAsync("無法取得直播清單").ConfigureAwait(false);
            else await Context.Channel.SendMessageAsync(null, false, embed).ConfigureAwait(false);
        }

        [Command("ComingSoonStream")]
        [Summary("顯示接下來直播的清單")]
        [Alias("CSS")]
        public async Task ComingSoonStream()
        {
            await Context.Channel.TriggerTypingAsync().ConfigureAwait(false);

            try
            {
                List<Google.Apis.YouTube.v3.Data.Video> result = new List<Google.Apis.YouTube.v3.Data.Video>();

                for (int i = 0; i < _service.Reminders.Values.Count; i += 50)
                {
                    var yt = _service.yt.Videos.List("snippet,liveStreamingDetails");
                    yt.Id = string.Join(',', _service.Reminders.Values.Select((x) => x.StreamVideo.VideoId).Skip(i).Take(50));
                    result.AddRange((await yt.ExecuteAsync().ConfigureAwait(false)).Items);
                }
                using (var db = DataBase.DBContext.GetDbContext())
                {
                    result = result.OrderBy((x) => x.LiveStreamingDetails.ScheduledStartTime.Value).ToList();
                    await Context.SendPaginatedConfirmAsync(0, (act) =>
                    {
                        return new EmbedBuilder().WithOkColor()
                        .WithTitle("接下來開台的清單")
                        .WithDescription(string.Join("\n\n",
                           result.Skip(act * 7).Take(7)
                           .Select((x) => $"{Format.Url(x.Snippet.Title, $"https://www.youtube.com/watch?v={x.Id}")}" +
                           $"\n{Format.Url(x.Snippet.ChannelTitle, $"https://www.youtube.com/channel/{x.Snippet.ChannelId}")}" +
                           $"\n直播時間: {x.LiveStreamingDetails.ScheduledStartTime.Value}" +
                           "\n是否在直播錄影清單內: " + (db.RecordYoutubeChannel.Any((x2) => x2.YoutubeChannelId.Trim() == x.Snippet.ChannelId) ? "是" : "否"))));
                    }, result.Count, 7).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message + "r\n" + ex.StackTrace);
            }
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
                await ReplyAsync("VideoId空白").ConfigureAwait(false);
                return;
            }
            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (!db.HasStreamVideoByVideoId(videoId))
                {
                    await ReplyAsync($"不存在 {videoId} 的影片").ConfigureAwait(false);
                    return;
                }

                var video = Extensions.GetStreamVideoByVideoId(videoId);
                EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor()
                    .WithTitle(video.VideoTitle)
                    .WithUrl($"https://www.youtube.com/watch?v={videoId}")
                    .WithDescription(Format.Url(video.ChannelTitle, $"https://www.youtube.com/channel/{video.ChannelId}"))
                    .AddField("排定開台時間", video.ScheduledStartTime, true);

                await ReplyAsync(embed: embedBuilder.Build()).ConfigureAwait(false);
            }
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
            using (var db = DataBase.DBContext.GetDbContext())
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

        private async Task<string> GetChannelTitle(string channelId)
        {
            try
            {
                var channel = _service.yt.Channels.List("snippet");
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

        private async Task<List<string>> GetChannelTitle(IEnumerable<string> channelId)
        {
            try
            {
                var channel = _service.yt.Channels.List("snippet");
                channel.Id = string.Join(",", channelId);
                var response = await channel.ExecuteAsync().ConfigureAwait(false);
                return response.Items.Select((x) => Format.Url(x.Snippet.Title, $"https://www.youtube.com/channel/{x.Id}")).ToList();
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message + "\n" + ex.StackTrace);
                return null;
            }
        }
    }
}