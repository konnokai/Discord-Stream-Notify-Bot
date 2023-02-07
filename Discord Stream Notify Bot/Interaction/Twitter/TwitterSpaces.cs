using Discord.Interactions;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction.Attribute;
using SocialOpinionAPI.Models.Users;

namespace Discord_Stream_Notify_Bot.Interaction.Twitter
{
    [Group("twitter-space", "推特語音空間")]
    public class TwitterSpaces : TopLevelModule<SharedService.Twitter.TwitterSpacesService>
    {
        private readonly DiscordSocketClient _client;

        public class GuildNoticeTwitterSpaceIdAutocompleteHandler : AutocompleteHandler
        {
            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
            {
                using var db = DataBase.DBContext.GetDbContext();
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
            }
        }

        public class GuildTwitterSpaceSpiderAutocompleteHandler : AutocompleteHandler
        {
            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
            {
                using var db = DataBase.DBContext.GetDbContext();
                IQueryable<TwitterSpaecSpider> channelList;

                if (autocompleteInteraction.User.Id == Program.ApplicatonOwner.Id)
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
            }
        }

        public TwitterSpaces(DiscordSocketClient client)
        {
            _client = client;
        }

        [EnabledInDm(false)]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [CommandSummary("新增推特語音空間開台通知的頻道\n" +
            "請使用@後面的使用者名稱來新增\n" +
            "可以使用`/twitter-space list`查詢有哪些頻道\n")]
        [CommandExample("LaplusDarknesss", "@inui_toko")]
        [SlashCommand("add", "新增推特語音空間開台通知的頻道")]
        public async Task AddChannel([Summary("推特使用者名稱")] string userScreenName, [Summary("發送通知的頻道")] ITextChannel textChannel)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Interaction.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            await DeferAsync(true).ConfigureAwait(false);

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

            UserModel user = _service.GetTwitterUser(userScreenName);
            if (user == null)
            {
                await Context.Interaction.SendErrorAsync($"{userScreenName} 不存在此使用者", true).ConfigureAwait(false);
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var noticeTwitterSpaceChannel = db.NoticeTwitterSpaceChannel.FirstOrDefault((x) => x.GuildId == Context.Guild.Id && x.NoticeTwitterSpaceUserId == user.data.id);

                if (noticeTwitterSpaceChannel != null)
                {
                    if (await PromptUserConfirmAsync($"{user.data.name} 已在語音空間通知清單內，是否覆蓋設定?").ConfigureAwait(false))
                    {
                        noticeTwitterSpaceChannel.DiscordChannelId = textChannel.Id;
                        db.NoticeTwitterSpaceChannel.Update(noticeTwitterSpaceChannel);
                        db.SaveChanges();
                        await Context.Interaction.SendConfirmAsync($"已將 {user.data.name} 的語音空間通知頻道變更至: {textChannel}", true).ConfigureAwait(false);
                    }
                    return;
                }

                string addString = "";
                if (!db.IsTwitterUserInDb(user.data.id)) addString += $"\n\n(注意: 該使用者未加入爬蟲清單\n如長時間無通知請使用 `/help get-command-help twitter-spider add` 查看說明並加入爬蟲)";
                db.NoticeTwitterSpaceChannel.Add(new NoticeTwitterSpaceChannel() { GuildId = Context.Guild.Id, DiscordChannelId = textChannel.Id, NoticeTwitterSpaceUserId = user.data.id, NoticeTwitterSpaceUserScreenName = user.data.username.ToLower() });
                await Context.Interaction.SendConfirmAsync($"已將 {user.data.name} 加入到語音空間通知頻道清單內{addString}", true).ConfigureAwait(false);

                db.SaveChanges();
            }
        }

        [EnabledInDm(false)]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [CommandSummary("移除推特語音空間通知的頻道\n" +
             "請使用@後面的使用者名稱來移除")]
        [CommandExample("LaplusDarknesss", "@inui_toko")]
        [SlashCommand("remove", "移除推特語音空間開台通知的頻道")]
        public async Task RemoveChannel([Summary("推特使用者名稱"), Autocomplete(typeof(GuildNoticeTwitterSpaceIdAutocompleteHandler))] string userScreenName)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Interaction.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "").ToLower();

            using (var db = DataBase.DBContext.GetDbContext())
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
                    await Context.Interaction.SendConfirmAsync($"已移除 {userScreenName}").ConfigureAwait(false);

                    db.SaveChanges();
                }
            }
        }

        [EnabledInDm(false)]
        [RequireContext(ContextType.Guild)]
        [SlashCommand("list", "顯示現在已加入推特語音空間通知的頻道")]
        public async Task ListChannel()
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var list = db.NoticeTwitterSpaceChannel.Where((x) => x.GuildId == Context.Guild.Id)
                .Select((x) => new KeyValuePair<string, ulong>(x.NoticeTwitterSpaceUserScreenName, x.DiscordChannelId)).ToList();

                if (list.Count() == 0) { await Context.Interaction.SendErrorAsync("推特語音空間通知清單為空").ConfigureAwait(false); return; }
                var twitterSpaceList = list.Select((x) => $"{x.Key} => <#{x.Value}>").ToList();

                await Context.SendPaginatedConfirmAsync(0, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("推特語音空間通知清單")
                        .WithDescription(string.Join('\n', twitterSpaceList.Skip(page * 20).Take(20)))
                        .WithFooter($"{Math.Min(twitterSpaceList.Count, (page + 1) * 20)} / {twitterSpaceList.Count}個使用者");
                }, twitterSpaceList.Count, 20, false);
            }
        }

        [EnabledInDm(false)]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages | GuildPermission.MentionEveryone, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [RequireBotPermission(GuildPermission.MentionEveryone)]
        [CommandSummary("設定通知訊息\n" +
            "不輸入通知訊息的話則會關閉通知訊息\n" +
            "需先新增直播通知後才可設定通知訊息(`/help get-command-help twitter-space add`)\n\n" +
            "(考慮到有伺服器需Ping特定用戶組的情況，故Bot需提及所有身分組權限)")]
        [CommandExample("LaplusDarknesss", "LaplusDarknesss @直播通知 總帥突襲開語音啦")]
        [SlashCommand("set-message", "設定通知訊息")]
        public async Task SetMessage([Summary("推特使用者名稱"), Autocomplete(typeof(GuildNoticeTwitterSpaceIdAutocompleteHandler))] string userScreenName, [Summary("通知訊息")] string message = "")
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Interaction.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "");

            UserModel user = _service.GetTwitterUser(userScreenName);
            if (user == null)
            {
                await Context.Interaction.SendErrorAsync($"{userScreenName} 不存在此使用者").ConfigureAwait(false);
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (db.NoticeTwitterSpaceChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeTwitterSpaceUserId == user.data.id))
                {
                    var noticeTwitterSpace = db.NoticeTwitterSpaceChannel.First((x) => x.GuildId == Context.Guild.Id && x.NoticeTwitterSpaceUserId == user.data.id);

                    noticeTwitterSpace.StratTwitterSpaceMessage = message;

                    db.NoticeTwitterSpaceChannel.Update(noticeTwitterSpace);
                    db.SaveChanges();

                    if (message != "") await Context.Interaction.SendConfirmAsync($"已設定 {user.data.name} 的推特語音空間通知訊息為:\n{message}").ConfigureAwait(false);
                    else await Context.Interaction.SendConfirmAsync($"已取消 {user.data.name} 的推特語音空間通知訊息").ConfigureAwait(false);
                }
                else
                {
                    await Context.Interaction.SendConfirmAsync($"並未設定推特語音空間通知\n" +
                        $"請先使用 `/help get-command-help twitter-space add` 查看說明並新增語音空間通知").ConfigureAwait(false);
                }
            }
        }

        [EnabledInDm(false)]
        [RequireContext(ContextType.Guild)]
        [SlashCommand("list-message", "列出已設定的推特語音空間通知訊息")]
        public async Task ListMessage([Summary("頁數")] int page = 0)
        {
            using (var db = DataBase.DBContext.GetDbContext())
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
                                .WithDescription("如果沒訊息的話就代表沒設定\n不用擔心會Tag到用戶組，Embed不會有Ping的反應");

                            foreach (var item in dic.Skip(page * 10).Take(10))
                            {
                                embedBuilder.AddField(item.Key, item.Value);
                            }

                            return embedBuilder;
                        }, dic.Count, 10).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex.Message + "\n" + ex.StackTrace);
                    }
                }
                else
                {
                    await Context.Interaction.SendConfirmAsync($"並未設定推特語音空間通知\n" +
                        $"請先使用 `/help get-command-help twitter-space add` 查看說明並新增語音空間通知").ConfigureAwait(false);
                }
            }
        }

        [SlashCommand("list-record-user", "顯示推特語音空間錄影清單")]
        public async Task ListRecord([Summary("頁數")] int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var nowRecordList = db.TwitterSpaecSpider.Where((x) => x.IsRecord && !x.IsWarningUser).Select((x) => $"{x.UserName} ({Format.Url($"{x.UserScreenName}", $"https://twitter.com/{x.UserScreenName}")})").ToList();
                int warningUserNum = db.TwitterSpaecSpider.Count((x) => x.IsWarningUser);

                if (nowRecordList.Count > 0)
                {
                    nowRecordList.Sort();
                    await Context.SendPaginatedConfirmAsync(0, page =>
                    {
                        return new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("推特語音空間記錄清單")
                            .WithDescription(string.Join('\n', nowRecordList.Skip(page * 20).Take(20)))
                            .WithFooter($"{Math.Min(nowRecordList.Count, (page + 1) * 20)} / {nowRecordList.Count}個使用者 ({warningUserNum}個隱藏的警告頻道)");
                    }, nowRecordList.Count, 20, false);
                }
                else await Context.Interaction.SendErrorAsync($"語音空間記錄清單中沒有任何使用者").ConfigureAwait(false);
            }
        }
    }
}
