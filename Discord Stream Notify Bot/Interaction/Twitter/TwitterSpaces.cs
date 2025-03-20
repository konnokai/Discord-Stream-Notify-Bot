using Discord.Interactions;
using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction.Attribute;

namespace Discord_Stream_Notify_Bot.Interaction.Twitter
{
    [RequireContext(ContextType.Guild)]
    [Group("twitter-space", "推特語音空間")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    [DefaultMemberPermissions(GuildPermission.ManageMessages)]
    public class TwitterSpaces : TopLevelModule<SharedService.Twitter.TwitterSpacesService>
    {
        private readonly DiscordSocketClient _client;
        private readonly MainDbService _dbService;

        public class GuildNoticeTwitterSpaceIdAutocompleteHandler : AutocompleteHandler
        {
            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
            {
                return await Task.Run(() =>
                {
                    using var db = Bot.DbService.GetDbContext();
                    if (!db.NoticeTwitterSpaceChannel.Any((x) => x.GuildId == context.Guild.Id))
                        return AutocompletionResult.FromSuccess();

                    var channelIdList = db.NoticeTwitterSpaceChannel.Where((x) => x.GuildId == context.Guild.Id).Select((x) => new KeyValuePair<string, string>(db.GetTwitterUserNameByUserScreenName(x.NoticeTwitterSpaceUserScreenName), x.NoticeTwitterSpaceUserScreenName));

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
                        Log.Error($"GuildNoticeTwitterSpaceIdAutocompleteHandler - {ex}");
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

        public class GuildTwitterSpaceSpiderAutocompleteHandler : AutocompleteHandler
        {
            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
            {
                return await Task.Run(() =>
                {
                    using var db = Bot.DbService.GetDbContext();
                    IQueryable<TwitterSpaecSpider> channelList;

                    if (autocompleteInteraction.User.Id == Bot.ApplicatonOwner.Id)
                    {
                        channelList = db.TwitterSpaecSpider;
                    }
                    else
                    {
                        if (!db.TwitterSpaecSpider.Any((x) => x.GuildId == autocompleteInteraction.GuildId))
                            return AutocompletionResult.FromSuccess();

                        channelList = db.TwitterSpaecSpider.Where((x) => x.GuildId == autocompleteInteraction.GuildId);
                    }

                    var channelList2 = new List<TwitterSpaecSpider>();
                    try
                    {
                        string value = autocompleteInteraction.Data.Current.Value.ToString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            foreach (var item in channelList)
                            {
                                if (item.UserName.Contains(value, StringComparison.CurrentCultureIgnoreCase) || item.UserScreenName.Contains(value, StringComparison.CurrentCultureIgnoreCase))
                                {
                                    channelList2.Add(item);
                                }
                            }
                        }
                        else
                        {
                            channelList2 = channelList.ToList();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"GuildTwitterSpaceSpiderAutocompleteHandler - {ex}");
                    }

                    List<AutocompleteResult> results = new();
                    foreach (var item in channelList2)
                    {
                        results.Add(new AutocompleteResult(item.UserName, item.UserScreenName));
                    }

                    return AutocompletionResult.FromSuccess(results.Take(25));
                });
            }
        }

        public TwitterSpaces(DiscordSocketClient client, MainDbService dbService)
        {
            _client = client;
            _dbService = dbService;
        }

        [CommandSummary("新增推特語音空間開台通知的頻道\n" +
            "請使用@後面的使用者名稱來新增\n" +
            "可以使用`/twitter-space list`查詢有哪些頻道\n")]
        [CommandExample("_998rrr_", "@inui_toko")]
        [SlashCommand("add", "新增推特語音空間開台通知的頻道")]
        public async Task AddChannel([Summary("推特使用者名稱")] string userScreenName,
            [Summary("發送通知的頻道"), ChannelTypes(ChannelType.Text, ChannelType.News)] IChannel channel)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Interaction.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            if (!_service.IsEnable)
            {
                await Context.Interaction.SendErrorAsync("此 Bot 的 Twitter 功能已關閉，請向 Bot 擁有者確認");
                return;
            }

            await DeferAsync(true).ConfigureAwait(false);

            var textChannel = channel as IGuildChannel;

            var permissions = (Context.Guild.GetUser(_client.CurrentUser.Id)).GetPermissions(textChannel);
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

            userScreenName = userScreenName.Replace("@", "");

            var user = await _service.GetTwitterUserAsync(userScreenName);
            if (user == null)
            {
                await Context.Interaction.SendErrorAsync($"`{userScreenName}` 不存在此使用者\n" +
                    $"這不是 Twitch 直播通知!!!!!!\n" +
                    "請確認名稱是否正確，若正確請向 Bot 擁有者回報", true).ConfigureAwait(false);
                return;
            }

            using (var db = _dbService.GetDbContext())
            {
                var noticeTwitterSpaceChannel = db.NoticeTwitterSpaceChannel.FirstOrDefault((x) => x.GuildId == Context.Guild.Id && x.NoticeTwitterSpaceUserId == user.RestId);

                if (noticeTwitterSpaceChannel != null)
                {
                    if (await PromptUserConfirmAsync($"`{user.Legacy.Name}` 已在語音空間通知清單內，是否覆蓋設定?").ConfigureAwait(false))
                    {
                        noticeTwitterSpaceChannel.DiscordChannelId = textChannel.Id;
                        db.NoticeTwitterSpaceChannel.Update(noticeTwitterSpaceChannel);
                        db.SaveChanges();
                        await Context.Interaction.SendConfirmAsync($"已將 `{user.Legacy.Name}` 的語音空間通知頻道變更至: {textChannel}", true, true).ConfigureAwait(false);
                    }
                    return;
                }

                string addString = "";
                if (!db.IsTwitterUserInDb(user.RestId)) addString += $"\n\n(注意: 該使用者未加入爬蟲清單\n如長時間無通知請使用 `/help get-command-help twitter-spider add` 查看說明並加入爬蟲)";
                db.NoticeTwitterSpaceChannel.Add(new NoticeTwitterSpaceChannel() { GuildId = Context.Guild.Id, DiscordChannelId = textChannel.Id, NoticeTwitterSpaceUserId = user.RestId, NoticeTwitterSpaceUserScreenName = user.Legacy.ScreenName });
                await Context.Interaction.SendConfirmAsync($"已將 `{user.Legacy.Name}` 加入到語音空間通知頻道清單內{addString}", true, true).ConfigureAwait(false);

                db.SaveChanges();
            }
        }

        [CommandSummary("移除推特語音空間通知的頻道\n" +
             "請使用@後面的使用者名稱來移除")]
        [CommandExample("_998rrr_", "@inui_toko")]
        [SlashCommand("remove", "移除推特語音空間開台通知的頻道")]
        public async Task RemoveChannel([Summary("推特使用者名稱"), Autocomplete(typeof(GuildNoticeTwitterSpaceIdAutocompleteHandler))] string userScreenName)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Interaction.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "").ToLower();

            using (var db = _dbService.GetDbContext())
            {
                if (!db.NoticeTwitterSpaceChannel.Any((x) => x.GuildId == Context.Guild.Id))
                {
                    await Context.Interaction.SendErrorAsync("並未設定推特語音空間通知...").ConfigureAwait(false);
                    return;
                }

                if (!db.NoticeTwitterSpaceChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeTwitterSpaceUserScreenName == userScreenName))
                {
                    await Context.Interaction.SendErrorAsync($"並未設定 `{userScreenName}` 的推特語音空間通知...").ConfigureAwait(false);
                    return;
                }
                else
                {
                    db.NoticeTwitterSpaceChannel.Remove(db.NoticeTwitterSpaceChannel.First((x) => x.GuildId == Context.Guild.Id && x.NoticeTwitterSpaceUserScreenName == userScreenName));
                    await Context.Interaction.SendConfirmAsync($"已移除 `{userScreenName}`", false, true).ConfigureAwait(false);

                    db.SaveChanges();
                }
            }
        }

        [SlashCommand("list", "顯示現在已加入推特語音空間通知的頻道")]
        public async Task ListChannel([Summary("頁數")] int page = 0)
        {
            using (var db = _dbService.GetDbContext())
            {
                var list = db.NoticeTwitterSpaceChannel.Where((x) => x.GuildId == Context.Guild.Id)
                .Select((x) => new KeyValuePair<string, ulong>(x.NoticeTwitterSpaceUserScreenName, x.DiscordChannelId)).ToList();

                if (list.Count() == 0) { await Context.Interaction.SendErrorAsync("推特語音空間通知清單為空").ConfigureAwait(false); return; }
                var twitterSpaceList = list.Select((x) => $"{x.Key} => <#{x.Value}>").ToList();

                await Context.SendPaginatedConfirmAsync(page, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("推特語音空間通知清單")
                        .WithDescription(string.Join('\n', twitterSpaceList.Skip(page * 20).Take(20)))
                        .WithFooter($"{Math.Min(twitterSpaceList.Count, (page + 1) * 20)} / {twitterSpaceList.Count} 個使用者");
                }, twitterSpaceList.Count, 20, false);
            }
        }

        [RequireBotPermission(GuildPermission.MentionEveryone)]
        [CommandSummary("設定通知訊息\n" +
            "不輸入通知訊息的話則會關閉通知訊息\n" +
            "需先新增直播通知後才可設定通知訊息(`/help get-command-help twitter-space add`)\n\n" +
            "(考慮到有伺服器需Ping特定用戶組的情況，故Bot需提及所有身分組權限)")]
        [CommandExample("_998rrr_", "_998rrr_ @直播通知 玖玖巴開語音啦")]
        [SlashCommand("set-message", "設定通知訊息")]
        public async Task SetMessage([Summary("推特使用者名稱"), Autocomplete(typeof(GuildNoticeTwitterSpaceIdAutocompleteHandler))] string userScreenName, [Summary("通知訊息")] string message = "")
        {
            try
            {
                await DeferAsync(true);

                if (string.IsNullOrWhiteSpace(userScreenName))
                {
                    await Context.Interaction.SendErrorAsync("使用者名稱不可空白", true).ConfigureAwait(false);
                    return;
                }

                userScreenName = userScreenName.Replace("@", "");

                var user = await _service.GetTwitterUserAsync(userScreenName);
                if (user == null)
                {
                    await Context.Interaction.SendErrorAsync($"`{userScreenName}` 不存在此使用者\n" +
                        $"(注意: 設定時請勿切換 Discord 頻道，這會導致自動輸入的頻道名稱跑掉)", true).ConfigureAwait(false);
                    return;
                }

                using (var db = _dbService.GetDbContext())
                {
                    if (db.NoticeTwitterSpaceChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeTwitterSpaceUserId == user.RestId))
                    {
                        var noticeTwitterSpace = db.NoticeTwitterSpaceChannel.First((x) => x.GuildId == Context.Guild.Id && x.NoticeTwitterSpaceUserId == user.RestId);

                        noticeTwitterSpace.StratTwitterSpaceMessage = message;

                        db.NoticeTwitterSpaceChannel.Update(noticeTwitterSpace);
                        db.SaveChanges();

                        if (message != "") await Context.Interaction.SendConfirmAsync($"已設定 `{user.Legacy.Name}` 的推特語音空間通知訊息為:\n{message}", true, true).ConfigureAwait(false);
                        else await Context.Interaction.SendConfirmAsync($"已取消 `{user.Legacy.Name}` 的推特語音空間通知訊息", true, true).ConfigureAwait(false);
                    }
                    else
                    {
                        await Context.Interaction.SendErrorAsync($"並未設定 `{user.Legacy.Name}` 的推特語音空間通知\n" +
                            $"請先使用 `/twitter-space add {userScreenName}` 新增語音空間通知後再設定通知訊息", true).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Twitter Set Message Error: {userScreenName}");
                await Context.Interaction.SendErrorAsync("未知的錯誤，請向 Bot 擁有者回報", true);
            }
        }

        [SlashCommand("list-message", "列出已設定的推特語音空間通知訊息")]
        public async Task ListMessage([Summary("頁數")] int page = 0)
        {
            await DeferAsync(true);

            try
            {
                using (var db = _dbService.GetDbContext())
                {
                    if (db.NoticeTwitterSpaceChannel.Any((x) => x.GuildId == Context.Guild.Id))
                    {
                        var noticeTwitterSpaces = db.NoticeTwitterSpaceChannel.Where((x) => x.GuildId == Context.Guild.Id);
                        Dictionary<string, string> dic = new Dictionary<string, string>();

                        foreach (var item in noticeTwitterSpaces)
                        {
                            string message = string.IsNullOrWhiteSpace(item.StratTwitterSpaceMessage) ? "無" : item.StratTwitterSpaceMessage;
                            dic.Add(item.NoticeTwitterSpaceUserScreenName, message);
                        }

                        try
                        {
                            await Context.SendPaginatedConfirmAsync(page, (page) =>
                            {
                                EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor().WithTitle("推特語音空間通知訊息清單")
                                    .WithDescription("如果沒訊息的話就代表沒設定\n不用擔心會 Tag 到用戶組，Embed 不會有 Ping 的反應");

                                foreach (var item in dic.Skip(page * 10).Take(10))
                                {
                                    embedBuilder.AddField(item.Key, item.Value);
                                }

                                return embedBuilder;
                            }, dic.Count, 10, isFollowup: true).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex.Message + "\n" + ex.StackTrace);
                        }
                    }
                    else
                    {
                        await Context.Interaction.SendErrorAsync($"並未設定推特語音空間通知\n" +
                            $"請先使用 `/help get-command-help twitter-space add` 查看說明並新增語音空間通知", true, true).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Twitter list-message: {Context.Guild.Id}");
                await Context.Interaction.SendErrorAsync("未知的錯誤，請向 Bot 擁有者回報");
            }
        }
    }
}
