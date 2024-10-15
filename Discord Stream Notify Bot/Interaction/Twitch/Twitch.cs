using Discord.Interactions;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction.Attribute;
using Discord_Stream_Notify_Bot.SharedService.Twitch;

namespace Discord_Stream_Notify_Bot.Interaction.Twitch
{
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    [DefaultMemberPermissions(GuildPermission.ManageMessages)]
    [Group("twitch", "Twitch 通知設定")]
    public class Twitch : TopLevelModule<TwitchService>
    {
        private readonly DiscordSocketClient _client;

        public class GuildNoticeTwitchChannelIdAutocompleteHandler : AutocompleteHandler
        {
            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
            {
                return await Task.Run(() =>
                {
                    using var db = DataBase.MainDbContext.GetDbContext();
                    if (!db.NoticeTwitchStreamChannels.Any((x) => x.GuildId == context.Guild.Id))
                        return AutocompletionResult.FromSuccess();

                    var channelIdList = db.NoticeTwitchStreamChannels
                        .Where((x) => x.GuildId == context.Guild.Id)
                        .Select((x) => new KeyValuePair<string, string>(db.GetTwitchUserNameByUserId(x.NoticeTwitchUserId), x.NoticeTwitchUserId));

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
                        Log.Error($"GuildNoticeTwitchChannelIdAutocompleteHandler - {ex}");
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

        public Twitch(DiscordSocketClient client)
        {
            _client = client;
        }

        [CommandExample("998rrr", "https://twitch.tv/998rrr")]
        [SlashCommand("add", "新增 Twitch 直播通知的頻道")]
        public async Task AddChannel([Summary("頻道網址")] string twitchUrl,
            [Summary("發送通知的頻道"), ChannelTypes(ChannelType.Text, ChannelType.News)] IChannel channel)
        {
            if (!_service.IsEnable)
            {
                await Context.Interaction.SendErrorAsync("此 Bot 的 Twitch 功能已關閉，請向 Bot 擁有者確認").ConfigureAwait(false);
                return;
            }

            await DeferAsync(true).ConfigureAwait(false);

            try
            {

                var textChannel = channel as IGuildChannel;

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

                var userData = await _service.GetUserAsync(twitchUserLogin: _service.GetUserLoginByUrl(twitchUrl));
                if (userData == null)
                {
                    await Context.Interaction.SendErrorAsync("錯誤，Twitch 使用者資料獲取失敗\n" +
                            "請確認網址是否正確，若正確請向 Bot 擁有者回報", true);
                    return;
                }

                using (var db = DataBase.MainDbContext.GetDbContext())
                {
                    await CheckIsFirstSetNoticeAndSendWarningMessageAsync(db);

                    var noticeTwitchStreamChannel = db.NoticeTwitchStreamChannels.FirstOrDefault((x) => x.GuildId == Context.Guild.Id && x.NoticeTwitchUserId == userData.Id);
                    if (noticeTwitchStreamChannel != null)
                    {
                        if (await PromptUserConfirmAsync($"`{userData.DisplayName}` 已在直播通知清單內，是否覆蓋設定?").ConfigureAwait(false))
                        {
                            noticeTwitchStreamChannel.DiscordChannelId = textChannel.Id;
                            db.NoticeTwitchStreamChannels.Update(noticeTwitchStreamChannel);
                            await Context.Interaction.SendConfirmAsync($"已將 `{userData.DisplayName}` 的通知頻道變更至: {textChannel}", true, true).ConfigureAwait(false);
                        }
                        else return;
                    }
                    else
                    {
                        string addString = "";
                        if (!db.TwitchSpider.Any((x) => x.UserId == userData.Id))
                            addString += $"\n\n(注意: 該頻道未加入爬蟲清單\n如長時間無通知請使用 `/help get-command-help twitch-spider add` 查看說明並加入爬蟲)";

                        db.NoticeTwitchStreamChannels.Add(new NoticeTwitchStreamChannel() { GuildId = Context.Guild.Id, DiscordChannelId = textChannel.Id, NoticeTwitchUserId = userData.Id });
                        await Context.Interaction.SendConfirmAsync($"已將 `{userData.DisplayName}` 加入到 Twitch 通知頻道清單內{addString}", true, true).ConfigureAwait(false);
                    }

                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Twitch Add Error: {twitchUrl}");
                await Context.Interaction.SendErrorAsync("新增失敗，請向 Bot 擁有者回報", true);
            }
        }

        [CommandExample("998rrr", "https://twitch.tv/998rrr")]
        [SlashCommand("remove", "移除 Twitch 直播通知的頻道")]
        public async Task RemoveChannel([Summary("頻道名稱", "userName"), Autocomplete(typeof(GuildNoticeTwitchChannelIdAutocompleteHandler))] string twitchId)
        {
            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var noticeTwitchStreamChannel = db.NoticeTwitchStreamChannels.FirstOrDefault((x) => x.GuildId == Context.Guild.Id && x.NoticeTwitchUserId == twitchId);

                if (noticeTwitchStreamChannel == null)
                {
                    await Context.Interaction.SendErrorAsync($"並未設定 `{twitchId}` 的直播通知...", true).ConfigureAwait(false);
                }
                else
                {
                    db.NoticeTwitchStreamChannels.Remove(noticeTwitchStreamChannel);
                    db.SaveChanges();

                    await Context.Interaction.SendConfirmAsync($"已移除 `{db.GetTwitchUserNameByUserId(twitchId)}`", false, true).ConfigureAwait(false);
                }
            }
        }

        [SlashCommand("list", "顯示現在已加入通知清單的 Twitch 直播頻道")]
        public async Task ListChannel([Summary("頁數")] int page = 0)
        {
            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var list = Queryable.Where(db.NoticeTwitchStreamChannels, (x) => x.GuildId == Context.Guild.Id)
                    .Select((x) => $"`{db.GetTwitchUserNameByUserId(x.NoticeTwitchUserId)}` => <#{x.DiscordChannelId}>").ToList();
                if (!list.Any()) { await Context.Interaction.SendErrorAsync("Twitch 直播通知清單為空").ConfigureAwait(false); return; }

                await Context.SendPaginatedConfirmAsync(page, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("Twitch 直播通知清單")
                        .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20)))
                        .WithFooter($"{Math.Min(list.Count, (page + 1) * 20)} / {list.Count}個頻道");
                }, list.Count, 20, false);
            }
        }

        [RequireBotPermission(GuildPermission.MentionEveryone)]
        [CommandSummary("設定通知訊息\n" +
            "不輸入通知訊息的話則會關閉通知訊息\n" +
            "若輸入 `-` 則可以關閉該通知類型\n" +
            "需先新增直播通知後才可設定通知訊息(`/help get-command-help twitch add`)\n\n" +
            "(考慮到有伺服器需 Ping 特定用戶組的情況，故 Bot 需提及所有身分組權限)")]
        [CommandExample("998rrr 開台啦", "https://twitch.tv/998rrr 開始直播 開台啦")]
        [SlashCommand("set-message", "設定通知訊息")]
        public async Task SetMessage([Summary("頻道名稱"), Autocomplete(typeof(GuildNoticeTwitchChannelIdAutocompleteHandler))] string twitchId,
            [Summary("通知類型")] TwitchService.NoticeType noticeType,
            [Summary("通知訊息")] string message = "")
        {
            await DeferAsync(true).ConfigureAwait(false);

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var noticeTwitchStreamChannel = db.NoticeTwitchStreamChannels.FirstOrDefault((x) => x.GuildId == Context.Guild.Id && x.NoticeTwitchUserId == twitchId);
                if (noticeTwitchStreamChannel == null)
                {
                    await Context.Interaction.SendErrorAsync($"並未設定 `{twitchId}` 的 Twitch 直播通知\n" +
                        $"請先使用 `/twitch add {twitchId}` 新增通知後再設定通知訊息", true).ConfigureAwait(false);
                }
                else
                {
                    string noticeTypeString = "", result = "";

                    message = message.Trim();
                    switch (noticeType)
                    {
                        case TwitchService.NoticeType.StartStream:
                            noticeTwitchStreamChannel.StartStreamMessage = message;
                            noticeTypeString = "開始直播";
                            break;
                        case TwitchService.NoticeType.EndStream:
                            noticeTwitchStreamChannel.EndStreamMessage = message;
                            noticeTypeString = "結束直播";
                            break;
                        case TwitchService.NoticeType.ChangeStreamData:
                            noticeTwitchStreamChannel.ChangeStreamDataMessage = message;
                            noticeTypeString = "更改直播資料";
                            break;
                    }

                    db.NoticeTwitchStreamChannels.Update(noticeTwitchStreamChannel);
                    db.SaveChanges();

                    if (message == "-")
                    {
                        result = $"已關閉 `{db.GetTwitchUserNameByUserId(twitchId)}` 的 `{noticeTypeString}` 通知";
                    }
                    else if (message != "")
                    {
                        result = $"已設定 `{db.GetTwitchUserNameByUserId(twitchId)}` 的 `{noticeTypeString}` 通知訊息為:\n" +
                            $"{message}";
                    }
                    else
                    {
                        result = $"已清除 `{db.GetTwitchUserNameByUserId(twitchId)}` 的 `{noticeTypeString}` 通知訊息";
                    }

                    await Context.Interaction.SendConfirmAsync(result, true, true).ConfigureAwait(false);
                }
            }
        }

        string GetCurrectMessage(string message)
            => message == "-" ? "(已關閉本類別的通知)" : message;

        [SlashCommand("list-message", "列出已設定的 Twitch 直播通知訊息")]
        public async Task ListMessage([Summary("頁數")] int page = 0)
        {
            try
            {
                using (var db = DataBase.MainDbContext.GetDbContext())
                {
                    if (db.NoticeTwitchStreamChannels.Any((x) => x.GuildId == Context.Guild.Id))
                    {
                        var noticeTwitchStreamChannels = db.NoticeTwitchStreamChannels.Where((x) => x.GuildId == Context.Guild.Id);
                        Dictionary<string, string> dic = new Dictionary<string, string>();

                        foreach (var item in noticeTwitchStreamChannels)
                        {
                            dic.Add(db.GetTwitchUserNameByUserId(item.NoticeTwitchUserId),
                                $"開始直播: {GetCurrectMessage(item.StartStreamMessage)}\n" +
                                $"結束直播: {GetCurrectMessage(item.EndStreamMessage)}\n" +
                                $"更改直播資料: {GetCurrectMessage(item.ChangeStreamDataMessage)}");
                        }

                        try
                        {
                            await Context.SendPaginatedConfirmAsync(page, (page) =>
                            {
                                EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor().WithTitle("Twitch 直播通知訊息清單")
                                    .WithDescription("如果沒訊息的話就代表沒設定\n不用擔心會 Tag 到用戶組，Embed 不會有 Ping 的反應");

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
                        await Context.Interaction.SendErrorAsync($"並未設定 Twitch 直播通知\n" +
                            $"請先使用 `/help get-command-help twitch add` 查看說明並新增 Twitch 直播通知").ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Twitch ListMessage");
                await Context.Interaction.SendErrorAsync("錯誤，請向 Bot 擁有者詢問");
            }
        }
    }
}
