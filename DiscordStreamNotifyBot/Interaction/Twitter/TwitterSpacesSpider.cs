using Discord.Interactions;
using DiscordStreamNotifyBot.DataBase;
using DiscordStreamNotifyBot.DataBase.Table;
using DiscordStreamNotifyBot.Interaction.Attribute;
using static DiscordStreamNotifyBot.Interaction.Twitter.TwitterSpaces;

namespace DiscordStreamNotifyBot.Interaction.Twitter
{
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.Administrator)]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [Group("twitter-spider", "Twiiter Space 爬蟲設定")]
    public class TwitterSpacesSpider : TopLevelModule<SharedService.Twitter.TwitterSpacesService>
    {
        private readonly DiscordSocketClient _client;
        private readonly MainDbService _dbService;
        public TwitterSpacesSpider(DiscordSocketClient client, MainDbService dbService)
        {
            _client = client;
            _dbService = dbService;
        }

        [RequireGuildMemberCount(500)]
        [CommandSummary("新增推特語音空間爬蟲\n" +
            "(請使用@後面的使用者名稱來新增)\n\n" +
           "**禁止新增非 VTuber 的推主**\n" +
            "每個伺服器可新增最多五個頻道爬蟲\n" +
            "伺服器需大於 500 人才可使用\n" +
            "未來會根據情況增減可新增的頻道數量\n" +
            "如有任何需要請向擁有者詢問\n")]
        [CommandExample("_998rrr_", "@inui_toko")]
        [SlashCommand("add", "新增推特語音空間爬蟲")]
        public async Task AddSpider([Summary("推特使用者名稱")] string userScreenName)
        {
            await DeferAsync(true).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Interaction.SendErrorAsync("使用者名稱不可空白", true).ConfigureAwait(false);
                return;
            }

            if (!_service.IsEnable)
            {
                await Context.Interaction.SendErrorAsync("此Bot的Twitter功能已關閉，請向擁有者確認", true).ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "");

            using (var db = _dbService.GetDbContext())
            {
                var user = await _service.GetTwitterUserAsync(userScreenName);

                if (user == null)
                {
                    await Context.Interaction.SendErrorAsync($"`{userScreenName}` 不存在此使用者", true).ConfigureAwait(false);
                    return;
                }

                if (db.TwitterSpaceSpider.Any((x) => x.UserId == user.RestId))
                {
                    var item = db.TwitterSpaceSpider.FirstOrDefault((x) => x.UserId == user.RestId);
                    bool isGuildExist = true;
                    string guild = "";

                    try
                    {
                        guild = item.GuildId == 0 ? "Bot 擁有者" : $"{_client.GetGuild(item.GuildId).Name}";
                    }
                    catch (Exception)
                    {
                        isGuildExist = false;

                        try
                        {
                            await (await Bot.ApplicatonOwner.CreateDMChannelAsync())
                                .SendMessageAsync(embed: new EmbedBuilder()
                                    .WithOkColor()
                                    .WithTitle("已更新推特語音爬蟲的持有伺服器")
                                    .AddField("推主", Format.Url(userScreenName, $"https://twitter.com/{userScreenName}"), false)
                                    .AddField("原伺服器", Context.Guild.Id, false)
                                    .AddField("新伺服器", $"{Context.Guild.Name} ({Context.Guild.Id})", false).Build());
                        }
                        catch (Exception ex) { Log.Error(ex.ToString()); }

                        item.GuildId = Context.Guild.Id;
                        db.TwitterSpaceSpider.Update(item);
                        db.SaveChanges();
                    }

                    await Context.Interaction.SendConfirmAsync($"`{userScreenName}` 已在爬蟲清單內\n" +
                        $"可直接到通知頻道內使用 `/twitter-space add-space-notice {userScreenName}` 開啟通知\n" +
                        (isGuildExist ? $"\n(由 `{guild}` 設定)" : ""), true, true).ConfigureAwait(false);
                    return;
                }

                if (user.Legacy.Protected.HasValue && user.Legacy.Protected.Value)
                {
                    await Context.Interaction.SendErrorAsync($"使用者已開啟推文保護，無法新增", true).ConfigureAwait(false);
                    return;
                }

                // 取得最大數量設定
                var guildConfig = db.GuildConfig.AsNoTracking().FirstOrDefault((x) => x.GuildId == Context.Guild.Id);
                int maxCount = 3;
                if (guildConfig != null && guildConfig.MaxTwitterSpaceSpiderCount > 0)
                    maxCount = (int)guildConfig.MaxTwitterSpaceSpiderCount;

                if (!DiscordStreamNotifyBot.Utility.OfficialGuildContains(Context.Guild.Id) && db.TwitterSpaceSpider.AsNoTracking().Count((x) => x.GuildId == Context.Guild.Id) >= maxCount)
                {
                    await Context.Interaction.SendErrorAsync($"此伺服器已設定 {maxCount} 個推特語音空間爬蟲，請移除後再試\n" +
                        $"如有特殊需求請向 Bot 擁有者詢問\n" +
                        $"(你可使用 `/utility send-message-to-bot-owner` 對擁有者發送訊息)", true).ConfigureAwait(false);
                    return;
                }

                var spider = new TwitterSpaceSpider() { GuildId = Context.User.Id == Bot.ApplicatonOwner.Id ? 0 : Context.Guild.Id, UserId = user.RestId, UserName = user.Legacy.Name, UserScreenName = user.Legacy.ScreenName };
                if (Context.User.Id == Bot.ApplicatonOwner.Id && !await PromptUserConfirmAsync("設定該爬蟲為本伺服器使用?"))
                    spider.GuildId = 0;

                db.TwitterSpaceSpider.Add(spider);
                db.SaveChanges();

                await Context.Interaction.SendConfirmAsync($"已將 `{userScreenName}` 加入到推特語音爬蟲清單內\n" +
                    $"請到通知頻道內使用 `/twitter-space add {userScreenName}` 來開啟通知", true, true).ConfigureAwait(false);

                try
                {

                    await (await Bot.ApplicatonOwner.CreateDMChannelAsync()).SendMessageAsync(embed: new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("已新增推特語音爬蟲")
                        .AddField("推主", Format.Url(userScreenName, $"https://twitter.com/{userScreenName}"), false)
                        .AddField("伺服器", $"{Context.Guild.Name} ({Context.Guild.Id})", false)
                        .AddField("執行者", $"{Context.User.Username} ({Context.User.Id})", false).Build());
                }
                catch (Exception ex) { Log.Error(ex.ToString()); }
            }
        }

        [CommandSummary("移除推特語音空間爬蟲\n" +
           "爬蟲必須由本伺服器新增才可移除")]
        [CommandExample("_998rrr_", "@inui_toko")]
        [SlashCommand("remove", "移除推特語音空間爬蟲")]
        public async Task RemoveSpider([Summary("推特使用者名稱"), Autocomplete(typeof(GuildTwitterSpaceSpiderAutocompleteHandler))] string userScreenName)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Interaction.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "");

            using (var db = _dbService.GetDbContext())
            {
                if (!db.TwitterSpaceSpider.Any((x) => x.UserScreenName == userScreenName))
                {
                    await Context.Interaction.SendErrorAsync($"並未設定 {userScreenName} 語音空間檢測爬蟲...").ConfigureAwait(false);
                    return;
                }

                if (Context.User.Id != Bot.ApplicatonOwner.Id && !db.TwitterSpaceSpider.Any((x) => x.UserScreenName == userScreenName && x.GuildId == Context.Guild.Id))
                {
                    await Context.Interaction.SendErrorAsync($"該語音空間爬蟲並非本伺服器新增，無法移除").ConfigureAwait(false);
                    return;
                }

                db.TwitterSpaceSpider.Remove(db.TwitterSpaceSpider.First((x) => x.UserScreenName == userScreenName));
                db.SaveChanges();

                await Context.Interaction.SendConfirmAsync($"已移除 {userScreenName}", false, true).ConfigureAwait(false);

                try
                {
                    await (await Bot.ApplicatonOwner.CreateDMChannelAsync()).SendMessageAsync(embed: new EmbedBuilder()
                        .WithErrorColor()
                        .WithTitle("已移除推特語音爬蟲")
                        .AddField("推主", Format.Url(userScreenName, $"https://twitter.com/{userScreenName}"), false)
                        .AddField("伺服器", $"{Context.Guild.Name} ({Context.Guild.Id})", false)
                        .AddField("執行者", $"{Context.User.Username} ({Context.User.Id})", false).Build());
                }
                catch (Exception ex) { Log.Error(ex.ToString()); }
            }
        }

        [SlashCommand("list", "顯示推特語音空間爬蟲")]
        public async Task ListSpider([Summary("頁數")] int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = _dbService.GetDbContext())
            {
                try
                {
                    var list = db.TwitterSpaceSpider.Where((x) => !x.IsWarningUser).Select((x) => Format.Url(x.UserScreenName, $"https://twitter.com/{x.UserScreenName}") +
                    $" 由 `" + (x.GuildId == 0 ? "Bot 擁有者" : (_client.GetGuild(x.GuildId) != null ? _client.GetGuild(x.GuildId).Name : "已退出的伺服器")) + "` 新增");
                    int warningChannelNum = db.TwitterSpaceSpider.Count((x) => x.IsWarningUser);

                    await Context.SendPaginatedConfirmAsync(page, page =>
                    {
                        return new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("推特語音空間爬蟲清單")
                            .WithDescription(string.Join('\n', list.Skip(page * 10).Take(10)))
                            .WithFooter($"{Math.Min(list.Count(), (page + 1) * 10)} / {list.Count()} 個使用者 ({warningChannelNum} 個隱藏的警告使用者)");
                    }, list.Count(), 10, false).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Demystify(), $"Twitch-Spider-List Error");
                    await Context.Interaction.SendErrorAsync("指令執行失敗", false, true);
                }
            }
        }
    }
}
