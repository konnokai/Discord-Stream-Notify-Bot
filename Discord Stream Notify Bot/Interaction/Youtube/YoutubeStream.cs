using Discord.Interactions;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction.Attribute;
using Video = Google.Apis.YouTube.v3.Data.Video;

namespace Discord_Stream_Notify_Bot.Interaction.Youtube
{
    [Group("youtube", "YouTube通知設定")]
    public class YoutubeStream : TopLevelModule<SharedService.Youtube.YoutubeStreamService>
    {
        private readonly DiscordSocketClient _client;

        public class GuildNoticeYoutubeChannelIdAutocompleteHandler : AutocompleteHandler
        {
            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
            {
                using var db = DataBase.DBContext.GetDbContext();
                if (!db.NoticeYoutubeStreamChannel.Any((x) => x.GuildId == context.Guild.Id))
                    return AutocompletionResult.FromSuccess();

                var channelIdList = db.NoticeYoutubeStreamChannel.Where((x) => x.GuildId == context.Guild.Id).Select((x) => new KeyValuePair<string, string>(db.GetChannelTitleByChannelId(x.NoticeStreamChannelId), x.NoticeStreamChannelId));

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
            }
        }

        public YoutubeStream(DiscordSocketClient client)
        {
            _client = client;
        }

        [SlashCommand("list-record-channel", "顯示直播記錄頻道")]
        public async Task ListRecordChannel()
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var nowRecordList = db.RecordYoutubeChannel.Select((x) => x.YoutubeChannelId).ToList();

                db.YoutubeChannelSpider.ToList().ForEach((item) => { if (!item.IsTrustedChannel && nowRecordList.Contains(item.ChannelId)) nowRecordList.Remove(item.ChannelId); });
                int warningChannelNum = db.YoutubeChannelSpider.Count((x) => x.IsTrustedChannel);

                if (nowRecordList.Count > 0)
                {
                    var list = new List<string>();

                    for (int i = 0; i < nowRecordList.Count; i += 50)
                    {
                        list.AddRange(await _service.GetChannelTitle(nowRecordList.Skip(i).Take(50), true));
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
                else await Context.Interaction.SendErrorAsync($"直播記錄清單中沒有任何頻道").ConfigureAwait(false);
            }
        }

        [SlashCommand("now-record-channel", "取得現在記錄直播的清單")]
        public async Task NowRecordChannel()
        {
            var newRecordStreamList = Discord_Stream_Notify_Bot.Utility.GetNowRecordStreamList();

            if (newRecordStreamList.Count == 0)
            {
                await Context.Interaction.SendErrorAsync("現在沒有直播記錄").ConfigureAwait(false);
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
                        await Context.Interaction.SendErrorAsync(ex.Message).ConfigureAwait(false);
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
                await Context.Interaction.SendErrorAsync(ex.Message).ConfigureAwait(false);
            }
        }

        [SlashCommand("now-streaming", "取得現在直播的成員")]
        public async Task NowStreaming(SharedService.Youtube.YoutubeStreamService.NowStreamingHost host)
        {
            var embed = await _service.GetNowStreamingChannel(host).ConfigureAwait(false);

            if (embed == null) await Context.Interaction.SendErrorAsync("無法取得直播清單").ConfigureAwait(false);
            else await Context.Interaction.RespondAsync(embed: embed).ConfigureAwait(false);
        }

        [SlashCommand("coming-soon-stream", "顯示接下來直播的清單")]
        public async Task ComingSoonStream()
        {
            try
            {
                List<Video> result = new List<Video>();

                for (int i = 0; i < _service.Reminders.Count; i += 50)
                {
                    var yt = _service.yt.Videos.List("snippet,liveStreamingDetails");
                    yt.Id = string.Join(',', _service.Reminders.Keys.Skip(i).Take(50));
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
                await Context.Interaction.SendErrorAsync("不明的錯誤，請向Bot擁有者回報", true);
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
                await Context.Interaction.SendErrorAsync("不明的錯誤，請向Bot擁有者回報", true);
            }
        }

        [EnabledInDm(false)]
        [RequireContext(ContextType.Guild)]
        [RequireBotPermission(GuildPermission.ManageGuild)]
        [RequireUserPermission(GuildPermission.ManageGuild, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [CommandSummary("設定伺服器橫幅使用指定頻道的最新影片(直播)縮圖\n" +
            "若未輸入頻道網址則關閉本設定\n\n" +
            "Bot需要有管理伺服器權限\n" +
            "且伺服器需有Boost Lv2才可使用本設定\n" +
            "(此功能依賴直播通知，請確保設定的頻道在兩大箱或是爬蟲清單內)")]
        [SlashCommand("set-banner-change", "設定伺服器橫幅使用指定頻道的最新影片(直播)縮圖")]
        public async Task SetBannerChange([Summary("頻道網址")] string channelUrl = "")
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (channelUrl == "")
                {
                    if (db.BannerChange.Any((x) => x.GuildId == Context.Guild.Id))
                    {
                        var guild = db.BannerChange.First((x) => x.GuildId == Context.Guild.Id);
                        db.BannerChange.Remove(guild);
                        db.SaveChanges();
                        await Context.Interaction.SendConfirmAsync("已移除橫幅設定");
                    }
                    else
                    {
                        await Context.Interaction.SendErrorAsync("伺服器並未使用本設定");
                    }
                }
                else
                {
                    if (Context.Guild.PremiumTier < PremiumTier.Tier2)
                    {
                        await Context.Interaction.SendErrorAsync("本伺服器未達Boost Lv2，不可設定橫幅\n" +
                            "故無法設定本功能");
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
                        await Context.Interaction.SendErrorAsync(fex.Message, true);
                        return;
                    }
                    catch (ArgumentNullException)
                    {
                        await Context.Interaction.SendErrorAsync("網址不可空白", true);
                        return;
                    }

                    string channelTitle = await _service.GetChannelTitle(channelId);

                    if (channelTitle == "")
                    {
                        await Context.Interaction.SendErrorAsync($"頻道 {channelId} 不存在", true).ConfigureAwait(false);
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

        [EnabledInDm(false)]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [CommandSummary("新增直播開台通知的頻道\n" +
            "輸入 `holo` 通知全部 `Holo成員` 的直播\n" +
            "輸入 `2434` 通知全部 `彩虹社成員` 的直播\n" +
            "(僅JP、EN跟VR的成員歸類在此選項內，如需其他成員建議先用 `/youtube-spider add` 設定)\n" +
            "輸入 `other` 通知部分 `非兩大箱` 的直播\n" +
            "(可以使用 `/youtube-spider list` 查詢有哪些頻道)\n" +
            "輸入 `all` 通知全部 `Holo + 2434 + 非兩大箱` 的直播\n" +
            "(all選項會覆蓋所有的通知設定，請注意)")]
        [CommandExample("https://www.youtube.com/channel/UCdn5BQ06XqgXoAxIhbqw5Rg", "all", "2434")]
        [SlashCommand("add", "新增直播開台通知的頻道")]
        public async Task AddChannel([Summary("頻道網址")] string channelUrl, [Summary("發送通知的頻道")] ITextChannel textChannel)
        {
            await DeferAsync(true).ConfigureAwait(false);

            var permissions = Context.Guild.GetUser(_client.CurrentUser.Id).GetPermissions(textChannel);
            if (!permissions.ViewChannel || !permissions.SendMessages)
            {
                await Context.Interaction.SendErrorAsync($"我在 `{textChannel}` 沒有 `讀取&編輯頻道` 的權限，請給予權限後再次執行本指令", true);
                return;
            }

            if (!permissions.EmbedLinks)
            {
                await Context.Interaction.SendErrorAsync($"我在 `{textChannel}` 沒有 `嵌入連結` 的權限，請給予權限後再次執行本指令", true);
                return;
            }

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

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var noticeYoutubeStreamChannel = db.NoticeYoutubeStreamChannel.FirstOrDefault((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == channelId);

                if (noticeYoutubeStreamChannel != null)
                {
                    if (await PromptUserConfirmAsync($"{channelId} 已在直播通知清單內，是否覆蓋設定?").ConfigureAwait(false))
                    {
                        noticeYoutubeStreamChannel.DiscordChannelId = textChannel.Id;
                        db.NoticeYoutubeStreamChannel.Update(noticeYoutubeStreamChannel);
                        db.SaveChanges();
                        await Context.Interaction.SendConfirmAsync($"已將 {channelId} 的通知頻道變更至: {textChannel}", true).ConfigureAwait(false);
                    }
                    return;
                }

                if (channelId == "all")
                {
                    if (db.NoticeYoutubeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id))
                    {
                        if (await PromptUserConfirmAsync("直播通知清單已有需通知的頻道\n" +
                            $"是否更改為通知全部頻道的直播?\n" +
                            $"注意: 將會把原先設定的直播通知清單重置").ConfigureAwait(false))
                        {
                            db.NoticeYoutubeStreamChannel.RemoveRange(Queryable.Where(db.NoticeYoutubeStreamChannel, (x) => x.GuildId == Context.Guild.Id));
                        }
                        else return;
                    }
                    db.NoticeYoutubeStreamChannel.Add(new NoticeYoutubeStreamChannel() { GuildId = Context.Guild.Id, DiscordChannelId = textChannel.Id, NoticeStreamChannelId = "all" });
                    await Context.Interaction.SendConfirmAsync($"將會通知全部的直播", true).ConfigureAwait(false);
                }
                else if (channelId == "holo" || channelId == "2434" || channelId == "other")
                {
                    if (db.NoticeYoutubeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == "all"))
                    {
                        if (await PromptUserConfirmAsync("已設定為通知全部頻道的直播\n" +
                            $"是否更改為僅通知 `{channelId}` 的直播?"))
                        {
                            db.NoticeYoutubeStreamChannel.Remove(db.NoticeYoutubeStreamChannel.First((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == "all"));
                            db.NoticeYoutubeStreamChannel.Add(new NoticeYoutubeStreamChannel() { GuildId = Context.Guild.Id, DiscordChannelId = textChannel.Id, NoticeStreamChannelId = channelId });
                            await Context.Interaction.SendConfirmAsync($"已將 {channelId} 加入到通知頻道清單內", true).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        db.NoticeYoutubeStreamChannel.Add(new NoticeYoutubeStreamChannel() { GuildId = Context.Guild.Id, DiscordChannelId = textChannel.Id, NoticeStreamChannelId = channelId });
                        await Context.Interaction.SendConfirmAsync($"已將 {channelId} 加入到通知頻道清單內", true).ConfigureAwait(false);
                    }
                }
                else
                {
                    string channelTitle = await _service.GetChannelTitle(channelId).ConfigureAwait(false);
                    if (channelTitle == "")
                    {
                        await Context.Interaction.SendErrorAsync($"頻道 {channelId} 不存在", true).ConfigureAwait(false);
                        return;
                    }

                    if (db.NoticeYoutubeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == "all"))
                    {
                        if (await PromptUserConfirmAsync("已設定為通知全部頻道的直播\n" +
                            $"是否更改為僅通知 `{channelTitle}` 的直播?"))
                        {
                            db.NoticeYoutubeStreamChannel.Remove(db.NoticeYoutubeStreamChannel.First((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == "all"));
                            db.NoticeYoutubeStreamChannel.Add(new NoticeYoutubeStreamChannel() { GuildId = Context.Guild.Id, DiscordChannelId = textChannel.Id, NoticeStreamChannelId = channelId });
                            await Context.Interaction.SendConfirmAsync($"已將 {channelTitle} 加入到通知頻道清單內", true).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        string addString = "";
                        if (!db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId) && !Extensions.IsChannelInDb(channelId))
                            addString += $"\n\n(注意: 該頻道未加入爬蟲清單\n如長時間無通知請使用 `/help get-command-help youtube-spider add` 查看說明並加入爬蟲)";
                        db.NoticeYoutubeStreamChannel.Add(new NoticeYoutubeStreamChannel() { GuildId = Context.Guild.Id, DiscordChannelId = textChannel.Id, NoticeStreamChannelId = channelId });
                        await Context.Interaction.SendConfirmAsync($"已將 {channelTitle} 加入到通知頻道清單內{addString}", true).ConfigureAwait(false);
                    }
                }

                db.SaveChanges();
            }
        }

        [EnabledInDm(false)]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [CommandSummary("移除直播開台通知的頻道\n" +
            "輸入holo移除全部 `Holo成員` 的直播通知\n" +
            "輸入2434移除全部 `彩虹社成員` 的直播通知\n" +
            "輸入other移除部分 `非兩大箱` 的直播通知\n" +
            "輸入all移除全部 `Holo + 2434 + 非兩大箱` 的直播通知")]
        [CommandExample("https://www.youtube.com/channel/UCdn5BQ06XqgXoAxIhbqw5Rg", "all", "2434")]
        [SlashCommand("remove", "移除直播開台通知的頻道")]
        public async Task RemoveChannel([Summary("頻道網址"), Autocomplete(typeof(GuildNoticeYoutubeChannelIdAutocompleteHandler))] string channelUrl)
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

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (!db.NoticeYoutubeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id))
                {
                    await Context.Interaction.SendErrorAsync("並未設定直播通知...", true).ConfigureAwait(false);
                    return;
                }

                if (channelId == "all")
                {
                    if (await PromptUserConfirmAsync("將移除全部的直播通知\n是否繼續?").ConfigureAwait(false))
                    {
                        db.NoticeYoutubeStreamChannel.RemoveRange(Queryable.Where(db.NoticeYoutubeStreamChannel, (x) => x.GuildId == Context.Guild.Id));
                        await Context.Interaction.SendConfirmAsync("已全部清除", true).ConfigureAwait(false);
                        db.SaveChanges();
                        return;
                    }
                    else return;
                }

                if (!db.NoticeYoutubeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == channelId))
                {
                    await Context.Interaction.SendErrorAsync($"並未設定`{channelId}`的直播通知...", true).ConfigureAwait(false);
                    return;
                }
                else
                {
                    if (channelId == "holo" || channelId == "2434" || channelId == "other")
                    {
                        db.NoticeYoutubeStreamChannel.Remove(db.NoticeYoutubeStreamChannel.First((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == channelId));
                        await Context.Interaction.SendConfirmAsync($"已移除 {channelId}", true).ConfigureAwait(false);
                    }
                    else if (db.NoticeYoutubeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == channelId))
                    {
                        string channelTitle = await _service.GetChannelTitle(channelId).ConfigureAwait(false);
                        if (channelTitle == "")
                        {
                            await Context.Interaction.SendErrorAsync($"頻道 {channelId} 不存在", true).ConfigureAwait(false);
                            return;
                        }

                        db.NoticeYoutubeStreamChannel.Remove(db.NoticeYoutubeStreamChannel.First((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == channelId));
                        await Context.Interaction.SendConfirmAsync($"已移除 {channelTitle}", true).ConfigureAwait(false);
                    }

                    db.SaveChanges();
                }
            }
        }

        [EnabledInDm(false)]
        [RequireContext(ContextType.Guild)]
        [SlashCommand("list", "顯示現在已加入通知清單的直播頻道")]
        public async Task ListChannel([Summary("頁數")] int page = 0)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var list = Queryable.Where(db.NoticeYoutubeStreamChannel, (x) => x.GuildId == Context.Guild.Id)
                .Select((x) => new KeyValuePair<string, ulong>(x.NoticeStreamChannelId, x.DiscordChannelId)).ToList();
                if (list.Count() == 0) { await Context.Interaction.SendErrorAsync("直播通知清單為空").ConfigureAwait(false); return; }

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
                            Log.Error(ex.Message + "\n" + ex.StackTrace);
                        }
                    }
                }

                await Context.SendPaginatedConfirmAsync(page, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("直播通知清單")
                        .WithDescription(string.Join('\n', channelTitleList.Skip(page * 20).Take(20)))
                        .WithFooter($"{Math.Min(channelTitleList.Count, (page + 1) * 20)} / {channelTitleList.Count}個頻道");
                }, channelTitleList.Count, 20, false);
            }
        }

        [EnabledInDm(false)]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages | GuildPermission.MentionEveryone, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [RequireBotPermission(GuildPermission.MentionEveryone)]
        [CommandSummary("設定通知訊息\n" +
            "不輸入通知訊息的話則會關閉該類型的通知\n" +
            "若輸入`-`則可以關閉該通知類型\n" +
            "需先新增直播通知後才可設定通知訊息 (`/help get-command-help youtube add`)\n\n" +
            "NoticeType(通知類型)說明:\n" +
            "NewStream: 新待機所\n" +
            "NewVideo: 新影片\n" +
            "Start: 開始直播\n" +
            "End: 結束直播\n" +
            "ChangeTime: 變更直播時間\n" +
            "Delete: 刪除直播\n\n" +
            "(考慮到有伺服器需Ping特定用戶組的情況，故Bot需提及所有身分組權限)")]
        [CommandExample("UCXRlIK3Cw_TJIQC5kSJJQMg start @通知用的用戶組 阿床開台了",
            "holo newstream @某人 新待機所建立",
            "UCUKD-uaobj9jiqB-VXt71mA newstream -",
            "UCXRlIK3Cw_TJIQC5kSJJQMg end")]
        [SlashCommand("set-message", "設定通知訊息")]
        public async Task SetMessage([Summary("頻道網址"), Autocomplete(typeof(GuildNoticeYoutubeChannelIdAutocompleteHandler))] string channelUrl, [Summary("通知類型")] SharedService.Youtube.YoutubeStreamService.NoticeType noticeType, [Summary("通知訊息")] string message = "")
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

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (db.NoticeYoutubeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == channelId))
                {
                    var noticeStreamChannel = db.NoticeYoutubeStreamChannel.First((x) => x.GuildId == Context.Guild.Id && x.NoticeStreamChannelId == channelId);
                    string noticeTypeString = "";

                    message = message.Trim();
                    switch (noticeType)
                    {
                        case SharedService.Youtube.YoutubeStreamService.NoticeType.NewStream:
                            noticeStreamChannel.NewStreamMessage = message;
                            noticeTypeString = "新待機所";
                            break;
                        case SharedService.Youtube.YoutubeStreamService.NoticeType.NewVideo:
                            noticeStreamChannel.NewVideoMessage = message;
                            noticeTypeString = "新影片";
                            break;
                        case SharedService.Youtube.YoutubeStreamService.NoticeType.Start:
                            noticeStreamChannel.StratMessage = message;
                            noticeTypeString = "開始直播";
                            break;
                        case SharedService.Youtube.YoutubeStreamService.NoticeType.End:
                            noticeStreamChannel.EndMessage = message;
                            noticeTypeString = "結束直播";
                            break;
                        case SharedService.Youtube.YoutubeStreamService.NoticeType.ChangeTime:
                            noticeStreamChannel.ChangeTimeMessage = message;
                            noticeTypeString = "變更直播時間";
                            break;
                        case SharedService.Youtube.YoutubeStreamService.NoticeType.Delete:
                            noticeStreamChannel.DeleteMessage = message;
                            noticeTypeString = "刪除直播";
                            break;
                    }

                    db.NoticeYoutubeStreamChannel.Update(noticeStreamChannel);
                    db.SaveChanges();

                    if (message == "-") await Context.Interaction.SendConfirmAsync($"已關閉 {channelId} 的 {noticeTypeString} 通知", true).ConfigureAwait(false);
                    else if (message != "") await Context.Interaction.SendConfirmAsync($"已設定 {channelId} 的 {noticeTypeString} 通知訊息為:\n{message}", true).ConfigureAwait(false);
                    else await Context.Interaction.SendConfirmAsync($"已取消 {channelId} 的 {noticeTypeString} 通知訊息", true).ConfigureAwait(false);
                }
                else
                {
                    await Context.Interaction.SendErrorAsync($"並未設定 {channelId} 的直播通知\n請先使用 `/youtube add {channelId}` 新增直播後再設定通知訊息", true).ConfigureAwait(false);
                }
            }
        }

        string GetCurrectMessage(string message)
            => message == "-" ? "(已關閉本類別的通知)" : message;

        [EnabledInDm(false)]
        [RequireContext(ContextType.Guild)]
        [SlashCommand("list-message", "列出已設定的通知訊息")]
        public async Task ListMessage([Summary("頁數")] int page = 0)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (db.NoticeYoutubeStreamChannel.Any((x) => x.GuildId == Context.Guild.Id))
                {
                    var noticeStreamChannels = db.NoticeYoutubeStreamChannel.Where((x) => x.GuildId == Context.Guild.Id);
                    Dictionary<string, string> dic = new Dictionary<string, string>();

                    foreach (var item in noticeStreamChannels)
                    {
                        var channelTitle = item.NoticeStreamChannelId;
                        if (channelTitle.StartsWith("UC")) channelTitle = (await _service.GetChannelTitle(channelTitle).ConfigureAwait(false)) + $" ({item.NoticeStreamChannelId})";

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
                        EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor().WithTitle("通知訊息清單")
                            .WithDescription("如果沒訊息的話就代表沒設定\n不用擔心會Tag到用戶組，Embed不會有Ping的反應");

                        foreach (var item in dic.Skip(page * 4).Take(4))
                        {
                            embedBuilder.AddField(item.Key, item.Value);
                        }

                        return embedBuilder;
                    }, dic.Count, 4);
                }
                else
                {
                    await Context.Interaction.SendErrorAsync($"並未設定直播通知\n請先使用 `/help get-command-help youtube add` 查看說明並新增直播通知").ConfigureAwait(false);
                }
            }
        }
    }
}