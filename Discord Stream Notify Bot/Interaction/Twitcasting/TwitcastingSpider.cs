using Discord.Interactions;
using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.Interaction.Attribute;
using Microsoft.EntityFrameworkCore;

namespace Discord_Stream_Notify_Bot.Interaction.TwitCasting
{
    [RequireContext(ContextType.Guild)]
    [Group("twitcasting-spider", "TwitCasting 爬蟲設定")]
    [RequireUserPermission(GuildPermission.Administrator)]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public class TwitcastingSpider : TopLevelModule<SharedService.Twitcasting.TwitcastingService>
    {
        private readonly DiscordSocketClient _client;
        private readonly MainDbService _dbService;
        public class GuildTwitCastingSpiderAutocompleteHandler : AutocompleteHandler
        {
            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
            {
                return await Task.Run(async () =>
                {
                    using var db = Bot.DbService.GetDbContext();
                    IQueryable<DataBase.Table.TwitcastingSpider> channelList;

                    if (autocompleteInteraction.User.Id == Bot.ApplicatonOwner.Id)
                    {
                        channelList = db.TwitcastingSpider;
                    }
                    else
                    {
                        if (!(await db.TwitcastingSpider.AsNoTracking().AnyAsync((x) => x.GuildId == autocompleteInteraction.GuildId)))
                            return AutocompletionResult.FromSuccess();

                        channelList = db.TwitcastingSpider.AsNoTracking().Where((x) => x.GuildId == autocompleteInteraction.GuildId);
                    }

                    var channelList2 = new List<DataBase.Table.TwitcastingSpider>();
                    try
                    {
                        string value = autocompleteInteraction.Data.Current.Value.ToString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            foreach (var item in channelList)
                            {
                                if (item.ChannelTitle.Contains(value, StringComparison.CurrentCultureIgnoreCase) || item.ChannelId.Contains(value, StringComparison.CurrentCultureIgnoreCase))
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
                        Log.Error($"GuildTwitCastingSpiderAutocompleteHandler - {ex}");
                    }

                    List<AutocompleteResult> results = new();
                    foreach (var item in channelList2)
                    {
                        results.Add(new AutocompleteResult(item.ChannelTitle, item.ChannelId));
                    }

                    return AutocompletionResult.FromSuccess(results.Take(25));
                });
            }
        }

        public TwitcastingSpider(DiscordSocketClient client, MainDbService dbService)
        {
            _client = client;
            _dbService = dbService;

            _client.ButtonExecuted += async (button) =>
            {
                try
                {
                    if (button.HasResponded || !button.Data.CustomId.StartsWith("spider_tc:"))
                        return;

                    Log.Info($"\"{button.User}\" Click Button: {button.Data.CustomId}");
                    await button.DeferAsync(false);

                    string[] buttonData = button.Data.CustomId.Split(new char[] { ':' });
                    if (buttonData.Length != 3)
                    {
                        await button.SendErrorAsync("此按鈕無法使用", true, false);
                        return;
                    }

                    using var db = _dbService.GetDbContext();
                    var twitcastingSpider = await db.TwitcastingSpider.FirstOrDefaultAsync((x) => x.ChannelId == buttonData[2]);
                    if (twitcastingSpider == null)
                    {
                        await button.SendErrorAsync("找不到此按鈕的頻道，可能已被移除", true, false);
                        return;
                    }

                    if (buttonData[1].Contains("warning"))
                    {
                        twitcastingSpider.IsWarningUser = !twitcastingSpider.IsWarningUser;
                        db.TwitcastingSpider.Update(twitcastingSpider);
                        await db.SaveChangesAsync();

                        await button.SendConfirmAsync($"已切換 `{twitcastingSpider.ChannelTitle}` 為 `" + (twitcastingSpider.IsWarningUser ? "警告" : "普通") + "` 狀態", true, true);
                    }
                    else if (buttonData[1].Contains("record"))
                    {
                        twitcastingSpider.IsRecord = !twitcastingSpider.IsRecord;
                        db.TwitcastingSpider.Update(twitcastingSpider);
                        await db.SaveChangesAsync();

                        await button.SendConfirmAsync($"已切換 `{twitcastingSpider.ChannelTitle}` 為 `" + (twitcastingSpider.IsRecord ? "開啟" : "關閉") + "` 錄影", true, true);
                    }

                    await db.SaveChangesAsync();

                    try
                    {
                        var guild = button.Message.Embeds.First().Fields.FirstOrDefault((x) => x.Name == "伺服器").Value;
                        var user = button.Message.Embeds.First().Fields.FirstOrDefault((x) => x.Name == "執行者").Value;
                        var embed = new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("已新增 TwitCasting 頻道爬蟲")
                            .AddField("頻道", Format.Url(twitcastingSpider.ChannelTitle, $"https://twitcasting.tv/{twitcastingSpider.ChannelId}"), false)
                            .AddField("伺服器", guild, false)
                            .AddField("執行者", user, false)
                            .AddField("頻道狀態", twitcastingSpider.IsWarningUser ? "警告" : "普通", true)
                            .AddField("頻道錄影", twitcastingSpider.IsRecord ? "開啟" : "關閉", true).Build();

                        try
                        {
                            await button.UpdateAsync((func) =>
                            {
                                func.Embed = embed;
                            });
                        }
                        catch
                        {
                            await button.ModifyOriginalResponseAsync((func) =>
                            {
                                func.Embed = embed;
                            });
                        }
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    await button.SendErrorAsync(ex.Message, true);
                    Log.Error(ex.ToString());
                }
            };
        }

        [RequireGuildMemberCount(500)]
        [CommandSummary("新增 TwitCasting 頻道檢測爬蟲\n" +
           "伺服器需大於 500 人才可使用\n" +
           "未來會根據情況增減可新增的頻道數量\n" +
           "如有任何需要請向擁有者詢問")]
        [CommandExample("nana_kaguraaa", "https://twitcasting.tv/nana_kaguraaa")]
        [SlashCommand("add", "新增 TwitCasting 頻道檢測爬蟲")]
        public async Task AddChannelSpider([Summary("頻道網址")] string channelUrl)
        {
            if (!_service.IsEnable)
            {
                await Context.Interaction.SendErrorAsync("此 Bot 的 TwitCasting 功能已關閉，請向 Bot 擁有者確認").ConfigureAwait(false);
                return;
            }

            await DeferAsync(true).ConfigureAwait(false);

            var channelData = await _service.GetChannelIdAndTitleAsync(channelUrl);
            if (string.IsNullOrEmpty(channelData.ChannelTitle))
            {
                await Context.Interaction.SendErrorAsync("錯誤，TwitCasting 找不到該使用者的名稱\n" +
                    "請確認網址是否正確，若正確請向 Bot 擁有者回報", true);
                return;
            }

            using (var db = _dbService.GetDbContext())
            {
                if (await db.TwitcastingSpider.AnyAsync((x) => x.ChannelId == channelData.ChannelId))
                {
                    var item = await db.TwitcastingSpider.FirstOrDefaultAsync((x) => x.ChannelId == channelData.ChannelId);
                    bool isGuildExist = true;
                    string guild = "";

                    try
                    {
                        guild = item.GuildId == 0 ? "Bot擁有者" : $"{_client.GetGuild(item.GuildId).Name}";
                    }
                    catch (Exception)
                    {
                        isGuildExist = false;

                        try
                        {
                            await (await Bot.ApplicatonOwner.CreateDMChannelAsync())
                                .SendMessageAsync(embed: new EmbedBuilder()
                                    .WithOkColor()
                                    .WithTitle("已更新 TwitCasting 爬蟲的持有伺服器")
                                    .AddField("頻道", Format.Url(item.ChannelTitle, $"https://twitcasting.tv/{channelData.ChannelId}"), false)
                                    .AddField("原伺服器", Context.Guild.Id, false)
                                    .AddField("新伺服器", $"{Context.Guild.Name} ({Context.Guild.Id})", false).Build());
                        }
                        catch (Exception ex) { Log.Error(ex.ToString()); }

                        item.GuildId = Context.Guild.Id;
                        db.TwitcastingSpider.Update(item);
                        await db.SaveChangesAsync();
                    }

                    await Context.Interaction.SendConfirmAsync($"`{channelData.ChannelTitle}` 已在爬蟲清單內\n" +
                        $"可直接到通知頻道內使用 `/twitcasting add {channelData.ChannelId}` 開啟通知" +
                        (isGuildExist ? $"\n(由 `{guild}` 設定)" : ""), true).ConfigureAwait(false);
                    return;
                }

                if (db.TwitcastingSpider.AsNoTracking().Count((x) => x.GuildId == Context.Guild.Id) >= 2)
                {
                    await Context.Interaction.SendErrorAsync($"此伺服器已設定 2 個 TwitCasting 爬蟲頻道，請移除後再試\n" +
                        $"如有特殊需求請向 Bot 擁有者詢問\n" +
                        $"(你可使用 `/utility send-message-to-bot-owner` 對擁有者發送訊息)", true).ConfigureAwait(false);
                    return;
                }

                var spider = new DataBase.Table.TwitcastingSpider() { GuildId = Context.Guild.Id, ChannelId = channelData.ChannelId, ChannelTitle = channelData.ChannelTitle };
                if (Context.User.Id == Bot.ApplicatonOwner.Id && !await PromptUserConfirmAsync("設定該爬蟲為本伺服器使用?"))
                    spider.GuildId = 0;

                await db.TwitcastingSpider.AddAsync(spider);
                await db.SaveChangesAsync();

                await Context.Interaction.SendConfirmAsync($"已將 `{channelData.ChannelTitle}` 加入到爬蟲清單內\n" +
                    $"請到通知頻道內使用 `/twitcasting add {channelData.ChannelId}` 來開啟通知", true, true).ConfigureAwait(false);

                try
                {
                    await (await Bot.ApplicatonOwner.CreateDMChannelAsync()).SendMessageAsync(embed: new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("已新增 TwitCasting 頻道爬蟲")
                            .AddField("頻道", Format.Url(channelData.ChannelTitle, $"https://twitcasting.tv/{channelData.ChannelId}"), false)
                            .AddField("伺服器", spider.GuildId != 0 ? $"{Context.Guild.Name} ({Context.Guild.Id})" : "擁有者", false)
                            .AddField("執行者", $"{Context.User.Username} ({Context.User.Id})", false)
                            .AddField("頻道狀態", "普通", true)
                            .AddField("頻道錄影", "關閉", true).Build(),
                        components: new ComponentBuilder()
                            .WithButton("切換頻道狀態", $"spider_tc:warning:{channelData.ChannelId}", ButtonStyle.Danger)
                            .WithButton("切換頻道錄影", $"spider_tc:record:{channelData.ChannelId}", ButtonStyle.Success).Build());
                }
                catch (Exception ex) { Log.Error(ex.ToString()); }
            }
        }

        [CommandSummary("移除 TwitCasting 頻道檢測爬蟲\n" +
            "爬蟲必須由本伺服器新增才可移除")]
        [CommandExample("nana_kaguraaa", "https://twitcasting.tv/nana_kaguraaa")]
        [SlashCommand("remove", "移除 TwitCasting 頻道檢測爬蟲")]
        public async Task RemoveChannelSpider([Summary("頻道網址"), Autocomplete(typeof(GuildTwitCastingSpiderAutocompleteHandler))] string channelUrl)
        {
            await DeferAsync(true).ConfigureAwait(false);

            var channelData = await _service.GetChannelIdAndTitleAsync(channelUrl);
            if (string.IsNullOrEmpty(channelData.ChannelTitle))
            {
                await Context.Interaction.SendErrorAsync("錯誤，TwitCasting 找不到該使用者的名稱\n" +
                    "請確認網址是否正確，若正確請向 Bot 擁有者回報", true);
                return;
            }

            using (var db = _dbService.GetDbContext())
            {
                if (!db.TwitcastingSpider.Any((x) => x.ChannelId == channelData.ChannelId))
                {
                    await Context.Interaction.SendErrorAsync($"並未設定 `{channelData.ChannelId}` 頻道檢測爬蟲...", true).ConfigureAwait(false);
                    return;
                }

                if (Context.Interaction.User.Id != Bot.ApplicatonOwner.Id && !db.TwitcastingSpider.Any((x) => x.ChannelId == channelData.ChannelId && x.GuildId == Context.Guild.Id))
                {
                    await Context.Interaction.SendErrorAsync($"該頻道爬蟲並非本伺服器新增，無法移除", true).ConfigureAwait(false);
                    return;
                }

                db.TwitcastingSpider.Remove(db.TwitcastingSpider.First((x) => x.ChannelId == channelData.ChannelId));
                await db.SaveChangesAsync();
            }
            await Context.Interaction.SendConfirmAsync($"已移除 {channelData.ChannelTitle}", true).ConfigureAwait(false);

            try
            {
                await (await Bot.ApplicatonOwner.CreateDMChannelAsync()).SendMessageAsync(embed: new EmbedBuilder()
                    .WithErrorColor()
                    .WithTitle("已移除 TwitCasting 頻道爬蟲")
                    .AddField("頻道", Format.Url(channelData.ChannelTitle, $"https://twitcasting.tv/{channelData.ChannelId}"), false)
                    .AddField("伺服器", $"{Context.Guild.Name} ({Context.Guild.Id})", false)
                    .AddField("執行者", $"{Context.User.Username} ({Context.User.Id})", false).Build());
            }
            catch (Exception ex) { Log.Error(ex.ToString()); }
        }

        [SlashCommand("list", "顯示已加入爬蟲檢測的頻道")]
        public async Task ListChannelSpider([Summary("頁數")] int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = _dbService.GetDbContext())
            {
                var list = db.TwitcastingSpider.AsNoTracking().Where((x) => !x.IsWarningUser).Select((x) => Format.Url(x.ChannelTitle, $"https://twitcasting.tv/{x.ChannelId}") +
                    $" 由 `" + (x.GuildId == 0 ? "Bot 擁有者" : (_client.GetGuild(x.GuildId) != null ? _client.GetGuild(x.GuildId).Name : "已退出的伺服器")) + "` 新增");
                int warningChannelNum = db.TwitcastingSpider.AsNoTracking().Count((x) => x.IsWarningUser);

                await Context.SendPaginatedConfirmAsync(page, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("TwitCasting 直播爬蟲清單")
                        .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20)))
                        .WithFooter($"{Math.Min(list.Count(), (page + 1) * 20)} / {list.Count()}個頻道 ({warningChannelNum}個非認可的爬蟲)");
                }, list.Count(), 10, false).ConfigureAwait(false);
            }
        }

        [SlashCommand("list-not-trusted", "顯示已加入但為警告狀態的爬蟲檢測頻道 (本清單可能內含中之人或前世的頻道)")]
        public async Task ListNotTrustedChannelSpider([Summary("頁數")] int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = _dbService.GetDbContext())
            {
                var list = db.TwitcastingSpider.AsNoTracking().Where((x) => x.IsWarningUser).Select((x) => Format.Url(x.ChannelTitle, $"https://twitcasting.tv/{x.ChannelId}") +
                    $" 由 `" + (x.GuildId == 0 ? "Bot 擁有者" : (_client.GetGuild(x.GuildId) != null ? _client.GetGuild(x.GuildId).Name : "已退出的伺服器")) + "` 新增");

                await Context.SendPaginatedConfirmAsync(page, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("警告的爬蟲清單")
                        .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20)))
                        .WithFooter($"{Math.Min(list.Count(), (page + 1) * 20)} / {list.Count()} 個頻道");
                }, list.Count(), 10, false, true).ConfigureAwait(false);
            }
        }
    }
}