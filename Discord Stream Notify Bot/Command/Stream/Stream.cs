using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord_Stream_Notify_Bot.DataBase;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Command.Stream
{
    public partial class Stream : TopLevelModule<Service.StreamService>
    {
        private readonly DiscordSocketClient _client;

        public Stream(DiscordSocketClient client)
        {
            _client = client;
        }

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

            if (nowRecordStreamList.ContainsKey(videoId))
            {
                await Context.Channel.SendConfirmAsync($"{videoId} 已經在錄影了").ConfigureAwait(false);
                return;
            }

            using (var db = new DBContext())
            {
                var video = await _service.GetVideoAsync(videoId);

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
        }

        [Command("AddRecordChannel")]
        [Summary("新增直播記錄頻道")]
        [Alias("ARC")]
        [RequireOwner]
        public async Task AddRecordChannel(string channelId)
        {
            if (!channelId.Contains("UC"))
            {
                await Context.Channel.SendConfirmAsync("頻道Id錯誤").ConfigureAwait(false);
                return;
            }

            try
            {
                channelId = channelId.Substring(channelId.IndexOf("UC"), 24);
            }
            catch
            {
                await Context.Channel.SendConfirmAsync("頻道Id格式錯誤，需為24字數").ConfigureAwait(false);
                return;
            }

            using (var db = new DBContext())
            {
                if (db.RecordChannel.Any((x) => x.ChannelId == channelId))
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
                    db.RecordChannel.Add(new DataBase.Table.RecordChannel() { ChannelId = channelId });
                    await db.SaveChangesAsync();
                    await Context.Channel.SendConfirmAsync($"已新增 {channelTitle} 至直播記錄清單").ConfigureAwait(false);
                }
            }
        }

        [Command("RemoveRecordChannel")]
        [Summary("移除直播記錄頻道")]
        [Alias("RRC")]
        [RequireOwner]
        public async Task RemoveRecordChannel(string channelId)
        {
            if (!channelId.Contains("UC"))
            {
                await Context.Channel.SendConfirmAsync("頻道Id錯誤").ConfigureAwait(false);
                return;
            }

            try
            {
                channelId = channelId.Substring(channelId.IndexOf("UC"), 24);
            }
            catch
            {
                await Context.Channel.SendConfirmAsync("頻道Id格式錯誤，需為24字數").ConfigureAwait(false);
                return;
            }

            using (var db = new DBContext())
            {
                if (!db.RecordChannel.Any((x) => x.ChannelId == channelId))
                {
                    await Context.Channel.SendConfirmAsync($"直播記錄清單中沒有 {channelId}").ConfigureAwait(false);
                    return;
                }

                string channelTitle = await GetChannelTitle(channelId);

                if (channelTitle == "")
                {
                    await Context.Channel.SendConfirmAsync($"頻道 {channelId} 不存在").ConfigureAwait(false);
                    return;
                }

                if (await PromptUserConfirmAsync(new EmbedBuilder().WithTitle("從直播記錄清單移除頻道?").WithDescription(channelTitle)))
                {
                    db.RecordChannel.Remove(db.RecordChannel.First((x) => x.ChannelId == channelId));
                    await db.SaveChangesAsync();
                    await Context.Channel.SendConfirmAsync($"已從直播記錄清單中移除 {channelTitle}").ConfigureAwait(false);
                }
            }
        }

        [Command("ListRecordChannel")]
        [Summary("顯示直播記錄頻道")]
        [Alias("LRC")]
        public async Task ListRecordChannel()
        {
            await Context.Channel.TriggerTypingAsync().ConfigureAwait(false);

            using (var db = new DBContext())
            {
                var nowRecordList = db.RecordChannel.ToList().Select((x) => x.ChannelId).ToList();

                db.ChannelSpider.ToList().ForEach((item) => { if (item.IsWarningChannel && nowRecordList.Contains(item.ChannelId)) nowRecordList.Remove(item.ChannelId); });
                int warningChannelNum = db.ChannelSpider.Count((x) => x.IsWarningChannel);

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
                            .WithFooter($"{Math.Min(list.Count, (page + 1) * 20)} / {list.Count}個頻道 ({warningChannelNum}個隱藏的警告頻道)");
                    }, list.Count, 20, false);
                }
                else await Context.Channel.SendConfirmAsync($"直播記錄清單中沒有任何頻道").ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator)]
        [Command("ListWarningRecordChannel")]
        [Summary("顯示直播記錄頻道")]
        [Alias("LWRC")]
        public async Task ListWarningRecordChannel()
        {
            await Context.Channel.TriggerTypingAsync().ConfigureAwait(false);

            using (var db = new DBContext())
            {
                var dbRecordList = db.RecordChannel.ToList().Select((x) => x.ChannelId).ToList();
                var nowRecordList = new List<string>();

                db.ChannelSpider.ToList().ForEach((item) => { if (item.IsWarningChannel && dbRecordList.Contains(item.ChannelId)) nowRecordList.Add(item.ChannelId); });

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
                            .WithTitle("警告的直播記錄清單")
                            .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20)))
                            .WithFooter($"{Math.Min(list.Count, (page + 1) * 20)} / {list.Count}個頻道");
                    }, list.Count, 20, false);
                }
                else await Context.Channel.SendConfirmAsync($"警告直播記錄清單中沒有任何頻道").ConfigureAwait(false);
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
                await Context.Channel.SendConfirmAsync("現在沒有直播記錄").ConfigureAwait(false);
                return;
            }

            try
            {
                var yt = _service.yt.Videos.List("Snippet");
                yt.Id = string.Join(',', newRecordStreamList.Keys);
                var result = await yt.ExecuteAsync().ConfigureAwait(false);
                await Context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle("正在錄影的直播")
                    .WithDescription(string.Join("\n\n",
                    result.Items.Select((x) => $"{Format.Url(x.Snippet.Title, $"https://www.youtube.com/watch?v={x.Id}")}\n{x.Snippet.ChannelTitle} - {newRecordStreamList[x.Id]}")))
                    .WithFooter($"{result.Items.Count}個頻道")
                    .Build())
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Context.Channel.SendConfirmAsync(ex.Message).ConfigureAwait(false);
            }
        }

        [Command("KillRecordProcess")]
        [Summary("停止指定ProcessId的直播記錄")]
        [Alias("KRC")]
        [RequireOwner]
        public async Task KillRecordProcess(int pId)
        {
            var process = Process.GetProcessById(pId);

            if (process == null)
            {
                await Context.Channel.SendConfirmAsync("指定ProcessId不存在程序").ConfigureAwait(false);
                return;
            }

            if (process.ProcessName != "streamlink")
            {
                await Context.Channel.SendConfirmAsync("指定ProcessId非StreamLink").ConfigureAwait(false);
                return;
            }

            process.Kill();
            await Context.Channel.SendConfirmAsync($"已停止ProcessId {pId}").ConfigureAwait(false);
        }

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

        [Command("ComingSoomStream")]
        [Summary("顯示接下來直播的清單")]
        [Alias("CSS")]
        public async Task ComingSoomStream()
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

                result = result.OrderBy((x) => x.LiveStreamingDetails.ScheduledStartTime.Value).ToList();
                using (var db = new DBContext())
                {
                    await Context.SendPaginatedConfirmAsync(0, (act) =>
                    {
                        return new EmbedBuilder().WithOkColor()
                        .WithTitle("接下來開台的清單")
                        .WithDescription(string.Join("\n\n",
                           result.Skip(act * 7).Take(7)
                           .Select((x) => $"{Format.Url(x.Snippet.Title, $"https://www.youtube.com/watch?v={x.Id}")}" +
                           $"\n{Format.Url(x.Snippet.ChannelTitle, $"https://www.youtube.com/channel/{x.Snippet.ChannelId}")}" +
                           $"\n直播時間: {x.LiveStreamingDetails.ScheduledStartTime.Value}" +
                           "\n是否在直播錄影清單內: " + (db.RecordChannel.Any((x2) => x2.ChannelId.Trim() == x.Snippet.ChannelId) ? "是" : "否"))));
                    }, result.Count, 7).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message + "r\n" + ex.StackTrace);
            }
        }

        //[RequireContext(ContextType.Guild)]
        //[RequireUserPermission(ChannelPermission.ManageMessages)]
        //[Command("SetNoticeStreamChannel")]
        //[Summary("設定現在的頻道為直播通知頻道\r\n" +
        //    "再執行一次則會解除直播通知\r\n\r\n" +
        //    "例:\r\n" +
        //    "`s!snsc`")]
        //[Alias("SNSC")]
        //public async Task SetNoticeStreamChannel()
        //{
        //    using (var db = new DBContext())
        //    {
        //        if (!db.GuildConfig.Any((x) => x.GuildId == Context.Guild.Id))
        //        {
        //            db.GuildConfig.Add(new DataBase.Table.GuildConfig() { GuildId = Context.Guild.Id, NoticeGuildChannelId = Context.Channel.Id, ChangeNowStreamerEmojiToNoticeChannel = false });
        //            await Context.Channel.SendConfirmAsync($"已設定 <#{Context.Channel.Id}> 為直播通知頻道").ConfigureAwait(false);
        //        }
        //        else
        //        {
        //            var guildConfig = db.GuildConfig.First((x) => x.GuildId == Context.Guild.Id);
        //            if (await PromptUserConfirmAsync(new EmbedBuilder().WithOkColor().WithDescription($"已設定 <#{guildConfig.NoticeGuildChannelId}> 為直播通知頻道\r\n" +
        //                $"是否解除?")).ConfigureAwait(false))
        //            {
        //                db.Remove(guildConfig);
        //                await Context.Channel.SendConfirmAsync($"已解除設定").ConfigureAwait(false);
        //            }
        //            else return;
        //        }

        //        await db.SaveChangesAsync();
        //    }
        //}

        //[RequireContext(ContextType.Guild)]
        //[RequireUserPermission(ChannelPermission.ManageMessages)]
        //[Command("SetChangeNowStreamerEmoji")]
        //[Summary("設定現在直播頻道的代表表情至直播通知頻道名稱\r\n" +
        //    "再執行一次則會解除直播通知\r\n\r\n" +
        //    "例:\r\n" +
        //    "`s!scnse`")]
        //[Alias("SCNSE")]
        //public async Task SetChangeNowStreamerEmoji()
        //{
        //    using (var db = new DBContext())
        //    {
        //        if (!db.GuildConfig.Any((x) => x.GuildId == Context.Guild.Id))
        //        {
        //            await Context.Channel.SendConfirmAsync("未設定直播開始時要通知的頻道\r\n" +
        //                "請在要通知的頻道內輸入 `s!snsc` 來開啟直播通知\r\n" +
        //                "(請確認Bot是否有該頻道的讀取與發送訊息權限)").ConfigureAwait(false);
        //            return;
        //        }

        //        var guildConfig = db.GuildConfig.First((x) => x.GuildId == Context.Guild.Id);
        //        var guild = _client.GetGuild(guildConfig.GuildId);
        //        var channel = guild.GetTextChannel(guildConfig.NoticeGuildChannelId);
        //        if (channel == null)
        //        {
        //            await Context.Channel.SendConfirmAsync("直播通知頻道已刪除\r\n" +
        //                "請重新建立一個頻道並執行s!snsc").ConfigureAwait(false); ;
        //            return;
        //        }

        //        if (guild.GetUser(_client.CurrentUser.Id).GetPermissions(channel).ManageChannel)
        //        {
        //            guildConfig.ChangeNowStreamerEmojiToNoticeChannel = !guildConfig.ChangeNowStreamerEmojiToNoticeChannel;
        //            db.GuildConfig.Update(guildConfig);
        //            await db.SaveChangesAsync();
        //            await Context.Channel.SendConfirmAsync("設定現在直播頻道的代表表情功能已" + (guildConfig.ChangeNowStreamerEmojiToNoticeChannel ? "開啟" : "關閉")).ConfigureAwait(false);
        //        }
        //        else
        //        {
        //            await channel.SendConfirmAsync("Bot無 `管理頻道` 權限，無法變更頻道名稱\r\n" +
        //                "請修正權限後再執行一次").ConfigureAwait(false);
        //        }
        //    }
        //}

        [Command("SetBannerChange")]
        [Summary("設定伺服器橫幅使用指定頻道的最新影片(直播)縮圖\r\n" +
            "若未輸入頻道Id則關閉本設定\r\n\r\n" +
            "Bot需要有管理伺服器權限\r\n" +
            "且伺服器需有Boost Lv2才可使用本設定")]
        [Alias("SBC")]
        [RequireBotPermission(GuildPermission.ManageGuild)]
        [RequireUserPermission(GuildPermission.ManageGuild)]
        public async Task SetBannerChange(string channelId = "")
        {
            if (channelId == "")
            {
                using (var db = new DBContext())
                {
                    if (db.BannerChange.Any((x) => x.GuildId == Context.Guild.Id))
                    {
                        var guild = db.BannerChange.First((x) => x.GuildId == Context.Guild.Id);
                        db.BannerChange.Remove(guild);
                        await db.SaveChangesAsync();
                        await Context.Channel.SendConfirmAsync("已移除橫幅設定");
                        return;
                    }
                    else
                    {
                        await Context.Channel.SendConfirmAsync("伺服器並未使用本設定");
                        return;
                    }
                }
            }
            else
            {
                if (!channelId.Contains("UC"))
                {
                    await Context.Channel.SendConfirmAsync("頻道Id錯誤").ConfigureAwait(false);
                    return;
                }

                try
                {
                    channelId = channelId.Substring(channelId.IndexOf("UC"), 24);
                }
                catch
                {
                    await Context.Channel.SendConfirmAsync("頻道Id格式錯誤，需為24字數").ConfigureAwait(false);
                    return;
                }
            }

            if (Context.Guild.PremiumTier < PremiumTier.Tier2)
            {
                await Context.Channel.SendConfirmAsync("本伺服器未達Boost Lv2，不可設定橫幅\r\n" +
                    "故無法設定本功能");
                return;
            }

            using (var db = new DBContext())
            {
                string channelTitle = await GetChannelTitle(channelId);

                if (channelTitle == "")
                {
                    await Context.Channel.SendConfirmAsync($"頻道 {channelId} 不存在").ConfigureAwait(false);
                    return;
                }

                if (db.BannerChange.Any((x) => x.GuildId == Context.Guild.Id))
                {
                    var guild = db.BannerChange.First((x) => x.GuildId == Context.Guild.Id);
                    guild.ChannelId = channelId;
                    db.BannerChange.Update(guild);
                }
                else
                {
                    db.BannerChange.Add(new DataBase.Table.BannerChange() { GuildId = Context.Guild.Id, ChannelId = channelId });
                }

                await db.SaveChangesAsync();
                await Context.Channel.SendConfirmAsync($"已設定伺服器橫幅使用 `{channelTitle}` 的直播縮圖").ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [Command("AddNoticeStreamChannel")]
        [Summary("新增直播開台通知的頻道\r\n" +
            "頻道Id必須為24字數+UC開頭\r\n" +
            "或是完整的Youtube頻道網址\r\n" +
            "\r\n" +
            "輸入holo通知全部`Holo成員`的直播\r\n" +
            "輸入2434通知全部`彩虹社成員`的直播\r\n" +
            "輸入other通知部分`非兩大箱`的直播\r\n" +
            "(可以使用 `s!lcs` 查詢有哪些頻道)\r\n" +
            "輸入all通知全部`Holo + 2434 + 非兩大箱`的直播\r\n" +
            "且會覆蓋所有前面項目的通知設定\r\n" +
            "\r\n" +
            "例:\r\n" +
            "`s!ansc UCdn5BQ06XqgXoAxIhbqw5Rg` 或\r\n" +
            "`s!ansc all` 或 `s!ansc holo`")]
        [Alias("ANSC")]
        public async Task AddNoticeStreamChannel(string channelId = "")
        {
            try
            {
                channelId = channelId.GetChannelId();
            }
            catch (Exception ex)
            {
                await Context.Channel.SendConfirmAsync(ex.Message).ConfigureAwait(false);
                return;
            }

            using (var db = new DBContext())
            {
                if (db.NoticeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == channelId))
                {
                    await Context.Channel.SendConfirmAsync($"{channelId} 已在直播通知清單內").ConfigureAwait(false);
                    return;
                }

                if (channelId == "all")
                {
                    if (db.NoticeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id))
                    {
                        if (await PromptUserConfirmAsync(new EmbedBuilder().WithOkColor().WithDescription("直播通知清單已有需通知的頻道\r\n" +
                            $"是否更改為通知全部頻道的直播?\r\n" +
                            $"注意: 將會把原先設定的直播通知清單重置")).ConfigureAwait(false))
                        {
                            db.NoticeStreamChannel.RemoveRange(Queryable.Where(db.NoticeStreamChannel, (x) => x.GuildId == Context.Guild.Id));
                        }
                        else return;
                    }
                    db.NoticeStreamChannel.Add(new DataBase.Table.NoticeStreamChannel() { GuildId = Context.Guild.Id, ChannelId = Context.Channel.Id, NoticeStreamChannelId = "all" });
                    await Context.Channel.SendConfirmAsync($"將會通知全部的直播").ConfigureAwait(false);
                }
                else if (channelId == "holo" || channelId == "2434" || channelId == "other")
                {
                    if (db.NoticeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == "all"))
                    {
                        if (await PromptUserConfirmAsync(new EmbedBuilder().WithOkColor().WithDescription("已設定為通知全部頻道的直播\r\n" +
                            $"是否更改為僅通知 `{channelId}` 的直播?")))
                        {
                            db.NoticeStreamChannel.Remove(db.NoticeStreamChannel.First((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == "all"));
                            db.NoticeStreamChannel.Add(new DataBase.Table.NoticeStreamChannel() { GuildId = Context.Guild.Id, ChannelId = Context.Channel.Id, NoticeStreamChannelId = channelId });
                            await Context.Channel.SendConfirmAsync($"已將 {channelId} 加入到通知頻道清單內").ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        db.NoticeStreamChannel.Add(new DataBase.Table.NoticeStreamChannel() { GuildId = Context.Guild.Id, ChannelId = Context.Channel.Id, NoticeStreamChannelId = channelId });
                        await Context.Channel.SendConfirmAsync($"已將 {channelId} 加入到通知頻道清單內").ConfigureAwait(false);
                    }
                }
                else
                {
                    string channelTitle = await GetChannelTitle(channelId).ConfigureAwait(false);
                    if (channelTitle == "")
                    {
                        await Context.Channel.SendConfirmAsync($"頻道 {channelId} 不存在").ConfigureAwait(false);
                        return;
                    }

                    if (db.NoticeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == "all"))
                    {
                        if (await PromptUserConfirmAsync(new EmbedBuilder().WithOkColor().WithDescription("已設定為通知全部頻道的直播\r\n" +
                            $"是否更改為僅通知 `{channelTitle}` 的直播?")))
                        {
                            db.NoticeStreamChannel.Remove(db.NoticeStreamChannel.First((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == "all"));
                            db.NoticeStreamChannel.Add(new DataBase.Table.NoticeStreamChannel() { GuildId = Context.Guild.Id, ChannelId = Context.Channel.Id, NoticeStreamChannelId = channelId });
                            await Context.Channel.SendConfirmAsync($"已將 {channelTitle} 加入到通知頻道清單內").ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        db.NoticeStreamChannel.Add(new DataBase.Table.NoticeStreamChannel() { GuildId = Context.Guild.Id, ChannelId = Context.Channel.Id, NoticeStreamChannelId = channelId });
                        await Context.Channel.SendConfirmAsync($"已將 {channelTitle} 加入到通知頻道清單內").ConfigureAwait(false);
                    }
                }

                await db.SaveChangesAsync();
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [Command("RemoveNoticeStreamChannel")]
        [Summary("移除直播開台通知的頻道\r\n" +
            "頻道Id必須為24字數+UC開頭\r\n" +
            "或是完整的Youtube頻道網址\r\n" +
            "\r\n" +
            "輸入holo移除全部`Holo成員`的直播通知\r\n" +
            "輸入2434移除全部`彩虹社成員`的直播通知\r\n" +
            "輸入other移除部分`非兩大箱`的直播通知\r\n" +
            "輸入all移除全部`Holo + 2434 + 非兩大箱`的直播通知\r\n\r\n" +
            "例:\r\n" +
            "`s!rnsc UCdn5BQ06XqgXoAxIhbqw5Rg` 或\r\n" +
            "`s!rnsc all` 或 `s!rnsc holo`")]
        [Alias("RNSC")]
        public async Task RemoveNoticeStreamChannel(string channelId = "")
        {
            try
            {
                channelId = channelId.GetChannelId();
            }
            catch (Exception ex)
            {
                await Context.Channel.SendConfirmAsync(ex.Message).ConfigureAwait(false);
                return;
            }

            using (var db = new DBContext())
            {
                if (!db.NoticeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id))
                {
                    await Context.Channel.SendConfirmAsync("並未設定直播通知...").ConfigureAwait(false);
                    return;
                }

                if (channelId == "all")
                {
                    if (await PromptUserConfirmAsync(new EmbedBuilder().WithOkColor().WithDescription("將移除全部的直播通知\r\n是否繼續?")).ConfigureAwait(false))
                    {
                        db.NoticeStreamChannel.RemoveRange(Queryable.Where(db.NoticeStreamChannel, (x) => x.GuildId == Context.Guild.Id));
                        await Context.Channel.SendConfirmAsync("已全部清除").ConfigureAwait(false);
                        await db.SaveChangesAsync();
                        return;
                    }
                }

                if (!db.NoticeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == channelId))
                {
                    await Context.Channel.SendConfirmAsync($"並未設定`{channelId}`的直播通知...").ConfigureAwait(false);
                    return;
                }
                else
                {
                    if (channelId == "holo" || channelId == "2434" || channelId == "other")
                    {
                        db.NoticeStreamChannel.Remove(db.NoticeStreamChannel.First((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == channelId));
                        await Context.Channel.SendConfirmAsync($"已移除 {channelId}").ConfigureAwait(false);
                    }
                    else if (db.NoticeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == channelId))
                    {
                        string channelTitle = await GetChannelTitle(channelId).ConfigureAwait(false);
                        if (channelTitle == "")
                        {
                            await Context.Channel.SendConfirmAsync($"頻道 {channelId} 不存在").ConfigureAwait(false);
                            return;
                        }

                        db.NoticeStreamChannel.Remove(db.NoticeStreamChannel.First((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == channelId));
                        await Context.Channel.SendConfirmAsync($"已移除 {channelTitle}").ConfigureAwait(false);
                    }

                    await db.SaveChangesAsync();
                }
            }
        }

        [RequireContext(ContextType.Guild)]
        [Command("ListNoticeStreamChannel")]
        [Summary("顯示現在已加入通知清單的直播頻道\r\n\r\n" +
            "例:\r\n" +
            "`s!lnsc`")]
        [Alias("LNSC")]
        public async Task ListNoticeStreamChannel()
        {
            using (var db = new DBContext())
            {
                var list = Queryable.Where(db.NoticeStreamChannel, (x) => x.GuildId == Context.Guild.Id)
                    .Select((x) => new KeyValuePair<string, ulong>(x.NoticeStreamChannelId, x.ChannelId)).ToList();
                if (list.Count() == 0) { await Context.Channel.SendConfirmAsync("直播通知清單為空").ConfigureAwait(false); return; }

                var ytChannelList = list.Select(x => x.Key).Where((x) => x.StartsWith("UC")).ToList();
                var channelTitleList = list.Where((x) => !x.Key.StartsWith("UC")).Select((x) => $"{x.Key} => <#{x.Value}>").ToList();

                if (ytChannelList.Count > 0)
                {
                    for (int i = 0; i < ytChannelList.Count; i += 50)
                    {
                        try
                        {
                            var channel = _service.yt.Channels.List("snippet");
                            channel.Id = string.Join(",", ytChannelList.Skip(i).Take(50));
                            var response = await channel.ExecuteAsync().ConfigureAwait(false);
                            channelTitleList.AddRange(response.Items.Select((x) => $"{x.Id} / {x.Snippet.Title} => <#{list.Find((x2) => x2.Key == x.Id).Value}>"));
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex.Message + "\r\n" + ex.StackTrace);
                        }
                    }
                }

                await Context.SendPaginatedConfirmAsync(0, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("直播記錄清單")
                        .WithDescription(string.Join('\n', channelTitleList.Skip(page * 20).Take(20)))
                        .WithFooter($"{Math.Min(channelTitleList.Count, (page + 1) * 20)} / {channelTitleList.Count}個頻道");
                }, channelTitleList.Count, 20, false);
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages | GuildPermission.MentionEveryone)]
        [RequireBotPermission(GuildPermission.MentionEveryone)]
        [Command("SetNoticeMessage")]
        [Summary("設定通知訊息\r\n" +
            "不輸入通知訊息的話則會關閉該類型的通知\r\n" +
            "需先新增直播通知後才可設定通知訊息(`s!h ansc`)\r\n\r\n" +
            "NoticeType(通知類型)說明:\r\n" +
            "NewStream: 新待機所\r\n" +
            "NewVideo: 新影片\r\n" +
            "Start: 開始直播\r\n" +
            "End: 結束直播\r\n" +
            "ChangeTime: 變更直播時間\r\n" +
            "Delete: 刪除直播\r\n\r\n" +
            "(考慮到有伺服器需Ping特定用戶組的情況，故Bot需提及所有身分組權限)\r\n" +
            "(建議在私人頻道中設定以免Ping到用戶組造成不必要的誤會)\r\n\r\n" +
            "例:\r\n" +
            "`s!snm UCXRlIK3Cw_TJIQC5kSJJQMg start @通知用的用戶組 阿床開台了`\r\n" +
            "`s!snm holo newstream @某人 新待機所建立`\r\n" +
            "`s!snm UCXRlIK3Cw_TJIQC5kSJJQMg end`")]
        [Alias("SNM")]
        public async Task SetNoticeMessage([Summary("頻道Id")]string channelId,[Summary("通知類型")] Service.StreamService.NoticeType noticeType, [Summary("通知訊息")][Remainder] string message = "")
        {
            try
            {
                channelId = channelId.GetChannelId();
            }
            catch (Exception ex)
            {
                await Context.Channel.SendConfirmAsync(ex.Message).ConfigureAwait(false);
                return;
            }

            using (var db = new DBContext())
            {
                if (db.NoticeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == channelId))
                {
                    var noticeStreamChannel = db.NoticeStreamChannel.First((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == channelId);
                    string noticeTypeString = "";

                    switch (noticeType)
                    {
                        case Service.StreamService.NoticeType.NewStream:
                            noticeStreamChannel.NewStreamMessage = message;
                            noticeTypeString = "新待機所";
                            break;
                        case Service.StreamService.NoticeType.NewVideo:
                            noticeStreamChannel.NewVideoMessage = message;
                            noticeTypeString = "新影片";
                            break;
                        case Service.StreamService.NoticeType.Start:
                            noticeStreamChannel.StratMessage = message;
                            noticeTypeString = "開始直播";
                            break;
                        case Service.StreamService.NoticeType.End:
                            noticeStreamChannel.EndMessage = message;
                            noticeTypeString = "結束直播";
                            break;
                        case Service.StreamService.NoticeType.ChangeTime:
                            noticeStreamChannel.ChangeTimeMessage = message;
                            noticeTypeString = "變更直播時間";
                            break;
                        case Service.StreamService.NoticeType.Delete:
                            noticeStreamChannel.DeleteMessage = message;
                            noticeTypeString = "刪除直播";
                            break;
                    }

                    db.NoticeStreamChannel.Update(noticeStreamChannel);
                    await db.SaveChangesAsync();

                    if (message != "") await Context.Channel.SendConfirmAsync($"已設定 {channelId} 的 {noticeTypeString} 通知訊息為:\r\n{message}").ConfigureAwait(false);
                    else await Context.Channel.SendConfirmAsync($"已取消 {channelId} 的 {noticeTypeString} 通知").ConfigureAwait(false);
                }
                else
                {
                    await Context.Channel.SendConfirmAsync($"並未設定 {channelId} 的直播通知\r\n請先使用 `s!ansc {channelId}` 新增直播後再設定通知訊息").ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.Guild)]
        [Command("ListNoticeMessage")]
        [Summary("列出已設定的通知訊息\r\n\r\n" +
            "例:\r\n" +
            "`s!lnm`")]
        [Alias("LNM")]
        public async Task ListNoticeMessage(int page = 0)
        {
            using (var db = new DBContext())
            {
                if (db.NoticeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id))
                {
                    var noticeStreamChannels = db.NoticeStreamChannel.ToList().Where((x) => x.GuildId == Context.Guild.Id);
                    Dictionary<string, string> dic = new Dictionary<string, string>();

                    foreach (var item in noticeStreamChannels)
                    {
                        var channelTitle = item.NoticeStreamChannelId;
                        if (channelTitle.StartsWith("UC")) channelTitle = (await GetChannelTitle(channelTitle).ConfigureAwait(false)) + $" ({item.NoticeStreamChannelId})";

                        dic.Add(channelTitle,
                            $"新待機所: {item.NewStreamMessage}\r\n" +
                            $"新影片: {item.NewVideoMessage}\r\n" +
                            $"開始直播: {item.StratMessage}\r\n" +
                            $"結束直播: {item.EndMessage}\r\n" +
                            $"變更直播時間: {item.ChangeTimeMessage}\r\n" +
                            $"刪除直播: {item.DeleteMessage}");
                    }

                    await Context.SendPaginatedConfirmAsync(page, (page) =>
                    {
                        EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor().WithTitle("通知訊息清單")
                            .WithDescription("如果沒訊息的話就代表沒設定\r\n不用擔心會Tag到用戶組，Embed不會有Ping的反應");

                        foreach (var item in dic.Skip(page * 4).Take(4))
                        {
                            embedBuilder.AddField(item.Key, item.Value);
                        }

                        return embedBuilder;
                    }, dic.Count, 4);
                }
                else
                {
                    await Context.Channel.SendConfirmAsync($"並未設定直播通知\r\n請先使用 `s!h ansc` 查看說明並新增直播通知").ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireOwner]
        [Command("GetVideoInfo")]
        [Summary("從資料庫取得已刪檔的直播資訊\r\n\r\n" +
            "例:\r\n" +
            "`s!gvi GzWDcutkMQw`")]
        [Alias("gvi")]
        public async Task GetVideoInfo(string videoId = "")
        {
            videoId = videoId.Trim();

            if (string.IsNullOrWhiteSpace(videoId))
            {
                await ReplyAsync("VideoId空白").ConfigureAwait(false);
                return;
            }

            using (var db = new DBContext())
            {
                if (!db.HasStreamVideoByVideoId(videoId))
                {
                    await ReplyAsync($"不存在 {videoId} 的影片").ConfigureAwait(false);
                    return;
                }

                var video = db.GetStreamVideoByVideoId(videoId);
                EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor()
                    .WithTitle(video.VideoTitle)
                    .WithUrl($"https://www.youtube.com/watch?v={videoId}")
                    .WithDescription(Format.Url(video.ChannelTitle, $"https://www.youtube.com/channel/{video.ChannelId}"))
                    .AddField("排定開台時間", video.ScheduledStartTime, true);

                await ReplyAsync(embed: embedBuilder.Build()).ConfigureAwait(false);
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
                Log.Error(ex.Message + "\r\n" + ex.StackTrace);
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
                return response.Items.Select((x) => $"{x.Snippet.Title} / {x.Id}").ToList();
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message + "\r\n" + ex.StackTrace);
                return null;
            }
        }
    }
}