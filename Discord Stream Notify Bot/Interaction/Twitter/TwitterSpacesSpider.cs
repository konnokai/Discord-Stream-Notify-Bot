using Discord.Interactions;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction.Attribute;
using SocialOpinionAPI.Models.Users;
using static Discord_Stream_Notify_Bot.Interaction.Twitter.TwitterSpaces;

namespace Discord_Stream_Notify_Bot.Interaction.Twitter
{
    [EnabledInDm(false)]
    [Group("twitter-spider", "Twiiter Space爬蟲設定")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public class TwitterSpacesSpider : TopLevelModule<SharedService.Twitter.TwitterSpacesService>
    {
        private readonly DiscordSocketClient _client;
        public TwitterSpacesSpider(DiscordSocketClient client)
        {
            _client = client;
        }

        [RequireContext(ContextType.Guild)]
        [RequireGuildMemberCount(500)]
        [RequireUserPermission(GuildPermission.Administrator, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [CommandSummary("新增推特語音空間爬蟲\n" +
            "(請使用@後面的使用者名稱來新增)\n\n" +
           "**禁止新增非VTuber的推主**\n" +
            "每個伺服器可新增最多五個頻道爬蟲\n" +
            "伺服器需大於500人才可使用\n" +
            "未來會根據情況增減可新增的頻道數量\n" +
            "如有任何需要請向擁有者詢問\n")]
        [CommandExample("LaplusDarknesss", "@inui_toko")]
        [SlashCommand("add", "新增推特語音空間爬蟲")]
        public async Task AddSpider([Summary("推特使用者名稱")] string userScreenName)
        {
            await DeferAsync(true).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Interaction.SendErrorAsync("使用者名稱不可空白", true).ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "");

            using (var db = DataBase.DBContext.GetDbContext())
            {
                UserModel user = _service.GetTwitterUser(userScreenName);

                if (user == null)
                {
                    await Context.Interaction.SendErrorAsync($"`{userScreenName}` 不存在此使用者", true).ConfigureAwait(false);
                    return;
                }

                if (db.TwitterSpaecSpider.Any((x) => x.UserId == user.data.id))
                {
                    var item = db.TwitterSpaecSpider.FirstOrDefault((x) => x.UserId == user.data.id);
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
                            await (await Program.ApplicatonOwner.CreateDMChannelAsync())
                                .SendMessageAsync(embed: new EmbedBuilder()
                                    .WithOkColor()
                                    .WithTitle("已更新推特語音爬蟲的持有伺服器")
                                    .AddField("推主", Format.Url(userScreenName, $"https://twitter.com/{userScreenName}"), false)
                                    .AddField("原伺服器", Context.Guild.Id, false)
                                    .AddField("新伺服器", $"{Context.Guild.Name} ({Context.Guild.Id})", false).Build());
                        }
                        catch (Exception ex) { Log.Error(ex.ToString()); }

                        item.GuildId = Context.Guild.Id;
                        db.TwitterSpaecSpider.Update(item);
                        db.SaveChanges();
                    }

                    await Context.Interaction.SendConfirmAsync($"`{userScreenName}` 已在爬蟲清單內\n" +
                        $"可直接到通知頻道內使用 `/twitter-space add-space-notice {userScreenName}` 開啟通知\n" +
                        (isGuildExist ? $"\n(由 `{guild}` 設定)" : ""), true).ConfigureAwait(false);
                    return;
                }

                if (user.data.is_protected)
                {
                    await Context.Interaction.SendErrorAsync($"使用者已開啟推文保護，無法新增", true).ConfigureAwait(false);
                    return;
                }

                if (db.TwitterSpaecSpider.Count((x) => x.GuildId == Context.Guild.Id) >= 5)
                {
                    await Context.Interaction.SendErrorAsync($"此伺服器已設定五個推特語音空間爬蟲，請移除後再試\n" +
                        $"如有特殊需求請向Bot擁有者詢問", true).ConfigureAwait(false);
                    return;
                }

                var spider = new TwitterSpaecSpider() { GuildId = Context.User.Id == Program.ApplicatonOwner.Id ? 0 : Context.Guild.Id, UserId = user.data.id, UserName = user.data.name, UserScreenName = user.data.username.ToLower() };
                if (Context.User.Id == Program.ApplicatonOwner.Id && !await PromptUserConfirmAsync("設定該爬蟲為本伺服器使用?"))
                    spider.GuildId = 0;

                db.TwitterSpaecSpider.Add(spider);
                db.SaveChanges();

                await Context.Interaction.SendConfirmAsync($"已將 `{userScreenName}` 加入到推特語音爬蟲清單內\n" +
                    $"請到通知頻道內使用 `/twitter-space add {userScreenName}` 來開啟通知", true).ConfigureAwait(false);

                try
                {

                    await (await Program.ApplicatonOwner.CreateDMChannelAsync()).SendMessageAsync(embed: new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("已新增推特語音爬蟲")
                        .AddField("推主", Format.Url(userScreenName, $"https://twitter.com/{userScreenName}"), false)
                        .AddField("伺服器", $"{Context.Guild.Name} ({Context.Guild.Id})", false)
                        .AddField("執行者", $"{Context.User.Username} ({Context.User.Id})", false).Build());
                }
                catch (Exception ex) { Log.Error(ex.ToString()); }
            }
        }

        [RequireContext(ContextType.Guild)]
        [CommandSummary("移除推特語音空間爬蟲\n" +
           "爬蟲必須由本伺服器新增才可移除")]
        [RequireUserPermission(GuildPermission.Administrator, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [CommandExample("LaplusDarknesss", "@inui_toko")]
        [SlashCommand("remove", "移除推特語音空間爬蟲")]
        public async Task RemoveSpider([Summary("推特使用者名稱"), Autocomplete(typeof(GuildTwitterSpaceSpiderAutocompleteHandler))] string userScreenName)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Interaction.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "").ToLower();

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (!db.TwitterSpaecSpider.Any((x) => x.UserScreenName == userScreenName))
                {
                    await Context.Interaction.SendErrorAsync($"並未設定 {userScreenName} 語音空間檢測爬蟲...").ConfigureAwait(false);
                    return;
                }

                if (Context.User.Id != Program.ApplicatonOwner.Id && !db.TwitterSpaecSpider.Any((x) => x.UserScreenName == userScreenName && x.GuildId == Context.Guild.Id))
                {
                    await Context.Interaction.SendErrorAsync($"該語音空間爬蟲並非本伺服器新增，無法移除").ConfigureAwait(false);
                    return;
                }

                db.TwitterSpaecSpider.Remove(db.TwitterSpaecSpider.First((x) => x.UserScreenName == userScreenName));
                db.SaveChanges();

                await Context.Interaction.SendConfirmAsync($"已移除 {userScreenName}").ConfigureAwait(false);

                try
                {
                    await (await Program.ApplicatonOwner.CreateDMChannelAsync()).SendMessageAsync(embed: new EmbedBuilder()
                        .WithErrorColor()
                        .WithTitle("已移除推特語音爬蟲")
                        .AddField("推主", Format.Url(userScreenName, $"https://twitter.com/{userScreenName}"), false)
                        .AddField("伺服器", $"{Context.Guild.Name} ({Context.Guild.Id})", false)
                        .AddField("執行者", $"{Context.User.Username} ({Context.User.Id})", false).Build());
                }
                catch (Exception ex) { Log.Error(ex.ToString()); }
            }
        }

        [RequireContext(ContextType.Guild)]
        [SlashCommand("list", "顯示推特語音空間爬蟲")]
        public async Task ListSpider([Summary("頁數")] int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var list = db.TwitterSpaecSpider.Where((x) => !x.IsWarningUser).Select((x) => Format.Url(x.UserScreenName, $"https://twitter.com/{x.UserScreenName}") +
                    $" 由 `" + (x.GuildId == 0 ? "Bot擁有者" : (_client.GetGuild(x.GuildId) != null ? _client.GetGuild(x.GuildId).Name : "已退出的伺服器")) + "` 新增");
                int warningChannelNum = db.TwitterSpaecSpider.Count((x) => x.IsWarningUser);

                await Context.SendPaginatedConfirmAsync(page, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("推特語音空間爬蟲清單")
                        .WithDescription(string.Join('\n', list.Skip(page * 10).Take(10)))
                        .WithFooter($"{Math.Min(list.Count(), (page + 1) * 10)} / {list.Count()}個使用者 ({warningChannelNum}個隱藏的警告使用者)");
                }, list.Count(), 10, false).ConfigureAwait(false);
            }
        }
    }
}
