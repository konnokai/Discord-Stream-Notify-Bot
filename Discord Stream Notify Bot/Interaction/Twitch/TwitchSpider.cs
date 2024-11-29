using Discord.Interactions;
using Discord_Stream_Notify_Bot.Interaction.Attribute;
using System.Diagnostics;

namespace Discord_Stream_Notify_Bot.Interaction.Twitch
{
    [RequireContext(ContextType.Guild)]
    [Group("twitch-spider", "Twitch 爬蟲設定")]
    [RequireUserPermission(GuildPermission.Administrator)]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public class TwitchSpider : TopLevelModule<SharedService.Twitch.TwitchService>
    {
        private readonly DiscordSocketClient _client;
        public class GuildTwitchSpiderAutocompleteHandler : AutocompleteHandler
        {
            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
            {
                return await Task.Run(() =>
                {
                    using var db = DataBase.MainDbContext.GetDbContext();
                    IQueryable<DataBase.Table.TwitchSpider> channelList;

                    if (autocompleteInteraction.User.Id == Program.ApplicatonOwner.Id)
                    {
                        channelList = db.TwitchSpider;
                    }
                    else
                    {
                        if (!db.TwitchSpider.Any((x) => x.GuildId == autocompleteInteraction.GuildId))
                            return AutocompletionResult.FromSuccess();

                        channelList = db.TwitchSpider.Where((x) => x.GuildId == autocompleteInteraction.GuildId);
                    }

                    var channelList2 = new List<DataBase.Table.TwitchSpider>();
                    try
                    {
                        string value = autocompleteInteraction.Data.Current.Value.ToString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            foreach (var item in channelList)
                            {
                                if (item.UserName.Contains(value, StringComparison.CurrentCultureIgnoreCase) || item.UserLogin.Contains(value, StringComparison.CurrentCultureIgnoreCase))
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
                        Log.Error($"GuildTwitchSpiderAutocompleteHandler - {ex}");
                    }

                    List<AutocompleteResult> results = new();
                    foreach (var item in channelList2)
                    {
                        results.Add(new AutocompleteResult(item.UserName, item.UserId));
                    }

                    return AutocompletionResult.FromSuccess(results.Take(25));
                });
            }
        }

        public TwitchSpider(DiscordSocketClient client)
        {
            _client = client;

            _client.ButtonExecuted += async (button) =>
            {
                try
                {
                    if (button.HasResponded || !button.Data.CustomId.StartsWith("spider_twitch:"))
                        return;

                    Log.Info($"\"{button.User}\" Click Button: {button.Data.CustomId}");
                    await button.DeferAsync(false);

                    string[] buttonData = button.Data.CustomId.Split(new char[] { ':' });
                    if (buttonData.Length != 3)
                    {
                        await button.SendErrorAsync("此按鈕無法使用", true, false);
                        return;
                    }

                    using var db = DataBase.MainDbContext.GetDbContext();
                    var twitchSpider = db.TwitchSpider.FirstOrDefault((x) => x.UserId == buttonData[2]);
                    if (twitchSpider == null)
                    {
                        await button.SendErrorAsync("找不到此按鈕的頻道，可能已被移除", true, false);
                        return;
                    }

                    if (buttonData[1].Contains("warning"))
                    {
                        twitchSpider.IsWarningUser = !twitchSpider.IsWarningUser;
                        db.TwitchSpider.Update(twitchSpider);
                        db.SaveChanges();

                        await button.SendConfirmAsync($"已切換 `{twitchSpider.UserName}` 為 `" + (twitchSpider.IsWarningUser ? "警告" : "普通") + "` 狀態", true, true);
                    }
                    else if (buttonData[1].Contains("record"))
                    {
                        twitchSpider.IsRecord = !twitchSpider.IsRecord;
                        db.TwitchSpider.Update(twitchSpider);
                        db.SaveChanges();

                        await button.SendConfirmAsync($"已切換 `{twitchSpider.UserName}` 為 `" + (twitchSpider.IsRecord ? "開啟" : "關閉") + "` 錄影", true, true);
                    }

                    db.SaveChanges();

                    try
                    {
                        var guild = button.Message.Embeds.First().Fields.FirstOrDefault((x) => x.Name == "伺服器").Value;
                        var user = button.Message.Embeds.First().Fields.FirstOrDefault((x) => x.Name == "執行者").Value;
                        var embed = new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("已新增 Twitch 頻道爬蟲")
                            .AddField("頻道", Format.Url(twitchSpider.UserName, $"https://twitch.tv/{twitchSpider.UserLogin}"), false)
                            .AddField("伺服器", guild, false)
                            .AddField("執行者", user, false)
                            .AddField("頻道狀態", twitchSpider.IsWarningUser ? "警告" : "普通", true)
                            .AddField("頻道錄影", twitchSpider.IsRecord ? "開啟" : "關閉", true).Build();

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
                    Log.Error(ex.Demystify().ToString());
                }
            };
        }

        [RequireGuildMemberCount(200)]
        [CommandSummary("新增 Twitch 頻道爬蟲\n" +
           "伺服器需大於 200 人才可使用\n" +
           "未來會根據情況增減可新增的頻道數量\n" +
           "如有任何需要請向擁有者詢問")]
        [CommandExample("998rrr", "https://twitch.tv/998rrr")]
        [SlashCommand("add", "新增 Twitch 頻道爬蟲")]
        public async Task AddChannelSpider([Summary("頻道網址")] string twitchUrl)
        {
            if (!_service.IsEnable)
            {
                await Context.Interaction.SendErrorAsync("此 Bot 的 Twitch 功能已關閉，請向 Bot 擁有者確認").ConfigureAwait(false);
                return;
            }

            await DeferAsync(true).ConfigureAwait(false);

            var userData = await _service.GetUserAsync(twitchUserLogin: _service.GetUserLoginByUrl(twitchUrl));
            if (userData == null)
            {
                await Context.Interaction.SendErrorAsync("錯誤，Twitch 使用者資料獲取失敗\n" +
                        "請確認網址是否正確，若正確請向 Bot 擁有者回報", true);
                return;
            }

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                if (db.TwitchSpider.Any((x) => x.UserId == userData.Id))
                {
                    var item = db.TwitchSpider.FirstOrDefault((x) => x.UserId == userData.Id);
                    bool isGuildExist = true;
                    string guild = "";

                    try
                    {
                        guild = item.GuildId == 0 ? "Bot 擁有者" : $"{_client.GetGuild(item.GuildId).Name}";
                    }
                    catch (Exception)
                    {
                        isGuildExist = false;

                        item.GuildId = Context.Guild.Id;
                        db.TwitchSpider.Update(item);
                        db.SaveChanges();

                        try
                        {
                            await (await Program.ApplicatonOwner.CreateDMChannelAsync())
                                .SendMessageAsync(embed: new EmbedBuilder()
                                    .WithOkColor()
                                    .WithTitle("已更新 Twitch 爬蟲的持有伺服器")
                                    .AddField("頻道", Format.Url(item.UserName, $"https://twitch.tv/{userData.Login}"), false)
                                    .AddField("原伺服器", Context.Guild.Id, false)
                                    .AddField("新伺服器", $"{Context.Guild.Name} ({Context.Guild.Id})", false).Build());
                        }
                        catch (Exception ex) { Log.Error(ex.Demystify(), "Update Twitch Spider GuildId Error"); }
                    }

                    await Context.Interaction.SendConfirmAsync($"`{userData.DisplayName}` 已在爬蟲清單內\n" +
                        $"可直接到通知頻道內使用 `/twitch add {userData.Login}` 開啟通知" +
                        (isGuildExist ? $"\n(由 `{guild}` 設定)" : ""), true).ConfigureAwait(false);
                    return;
                }

                if (db.TwitchSpider.Count((x) => x.GuildId == Context.Guild.Id) >= 3)
                {
                    await Context.Interaction.SendErrorAsync($"此伺服器已設定 3 個 Twitch 爬蟲頻道，請移除後再試\n" +
                        $"如有特殊需求請向 Bot 擁有者詢問\n" +
                        $"(你可使用 `/utility send-message-to-bot-owner` 對擁有者發送訊息)", true).ConfigureAwait(false);
                    return;
                }

                var spider = new DataBase.Table.TwitchSpider()
                {
                    GuildId = Context.Guild.Id,
                    UserId = userData.Id,
                    UserLogin = userData.Login,
                    UserName = userData.DisplayName,
                    ProfileImageUrl = userData.ProfileImageUrl,
                    OfflineImageUrl = userData.OfflineImageUrl
                };

                if (Context.User.Id == Program.ApplicatonOwner.Id && !await PromptUserConfirmAsync("設定該爬蟲為本伺服器使用?"))
                    spider.GuildId = 0;

                db.TwitchSpider.Add(spider);
                db.SaveChanges();

                await Context.Interaction.SendConfirmAsync($"已將 `{userData.DisplayName}` 加入到爬蟲清單內\n" +
                    $"請到通知頻道內使用 `/twitch add {userData.Login}` 來開啟通知", true, true).ConfigureAwait(false);

                try
                {
                    await (await Program.ApplicatonOwner.CreateDMChannelAsync()).SendMessageAsync(embed: new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("已新增 Twitch 頻道爬蟲")
                            .AddField("頻道", Format.Url(userData.DisplayName, $"https://twitch.tv/{userData.Login}"), false)
                            .AddField("伺服器", spider.GuildId != 0 ? $"{Context.Guild.Name} ({Context.Guild.Id})" : "擁有者", false)
                            .AddField("執行者", $"{Context.User} ({Context.User.Id})", false)
                            .AddField("頻道狀態", "普通", true)
                            .AddField("頻道錄影", "關閉", true).Build(),
                        components: new ComponentBuilder()
                            .WithButton("切換頻道狀態", $"spider_twitch:warning:{userData.Id}", ButtonStyle.Danger)
                            .WithButton("切換頻道錄影", $"spider_twitch:record:{userData.Id}", ButtonStyle.Success).Build());
                }
                catch (Exception ex) { Log.Error(ex.Demystify().ToString()); }
            }
        }

        [CommandSummary("移除 Twitch 頻道檢測爬蟲\n" +
            "爬蟲必須由本伺服器新增才可移除")]
        [CommandExample("998rrr", "https://twitch.tv/998rrr")]
        [SlashCommand("remove", "移除 Twitch 頻道爬蟲")]
        public async Task RemoveChannelSpider([Summary("頻道網址", "userName"), Autocomplete(typeof(GuildTwitchSpiderAutocompleteHandler))] string twitchId)
        {
            await DeferAsync(true).ConfigureAwait(false);

            DataBase.Table.TwitchSpider twitchSpider = null;
            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                if (!db.TwitchSpider.Any((x) => x.UserId == twitchId))
                {
                    await Context.Interaction.SendErrorAsync($"並未設定 `{twitchId}` 頻道檢測爬蟲...", true).ConfigureAwait(false);
                    return;
                }

                if (Context.Interaction.User.Id != Program.ApplicatonOwner.Id && !db.TwitchSpider.Any((x) => x.UserId == twitchId && x.GuildId == Context.Guild.Id))
                {
                    await Context.Interaction.SendErrorAsync($"該頻道爬蟲並非本伺服器新增，無法移除", true).ConfigureAwait(false);
                    return;
                }

                twitchSpider = db.TwitchSpider.First((x) => x.UserId == twitchId);
                db.TwitchSpider.Remove(twitchSpider);
                db.SaveChanges();
            }

            await Context.Interaction.SendConfirmAsync($"已移除 {twitchSpider?.UserName}", true).ConfigureAwait(false);

            try
            {
                await (await Program.ApplicatonOwner.CreateDMChannelAsync()).SendMessageAsync(embed: new EmbedBuilder()
                    .WithErrorColor()
                    .WithTitle("已移除 Twitch 頻道爬蟲")
                    .AddField("頻道", Format.Url(twitchSpider?.UserName, $"https://twitch.tv/{twitchSpider.UserLogin}"), false)
                    .AddField("伺服器", $"{Context.Guild.Name} ({Context.Guild.Id})", false)
                    .AddField("執行者", $"{Context.User.Username} ({Context.User.Id})", false).Build());
            }
            catch (Exception ex) { Log.Error(ex.Demystify().ToString()); }
        }

        [SlashCommand("list", "顯示已加入爬蟲檢測的頻道")]
        public async Task ListChannelSpider([Summary("頁數")] int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                try
                {
                    var list = db.TwitchSpider.Where((x) => !x.IsWarningUser).Select((x) => Format.Url(x.UserName, $"https://twitch.tv/{x.UserLogin}") +
                        $" 由 `" + (x.GuildId == 0 ? "Bot 擁有者" : (_client.GetGuild(x.GuildId) != null ? _client.GetGuild(x.GuildId).Name : "已退出的伺服器")) + "` 新增");
                    int warningChannelNum = db.TwitchSpider.Count((x) => x.IsWarningUser);

                    await Context.SendPaginatedConfirmAsync(page, page =>
                    {
                        return new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("Twitch 直播爬蟲清單")
                            .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20)))
                            .WithFooter($"{Math.Min(list.Count(), (page + 1) * 20)} / {list.Count()}個頻道 ({warningChannelNum}個非認可的爬蟲)");
                    }, list.Count(), 10, false).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Demystify(), $"Twitch-Spider-List Error");
                    await Context.Interaction.SendErrorAsync("指令執行失敗", false, true);
                }
            }
        }

        [SlashCommand("list-not-trusted", "顯示已加入但為警告狀態的爬蟲檢測頻道 (本清單可能內含中之人或前世的頻道)")]
        public async Task ListNotTrustedChannelSpider([Summary("頁數")] int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var list = db.TwitchSpider.Where((x) => x.IsWarningUser).Select((x) => Format.Url(x.UserName, $"https://twitch.tv/{x.UserLogin}") +
                    $" 由 `" + (x.GuildId == 0 ? "Bot 擁有者" : (_client.GetGuild(x.GuildId) != null ? _client.GetGuild(x.GuildId).Name : "已退出的伺服器")) + "` 新增");

                await Context.SendPaginatedConfirmAsync(page, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("警告的爬蟲清單")
                        .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20)))
                        .WithFooter($"{Math.Min(list.Count(), (page + 1) * 20)} / {list.Count()}個頻道");
                }, list.Count(), 10, false, true).ConfigureAwait(false);
            }
        }
    }
}
