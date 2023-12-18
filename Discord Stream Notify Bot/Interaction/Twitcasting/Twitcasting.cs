using Discord.Interactions;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction.Attribute;

namespace Discord_Stream_Notify_Bot.Interaction.Twitcasting
{
    [EnabledInDm(false)]
    [Group("twitcasting", "Twitcasting 通知")]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    [DefaultMemberPermissions(GuildPermission.ManageMessages)]
    public class Twitcasting : TopLevelModule<SharedService.Twitcasting.TwitcastingService>
    {
        private readonly DiscordSocketClient _client;

        public class GuildNoticeTwitcastingChannelIdAutocompleteHandler : AutocompleteHandler
        {
            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
            {
                return await Task.Run(() =>
                {
                    using var db = DataBase.DBContext.GetDbContext();
                    if (!db.NoticeTwitcastingStreamChannels.Any((x) => x.GuildId == context.Guild.Id))
                        return AutocompletionResult.FromSuccess();

                    var channelIdList = db.NoticeTwitcastingStreamChannels.Where((x) => x.GuildId == context.Guild.Id).Select((x) => new KeyValuePair<string, string>(db.GetTwitcastingChannelTitleByChannelId(x.ChannelId), x.ChannelId));

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
                        Log.Error($"GuildNoticeTwitcastingChannelIdAutocompleteHandler - {ex}");
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

        public Twitcasting(DiscordSocketClient client)
        {
            _client = client;
        }

        [CommandExample("nana_kaguraaa", "https://twitcasting.tv/nana_kaguraaa")]
        [SlashCommand("add", "新增 Twitcasting 直播通知的頻道")]
        public async Task AddChannel([Summary("頻道網址")] string channelUrl, [Summary("發送通知的頻道")] IChannel channel)
        {
            await DeferAsync(true).ConfigureAwait(false);

            if (channel.GetChannelType() != ChannelType.Text && channel.GetChannelType() != ChannelType.News)
            {
                await Context.Interaction.SendErrorAsync($"`{channel}` 非可接受的頻道類型，僅可接受文字頻道或公告頻道", true);
                return;
            }

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

            var channelData = await _service.GetChannelIdAndTitleAsync(channelUrl);
            if (string.IsNullOrEmpty(channelData.ChannelTitle))
            {
                await Context.Interaction.SendErrorAsync("Twitcasting 上找不到該使用者的名稱\n" +
                    "這不是 Twitch 直播通知指令!!!!!!!!!!!!!!!!!!\n" +
                    "請確認網址是否正確，若正確請向Bot擁有者回報", true);
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var noticeTwitcastingStreamChannel = db.NoticeTwitcastingStreamChannels.FirstOrDefault((x) => x.GuildId == Context.Guild.Id && x.ChannelId == channelData.ChannelId);
                if (noticeTwitcastingStreamChannel != null)
                {
                    if (await PromptUserConfirmAsync($"`{channelData.ChannelTitle}` 已在直播通知清單內，是否覆蓋設定?").ConfigureAwait(false))
                    {
                        noticeTwitcastingStreamChannel.DiscordChannelId = textChannel.Id;
                        db.NoticeTwitcastingStreamChannels.Update(noticeTwitcastingStreamChannel);
                        await Context.Interaction.SendConfirmAsync($"已將 `{channelData.ChannelTitle}` 的通知頻道變更至: {textChannel}", true, true).ConfigureAwait(false);
                    }
                    else return;
                }
                else
                {
                    string addString = "";
                    if (!db.TwitcastingSpider.Any((x) => x.ChannelId == channelData.ChannelId))
                        addString += $"\n\n(注意: 該頻道未加入爬蟲清單\n如長時間無通知請使用 `/help get-command-help twitcasting-spider add` 查看說明並加入爬蟲)";
                    db.NoticeTwitcastingStreamChannels.Add(new NoticeTwitcastingStreamChannel() { GuildId = Context.Guild.Id, DiscordChannelId = textChannel.Id, ChannelId = channelData.ChannelId });
                    await Context.Interaction.SendConfirmAsync($"已將 `{channelData.ChannelTitle}` 加入到Twitcasting通知頻道清單內{addString}", true, true).ConfigureAwait(false);
                }

                db.SaveChanges();
            }
        }

        [CommandExample("nana_kaguraaa", "https://twitcasting.tv/nana_kaguraaa")]
        [SlashCommand("remove", "移除 Twitcasting 直播通知的頻道")]
        public async Task RemoveChannel([Summary("頻道網址"), Autocomplete(typeof(GuildNoticeTwitcastingChannelIdAutocompleteHandler))] string channelUrl)
        {
            await DeferAsync(true).ConfigureAwait(false);

            var channelData = await _service.GetChannelIdAndTitleAsync(channelUrl);
            if (string.IsNullOrEmpty(channelData.ChannelTitle))
            {
                await Context.Interaction.SendErrorAsync("錯誤，Twitcasting 找不到該使用者的名稱\n" +
                    "請確認網址是否正確，若正確請向 Bot 擁有者回報", true);
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (!db.NoticeTwitcastingStreamChannels.Any((x) => x.GuildId == Context.Guild.Id))
                {
                    await Context.Interaction.SendErrorAsync("並未設定直播通知...", true).ConfigureAwait(false);
                    return;
                }

                if (!db.NoticeTwitcastingStreamChannels.Any((x) => x.GuildId == Context.Guild.Id && x.ChannelId == channelData.ChannelId))
                {
                    await Context.Interaction.SendErrorAsync($"並未設定 `{channelData.ChannelId}` 的直播通知...", true).ConfigureAwait(false);
                    return;
                }
                else
                {
                    db.NoticeTwitcastingStreamChannels.Remove(db.NoticeTwitcastingStreamChannels.First((x) => x.GuildId == Context.Guild.Id && x.ChannelId == channelData.ChannelId));
                    db.SaveChanges();
                    await Context.Interaction.SendConfirmAsync($"已移除 `{channelData.ChannelTitle}`", true, true).ConfigureAwait(false);
                }
            }
        }

        [SlashCommand("list", "顯示現在已加入通知清單的 Twitcasting 直播頻道")]
        public async Task ListChannel([Summary("頁數")] int page = 0)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var list = Queryable.Where(db.NoticeTwitcastingStreamChannels, (x) => x.GuildId == Context.Guild.Id)
                .Select((x) => $"`{db.GetTwitcastingChannelTitleByChannelId(x.ChannelId)}` => <#{x.DiscordChannelId}>").ToList();
                if (list.Count() == 0) { await Context.Interaction.SendErrorAsync("Twitcasting 直播通知清單為空").ConfigureAwait(false); return; }

                await Context.SendPaginatedConfirmAsync(page, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("Twitcasting 直播通知清單")
                        .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20)))
                        .WithFooter($"{Math.Min(list.Count, (page + 1) * 20)} / {list.Count}個頻道");
                }, list.Count, 20, false);
            }
        }

        [RequireBotPermission(GuildPermission.MentionEveryone)]
        [CommandSummary("設定通知訊息\n" +
            "不輸入通知訊息的話則會關閉通知訊息\n" +
            "需先新增直播通知後才可設定通知訊息(`/help get-command-help twitcasting add`)\n\n" +
            "(考慮到有伺服器需 Ping 特定用戶組的情況，故 Bot 需提及所有身分組權限)")]
        [CommandExample("nana_kaguraaa 開台啦", "https://twitcasting.tv/nana_kaguraaa 開台啦")]
        [SlashCommand("set-message", "設定通知訊息")]
        public async Task SetMessage([Summary("頻道網址"), Autocomplete(typeof(GuildNoticeTwitcastingChannelIdAutocompleteHandler))] string channelUrl, [Summary("通知訊息")] string message = "")
        {
            await DeferAsync(true).ConfigureAwait(false);

            var channelData = await _service.GetChannelIdAndTitleAsync(channelUrl);
            if (string.IsNullOrEmpty(channelData.ChannelTitle))
            {
                await Context.Interaction.SendErrorAsync("錯誤，Twitcasting 找不到該使用者的名稱\n" +
                    "請確認網址是否正確，若正確請向 Bot 擁有者回報", true);
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (db.NoticeTwitcastingStreamChannels.Any((x) => x.GuildId == Context.Guild.Id && x.ChannelId == channelData.ChannelId))
                {
                    var noticeStreamChannel = db.NoticeTwitcastingStreamChannels.First((x) => x.GuildId == Context.Guild.Id && x.ChannelId == channelData.ChannelId);

                    noticeStreamChannel.StartStreamMessage = message.Trim();
                    db.NoticeTwitcastingStreamChannels.Update(noticeStreamChannel);
                    db.SaveChanges();

                    if (message != "") await Context.Interaction.SendConfirmAsync($"已設定 `{channelData.ChannelTitle}` 的 Twitcasting 直播通知訊息為:\n{message}", true, true).ConfigureAwait(false);
                    else await Context.Interaction.SendConfirmAsync($"已取消 `{channelData.ChannelTitle}` 的 Twitcasting 直播通知訊息", true, true).ConfigureAwait(false);
                }
                else
                {
                    await Context.Interaction.SendErrorAsync($"並未設定 `{channelData.ChannelTitle}` 的 Twitcasting 直播通知\n" +
                        $"請先使用 `/twitcasting add {channelData.ChannelId}` 新增通知後再設定通知訊息", true).ConfigureAwait(false);
                }
            }
        }

        [SlashCommand("list-message", "列出已設定的 Twitcasting 直播通知訊息")]
        public async Task ListMessage([Summary("頁數")] int page = 0)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (db.NoticeTwitcastingStreamChannels.Any((x) => x.GuildId == Context.Guild.Id))
                {
                    var noticeTwitterSpaces = db.NoticeTwitcastingStreamChannels.Where((x) => x.GuildId == Context.Guild.Id);
                    Dictionary<string, string> dic = new Dictionary<string, string>();

                    foreach (var item in noticeTwitterSpaces)
                    {
                        string message = string.IsNullOrWhiteSpace(item.StartStreamMessage) ? "無" : item.StartStreamMessage;
                        dic.Add(db.GetTwitcastingChannelTitleByChannelId(item.ChannelId), message);
                    }

                    try
                    {
                        await Context.SendPaginatedConfirmAsync(page, (page) =>
                        {
                            EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor().WithTitle("Twitcasting 直播通知訊息清單")
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
                    await Context.Interaction.SendErrorAsync($"並未設定 Twitcasting 直播通知\n" +
                        $"請先使用 `/help get-command-help twitcasting add` 查看說明並新增 Twitcasting 直播通知").ConfigureAwait(false);
                }
            }
        }
    }
}