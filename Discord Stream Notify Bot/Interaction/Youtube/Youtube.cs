using Discord.Interactions;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction.Attribute;
using Discord_Stream_Notify_Bot.SharedService.Youtube;
using Microsoft.EntityFrameworkCore;
using Video = Google.Apis.YouTube.v3.Data.Video;

namespace Discord_Stream_Notify_Bot.Interaction.Youtube
{
    [Group("youtube", "YouTube 通知設定")]
    public class Youtube : TopLevelModule<YoutubeStreamService>
    {
        private readonly DiscordSocketClient _client;

        public class GuildNoticeYoutubeChannelIdAutocompleteHandler : AutocompleteHandler
        {
            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
            {
                return await Task.Run(() =>
                {
                    using var db = DataBase.MainDbContext.GetDbContext();
                    if (!db.NoticeYoutubeStreamChannel.Any((x) => x.GuildId == context.Guild.Id))
                        return AutocompletionResult.FromSuccess();

                    var channelIdList = db.NoticeYoutubeStreamChannel.Where((x) => x.GuildId == context.Guild.Id).Select((x) => new KeyValuePair<string, string>(db.GetYoutubeChannelTitleByChannelId(x.YouTubeChannelId), x.YouTubeChannelId));

                    var channelIdList2 = new Dictionary<string, string>();
                    try
                    {
                        string value = autocompleteInteraction.Data.Current.Value.ToString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            foreach (var item in channelIdList)
                            {
                                if (item.Key.Contains(value, StringComparison.CurrentCultureIgnoreCase) || item.Value.Contains(value, StringComparison.CurrentCultureIgnoreCase))
                                {
                                    channelIdList2.Add(item.Key, item.Value);
                                }
                            }
                        }
                        else
                        {
                            foreach (var item in channelIdList)
                            {
                                channelIdList2.Add(item.Key, item.Value);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"GuildNoticeYoutubeChannelIdAutocompleteHandler - {ex}");
                    }

                    List<AutocompleteResult> results = new();
                    foreach (var item in channelIdList2)
                    {
                        results.Add(new AutocompleteResult(item.Key, item.Value));
                    }

                    return AutocompletionResult.FromSuccess(results.Take(25));
                });
            }
        }

        public Youtube(DiscordSocketClient client)
        {
            _client = client;
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [DefaultMemberPermissions(GuildPermission.ManageMessages)]
        [SlashCommand("list-record-channel", "顯示直播記錄頻道")]
        public async Task ListRecordChannel([Summary("頁數")] int page = 0)
        {
            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                if (db.RecordYoutubeChannel.Any())
                {
                    var list = new List<string>();

                    foreach (var item in db.RecordYoutubeChannel.ToList().Chunk(50))
                    {
                        list.AddRange(await _service.GetChannelTitle(item.Select((x) => x.YoutubeChannelId), true));
                    }

                    list.Sort();
                    await Context.SendPaginatedConfirmAsync(page, page =>
                    {
                        return new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("直播記錄清單")
                            .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20)))
                            .WithFooter($"{Math.Min(list.Count, (page + 1) * 20)} / {list.Count} 個頻道");
                    }, list.Count, 20, false);
                }
                else await Context.Interaction.SendErrorAsync($"直播記錄清單中沒有任何頻道").ConfigureAwait(false);
            }
        }

        [SlashCommand("now-streaming", "取得現在直播的成員")]
        public async Task NowStreaming(YoutubeStreamService.NowStreamingHost host)
        {
            var embed = await _service.GetNowStreamingChannel(host).ConfigureAwait(false);

            if (embed == null) await Context.Interaction.SendErrorAsync("無法取得直播清單").ConfigureAwait(false);
            else await Context.Interaction.RespondAsync(embed: embed).ConfigureAwait(false);
        }

        [SlashCommand("coming-soon-stream", "顯示接下來直播的清單")]
        public async Task ComingSoonStream([Summary("頁數")] int page = 0)
        {
            try
            {
                List<Video> result = new List<Video>();

                for (int i = 0; i < _service.Reminders.Count; i += 50)
                {
                    var yt = _service.YouTubeService.Videos.List("snippet,liveStreamingDetails");
                    yt.Id = string.Join(',', _service.Reminders.Keys.Skip(i).Take(50));
                    result.AddRange((await yt.ExecuteAsync().ConfigureAwait(false)).Items);
                }

                using (var db = DataBase.MainDbContext.GetDbContext())
                {
                    result = result.OrderBy((x) => x.LiveStreamingDetails.ScheduledStartTimeDateTimeOffset).ToList();
                    await Context.SendPaginatedConfirmAsync(page, (act) =>
                    {
                        return new EmbedBuilder().WithOkColor()
                        .WithTitle("接下來開台的清單")
                        .WithDescription(string.Join("\n\n",
                           result.Skip(act * 7).Take(7)
                           .Select((x) => $"{Format.Url(x.Snippet.Title, $"https://www.youtube.com/watch?v={x.Id}")}" +
                           $"\n{Format.Url(x.Snippet.ChannelTitle, $"https://www.youtube.com/channel/{x.Snippet.ChannelId}")}" +
                           $"\n直播時間: {DateTime.Parse(x.LiveStreamingDetails.ScheduledStartTimeRaw)}" +
                           "\n是否在直播錄影清單內: " + (db.RecordYoutubeChannel.Any((x2) => x2.YoutubeChannelId.Trim() == x.Snippet.ChannelId) ? "是" : "否"))));
                    }, result.Count, 7).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message + "r\n" + ex.StackTrace);
                await Context.Interaction.SendErrorAsync("不明的錯誤，請向 Bot 擁有者回報", true);
            }
        }

        [SlashCommand("get-member-only-playlist", "將頻道網址轉換成會員限定清單網址")]
        public async Task GetMemberOnlyPlayListAsync([Summary("頻道網址")] string channelUrl)
        {
            await DeferAsync(true);

            try
            {
                string channelId = "";
                try
                {
                    channelId = await _service.GetChannelIdAsync(channelUrl).ConfigureAwait(false);
                    await Context.Interaction.SendConfirmAsync($"https://www.youtube.com/playlist?list={channelId.Replace("UC", "UUMO")}", true, true);
                }
                catch (FormatException fex)
                {
                    await Context.Interaction.SendErrorAsync(fex.Message, true);
                    return;
                }
                catch (ArgumentNullException)
                {
                    await Context.Interaction.SendErrorAsync("網址不可空白", true);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message + "r\n" + ex.StackTrace);
                await Context.Interaction.SendErrorAsync("不明的錯誤，請向 Bot 擁有者回報", true);
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireBotPermission(GuildPermission.ManageGuild)]
        [RequireUserPermission(GuildPermission.ManageGuild)]
        [DefaultMemberPermissions(GuildPermission.ManageGuild)]
        [CommandSummary("設定伺服器橫幅使用指定頻道的最新影片(直播)縮圖\n" +
            "若未輸入頻道網址則關閉本設定\n\n" +
            "Bot需要有管理伺服器權限\n" +
            "且伺服器需有 Boost Lv2 才可使用本設定\n" +
            "(此功能依賴直播通知，請確保設定的頻道在兩大箱或是爬蟲清單內)")]
        [CommandExample("https://www.youtube.com/@998rrr")]
        [SlashCommand("set-banner-change", "設定伺服器橫幅使用指定頻道的最新影片(直播)縮圖")]
        public async Task SetBannerChange([Summary("頻道網址")] string channelUrl = "")
        {
            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                if (channelUrl == "")
                {
                    if (db.BannerChange.Any((x) => x.GuildId == Context.Guild.Id))
                    {
                        var guild = db.BannerChange.First((x) => x.GuildId == Context.Guild.Id);
                        db.BannerChange.Remove(guild);
                        db.SaveChanges();
                        await Context.Interaction.SendConfirmAsync("已移除橫幅設定").ConfigureAwait(false);
                    }
                    else
                    {
                        await Context.Interaction.SendErrorAsync("伺服器並未使用本設定").ConfigureAwait(false);
                    }
                }
                else
                {
                    if (Context.Guild.PremiumTier < PremiumTier.Tier2)
                    {
                        await Context.Interaction.SendErrorAsync("本伺服器未達 Boost Lv2，不可設定橫幅\n" +
                            "故無法設定本功能").ConfigureAwait(false);
                        return;
                    }

                    await DeferAsync().ConfigureAwait(false);

                    string channelId = "";
                    try
                    {
                        channelId = await _service.GetChannelIdAsync(channelUrl).ConfigureAwait(false);
                    }
                    catch (FormatException fex)
                    {
                        await Context.Interaction.SendErrorAsync(fex.Message, true).ConfigureAwait(false);
                        return;
                    }
                    catch (ArgumentNullException)
                    {
                        await Context.Interaction.SendErrorAsync("網址不可空白", true).ConfigureAwait(false);
                        return;
                    }

                    string channelTitle = await _service.GetChannelTitle(channelId);
                    if (channelTitle == "")
                    {
                        await Context.Interaction.SendErrorAsync($"頻道 `{channelId}` 不存在", true).ConfigureAwait(false);
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
                        db.BannerChange.Add(new BannerChange() { GuildId = Context.Guild.Id, ChannelId = channelId });
                    }

                    await Context.Interaction.SendConfirmAsync($"已設定伺服器橫幅使用 `{channelTitle}` 的直播縮圖", true).ConfigureAwait(false);
                    db.SaveChanges();
                }
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [DefaultMemberPermissions(GuildPermission.ManageMessages)]
        [CommandSummary("新增直播開台通知的頻道\n" +
            "輸入 `holo` 通知全部 `Holo成員` 的直播\n" +
            "輸入 `2434` 通知全部 `彩虹社成員` 的直播\n" +
            "(僅JP、EN 跟 VR 的成員歸類在此選項內，如需其他成員建議先用 `/youtube-spider add` 設定)\n" +
            "輸入 `other` 通知部分 `非兩大箱` 的直播\n" +
            "(可以使用 `/youtube-spider list` 查詢有哪些頻道)")]
        [CommandExample("https://www.youtube.com/@998rrr", "other", "2434")]
        [SlashCommand("add", "新增YouTube直播開台通知的頻道")]
        public async Task AddChannel([Summary("頻道網址")] string channelUrl,
            [Summary("發送通知的頻道"), ChannelTypes(ChannelType.Text, ChannelType.News)] IChannel channel)
        {
            await DeferAsync(true).ConfigureAwait(false);

            var textChannel = channel as IGuildChannel;
            var permissions = Context.Guild.GetUser(_client.CurrentUser.Id).GetPermissions(textChannel);
            if (!permissions.ViewChannel || !permissions.SendMessages)
            {
                await Context.Interaction.SendErrorAsync($"我在 `{textChannel}` 沒有 `讀取&編輯頻道` 的權限，請給予權限後再次執行本指令", true).ConfigureAwait(false);
                return;
            }

            if (!permissions.EmbedLinks)
            {
                await Context.Interaction.SendErrorAsync($"我在 `{textChannel}` 沒有 `嵌入連結` 的權限，請給予權限後再次執行本指令", true).ConfigureAwait(false);
                return;
            }

            string channelId = "";
            try
            {
                channelId = await _service.GetChannelIdAsync(channelUrl);
            }
            catch (FormatException fex)
            {
                await Context.Interaction.SendErrorAsync(fex.Message, true).ConfigureAwait(false);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Interaction.SendErrorAsync("網址不可空白", true).ConfigureAwait(false);
                return;
            }

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                await CheckIsFirstSetNoticeAndSendWarningMessageAsync(db);

                var noticeYoutubeStreamChannel = db.NoticeYoutubeStreamChannel.FirstOrDefault((x) => x.GuildId == Context.Guild.Id && x.YouTubeChannelId == channelId);
                if (noticeYoutubeStreamChannel != null)
                {
                    if (await PromptUserConfirmAsync($"`{channelId}` 已在通知清單內，是否覆蓋設定?").ConfigureAwait(false))
                    {
                        noticeYoutubeStreamChannel.DiscordNoticeStreamChannelId = textChannel.Id;
                        db.NoticeYoutubeStreamChannel.Update(noticeYoutubeStreamChannel);
                        db.SaveChanges();
                        await Context.Interaction.SendConfirmAsync($"已將 `{channelId}` 的 __直播__ 通知頻道變更至: {textChannel}", true, true).ConfigureAwait(false);
                    }
                    return;
                }

                if (channelId == "holo" || channelId == "2434" || channelId == "other")
                {
                    db.NoticeYoutubeStreamChannel.Add(new NoticeYoutubeStreamChannel()
                    {
                        GuildId = Context.Guild.Id,
                        DiscordNoticeStreamChannelId = textChannel.Id,
                        DiscordNoticeVideoChannelId = textChannel.Id,
                        YouTubeChannelId = channelId
                    });
                    await Context.Interaction.SendConfirmAsync($"已將 `{channelId}` 加入到直播通知頻道清單內", true, true).ConfigureAwait(false);
                }
                else
                {
                    string channelTitle = await _service.GetChannelTitle(channelId);
                    if (channelTitle == "")
                    {
                        await Context.Interaction.SendErrorAsync($"頻道 `{channelId}` 不存在", true).ConfigureAwait(false);
                        return;
                    }

                    string addString = "\n如需調整影片上傳通知頻道請使用 `/youtube set-video-notice-channel`";
                    if (!db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId) && !Extensions.IsChannelInDb(channelId))
                        addString += $"\n\n(注意: 該頻道未加入爬蟲清單\n如長時間無通知請使用 `/help get-command-help youtube-spider add` 查看說明並加入爬蟲)";

                    db.NoticeYoutubeStreamChannel.Add(new NoticeYoutubeStreamChannel()
                    {
                        GuildId = Context.Guild.Id,
                        DiscordNoticeStreamChannelId = textChannel.Id,
                        DiscordNoticeVideoChannelId = textChannel.Id,
                        YouTubeChannelId = channelId
                    });
                    await Context.Interaction.SendConfirmAsync($"已將 `{channelTitle}` 加入到直播通知頻道清單內{addString}", true, true).ConfigureAwait(false);
                }

                db.SaveChanges();
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [DefaultMemberPermissions(GuildPermission.ManageMessages)]
        [CommandSummary("移除通知頻道\n" +
            "輸入 `holo` 移除全部 `Holo成員` 的直播通知\n" +
            "輸入 `2434` 移除全部 `彩虹社成員` 的直播通知\n" +
            "輸入 `other` 移除部分 `非兩大箱` 的直播通知\n" +
            "輸入 `all` 移除全部的直播通知")]
        [CommandExample("https://www.youtube.com/@998rrr", "all", "2434")]
        [SlashCommand("remove", "移除 YouTube 直播開台通知的頻道")]
        public async Task RemoveChannel([Summary("頻道名稱"), Autocomplete(typeof(GuildNoticeYoutubeChannelIdAutocompleteHandler))] string channelName)
        {
            await DeferAsync(true).ConfigureAwait(false);

            string channelId = "";
            try
            {
                channelId = await _service.GetChannelIdAsync(channelName).ConfigureAwait(false);
            }
            catch (FormatException fex)
            {
                await Context.Interaction.SendErrorAsync(fex.Message, true);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Interaction.SendErrorAsync("網址不可空白", true);
                return;
            }

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                if (!db.NoticeYoutubeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id))
                {
                    await Context.Interaction.SendErrorAsync("並未設定 YouTube 通知...", true).ConfigureAwait(false);
                    return;
                }

                if (channelId == "all")
                {
                    if (await PromptUserConfirmAsync("將移除全部的 YouTube 通知，是否繼續?").ConfigureAwait(false))
                    {
                        db.NoticeYoutubeStreamChannel.RemoveRange(Queryable.Where(db.NoticeYoutubeStreamChannel, (x) => x.GuildId == Context.Guild.Id));
                        await Context.Interaction.SendConfirmAsync("已全部清除", true, true).ConfigureAwait(false);
                        db.SaveChanges();
                        return;
                    }
                    else return;
                }

                if (!db.NoticeYoutubeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id && x.YouTubeChannelId == channelId))
                {
                    await Context.Interaction.SendErrorAsync($"並未設定 `{channelId}` 的直播通知...", true).ConfigureAwait(false);
                }
                else
                {
                    db.NoticeYoutubeStreamChannel.Remove(db.NoticeYoutubeStreamChannel.First((x) => x.GuildId == Context.Guild.Id && x.YouTubeChannelId == channelId));
                    await Context.Interaction.SendConfirmAsync($"已移除 `{channelId}`", true, true).ConfigureAwait(false);

                    db.SaveChanges();
                }
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [DefaultMemberPermissions(GuildPermission.ManageMessages)]
        [SlashCommand("set-video-notice-channel", "設定 YouTube 影片上傳通知頻道")]
        public async Task SetVideoNoticeChannel([Summary("頻道網址"), Autocomplete(typeof(GuildNoticeYoutubeChannelIdAutocompleteHandler))] string channelName,
             [Summary("發送通知的頻道"), ChannelTypes(ChannelType.Text, ChannelType.News)] IChannel channel)
        {
            await DeferAsync(true).ConfigureAwait(false);

            var textChannel = channel as IGuildChannel;
            var permissions = Context.Guild.GetUser(_client.CurrentUser.Id).GetPermissions(textChannel);
            if (!permissions.ViewChannel || !permissions.SendMessages)
            {
                await Context.Interaction.SendErrorAsync($"我在 `{textChannel}` 沒有 `讀取&編輯頻道` 的權限，請給予權限後再次執行本指令", true).ConfigureAwait(false);
                return;
            }

            if (!permissions.EmbedLinks)
            {
                await Context.Interaction.SendErrorAsync($"我在 `{textChannel}` 沒有 `嵌入連結` 的權限，請給予權限後再次執行本指令", true).ConfigureAwait(false);
                return;
            }

            string channelId = "";
            try
            {
                channelId = await _service.GetChannelIdAsync(channelName);
            }
            catch (FormatException fex)
            {
                await Context.Interaction.SendErrorAsync(fex.Message, true).ConfigureAwait(false);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Interaction.SendErrorAsync("網址不可空白", true).ConfigureAwait(false);
                return;
            }

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var noticeYoutubeStreamChannel = db.NoticeYoutubeStreamChannel.FirstOrDefault((x) => x.GuildId == Context.Guild.Id && x.YouTubeChannelId == channelId);
                if (noticeYoutubeStreamChannel != null)
                {
                    noticeYoutubeStreamChannel.DiscordNoticeVideoChannelId = textChannel.Id;
                    db.NoticeYoutubeStreamChannel.Update(noticeYoutubeStreamChannel);
                    db.SaveChanges();
                    await Context.Interaction.SendConfirmAsync($"已將 `{channelId}` 的 __影片__ 通知頻道變更至: {textChannel}", true, true).ConfigureAwait(false);
                }
                else
                {
                    await Context.Interaction.SendErrorAsync($"找不到 `{channelId}` 的通知設定" +
                        $"請先使用 `/youtube add {channelId}` 新增直播後再設定通知訊息" + 
                        $"若已新增，請向 Bot 擁有者詢問", true).ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [DefaultMemberPermissions(GuildPermission.ManageMessages)]
        [SlashCommand("list", "顯示現在已加入通知清單的 YouTube 頻道")]
        public async Task ListChannel([Summary("頁數")] int page = 0)
        {
            await DeferAsync();

            try
            {
                using (var db = DataBase.MainDbContext.GetDbContext())
                {
                    if (!db.NoticeYoutubeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id))
                    {
                        await Context.Interaction.SendErrorAsync("YouTube 通知清單為空", true).ConfigureAwait(false);
                        return;
                    }

                    var ytChannelList = db.NoticeYoutubeStreamChannel
                        .Where((x) => x.GuildId == Context.Guild.Id && x.YouTubeChannelId.StartsWith("UC"))
                        .Select((x) => $"{db.GetYoutubeChannelTitleByChannelId(x.YouTubeChannelId)} | 直播通知: <#{x.DiscordNoticeStreamChannelId}> | 影片通知: <#{x.DiscordNoticeVideoChannelId}>")
                        .ToList();

                    var notYTChannelNoticeList = db.NoticeYoutubeStreamChannel
                        .Where((x) => x.GuildId == Context.Guild.Id && !x.YouTubeChannelId.StartsWith("UC"))
                        .Select((x) => $"{db.GetYoutubeChannelTitleByChannelId(x.YouTubeChannelId)} | 直播通知: <#{x.DiscordNoticeStreamChannelId}> | 影片通知: <#{x.DiscordNoticeVideoChannelId}>")
                        .ToList();

                    ytChannelList.AddRange(notYTChannelNoticeList);

                    await Context.SendPaginatedConfirmAsync(page, page =>
                    {
                        return new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("YouTube 通知清單")
                            .WithDescription(string.Join('\n', ytChannelList.Skip(page * 20).Take(20)))
                            .WithFooter($"{Math.Min(ytChannelList.Count, (page + 1) * 20)} / {ytChannelList.Count} 個頻道");
                    }, ytChannelList.Count, 20, isFollowup: true);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"YouTube ListChannel Error: {Context.Guild.Id}");
                await Context.Interaction.SendErrorAsync("未知的錯誤，請向 Bot 擁有者回報");
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages | GuildPermission.MentionEveryone)]
        [DefaultMemberPermissions(GuildPermission.ManageMessages | GuildPermission.MentionEveryone)]
        [RequireBotPermission(GuildPermission.MentionEveryone)]
        [CommandSummary("設定通知訊息\n" +
            "不輸入通知訊息的話則會關閉該類型的通知\n" +
            "若輸入 `-` 則可以關閉該通知類型\n" +
            "需先新增直播通知後才可設定通知訊息 (`/help get-command-help youtube add`)\n\n" +
            "(考慮到有伺服器需 Ping 特定用戶組的情況，故 Bot 需提及所有身分組權限)")]
        [CommandExample("998rrr 開始直播\\首播 @通知用的用戶組 玖玖巴開台啦",
            "holo 新待機室 @某人 新待機所建立",
            "UCUKD-uaobj9jiqB-VXt71mA 新上傳影片 -",
            "UUMOs5FNYPHeZz5f7N1BDExxfg 結束直播\\首播")]
        [SlashCommand("set-message", "設定 YouTube 通知訊息")]
        public async Task SetMessage([Summary("頻道網址"), Autocomplete(typeof(GuildNoticeYoutubeChannelIdAutocompleteHandler))] string channelUrl,
            [Summary("通知類型")] YoutubeStreamService.NoticeType noticeType,
            [Summary("通知訊息")] string message = "")
        {
            await DeferAsync(true).ConfigureAwait(false);

            string channelId = "";
            try
            {
                channelId = await _service.GetChannelIdAsync(channelUrl).ConfigureAwait(false);
            }
            catch (FormatException fex)
            {
                await Context.Interaction.SendErrorAsync(fex.Message, true);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Interaction.SendErrorAsync("網址不可空白", true);
                return;
            }

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var channelTitle = db.GetYoutubeChannelTitleByChannelId(channelId);
                var noticeStreamChannel = db.NoticeYoutubeStreamChannel.FirstOrDefault((x) => x.GuildId == Context.Guild.Id && x.YouTubeChannelId == channelId);
                if (noticeStreamChannel == null)
                {
                    await Context.Interaction.SendErrorAsync($"並未設定 `{channelTitle}` 的直播通知\n" +
                        $"請先使用 `/youtube add {channelId}` 新增直播後再設定通知訊息", true).ConfigureAwait(false);
                }
                else
                {
                    string noticeTypeString = "", result = "";

                    message = message.Trim();
                    switch (noticeType)
                    {
                        case YoutubeStreamService.NoticeType.NewStream:
                            noticeStreamChannel.NewStreamMessage = message;
                            noticeTypeString = "新待機所";
                            break;
                        case YoutubeStreamService.NoticeType.NewVideo:
                            noticeStreamChannel.NewVideoMessage = message;
                            noticeTypeString = "新上傳影片";
                            break;
                        case YoutubeStreamService.NoticeType.Start:
                            noticeStreamChannel.StratMessage = message;
                            noticeTypeString = "開始直播\\首播";
                            break;
                        case YoutubeStreamService.NoticeType.End:
                            noticeStreamChannel.EndMessage = message;
                            noticeTypeString = "結束直播\\首播";
                            break;
                        case YoutubeStreamService.NoticeType.ChangeTime:
                            noticeStreamChannel.ChangeTimeMessage = message;
                            noticeTypeString = "變更直播時間";
                            break;
                        case YoutubeStreamService.NoticeType.Delete:
                            noticeStreamChannel.DeleteMessage = message;
                            noticeTypeString = "已刪除或私人化直播";
                            break;
                    }

                    db.NoticeYoutubeStreamChannel.Update(noticeStreamChannel);
                    db.SaveChanges();

                    if (message == "-")
                    {
                        result = $"已關閉 `{channelTitle}` 的 `{noticeTypeString}` 通知";
                    }
                    else if (message != "")
                    {
                        result = $"已設定 `{channelTitle}` 的 `{noticeTypeString}` 通知訊息為:\n" +
                                $"{message}";

                        if (noticeType == YoutubeStreamService.NoticeType.End && !db.RecordYoutubeChannel.AsNoTracking().Any((x) => x.YoutubeChannelId == channelId))
                        {
                            result += $"\n\n(注意: 該頻道目前不會有結束直播通知)";
                        }
                        else if (!db.YoutubeChannelSpider.FirstOrDefault((x) => x.IsTrustedChannel)?.IsTrustedChannel ?? false &&
                            (channelId != "holo" && channelId != "2434" && channelId != "other"))
                        {
                            result += $"\n\n(注意: 該頻道目前僅會有影片上傳通知)";
                        }
                    }
                    else
                    {
                        result = $"已清除 `{channelTitle}` 的 `{noticeTypeString}` 通知訊息";
                    }

                    await Context.Interaction.SendConfirmAsync(result, true, true).ConfigureAwait(false);
                }
            }
        }

        string GetCurrectMessage(string message)
            => message == "-" ? "(已關閉本類別的通知)" : message;

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [DefaultMemberPermissions(GuildPermission.ManageMessages)]
        [SlashCommand("list-message", "列出已設定的通知訊息")]
        public async Task ListMessage([Summary("頁數")] int page = 0)
        {
            try
            {
                using (var db = DataBase.MainDbContext.GetDbContext())
                {
                    if (db.NoticeYoutubeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id))
                    {
                        var noticeStreamChannels = db.NoticeYoutubeStreamChannel.Where((x) => x.GuildId == Context.Guild.Id);
                        Dictionary<string, string> dic = new Dictionary<string, string>();

                        foreach (var item in noticeStreamChannels)
                        {
                            var channelTitle = item.YouTubeChannelId;
                            if (channelTitle.StartsWith("UC"))
                            {
                                var ytChannelTitle = db.GetYoutubeChannelTitleByChannelId(channelTitle);
                                channelTitle = (ytChannelTitle.StartsWith("UC") ? "__找不到頻道名稱__" : ytChannelTitle) + $" ({item.YouTubeChannelId})";
                            }

                            dic.Add(channelTitle,
                                $"新待機所: {GetCurrectMessage(item.NewStreamMessage)}\n" +
                                $"新影片: {GetCurrectMessage(item.NewVideoMessage)}\n" +
                                $"開始直播: {GetCurrectMessage(item.StratMessage)}\n" +
                                $"結束直播: {GetCurrectMessage(item.EndMessage)}\n" +
                                $"變更直播時間: {GetCurrectMessage(item.ChangeTimeMessage)}\n" +
                                $"刪除直播: {GetCurrectMessage(item.DeleteMessage)}");
                        }

                        await Context.SendPaginatedConfirmAsync(page, (page) =>
                        {
                            EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor().WithTitle("YouTube 通知訊息清單")
                                .WithDescription("如果沒訊息的話就代表沒設定\n不用擔心會Tag到用戶組，Embed 不會有 Ping 的反應");

                            foreach (var item in dic.Skip(page * 4).Take(4))
                            {
                                embedBuilder.AddField(item.Key, item.Value);
                            }

                            return embedBuilder;
                        }, dic.Count, 4);
                    }
                    else
                    {
                        await Context.Interaction.SendErrorAsync($"並未設定 YouTube 通知\n" +
                            $"請先使用 `/help get-command-help youtube add` 查看說明並新增直播通知").ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "YouTube ListMessage");
                await Context.Interaction.SendErrorAsync("錯誤，請向 Bot 擁有者詢問");
            }
        }
    }
}