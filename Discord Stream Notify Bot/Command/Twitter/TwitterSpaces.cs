using Discord.Commands;
using Discord_Stream_Notify_Bot.Command.Attribute;
using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.DataBase.Table;

namespace Discord_Stream_Notify_Bot.Command.Twitter
{
    public class TwitterSpaces : TopLevelModule, ICommandService
    {
        private readonly DiscordSocketClient _client;
        private readonly SharedService.Twitter.TwitterSpacesService _service;
        private readonly MainDbService _dbService;

        public TwitterSpaces(DiscordSocketClient client, SharedService.Twitter.TwitterSpacesService service, MainDbService dbService)
        {
            _client = client;
            _service = service;
            _dbService = dbService;
        }

        [RequireContext(ContextType.DM)]
        [Command("ListTwitterSpaceSpider")]
        [Summary("顯示推特語音空間爬蟲")]
        [Alias("ltss")]
        public async Task ListTwitterSpaceSpider(int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = _dbService.GetDbContext())
            {
                var list = db.TwitterSpaecSpider.Where((x) => !x.IsWarningUser).Select((x) => Format.Url(x.UserScreenName, $"https://twitter.com/{x.UserScreenName}") +
                    $" 由 `" + (x.GuildId == 0 ? "Bot擁有者" : (_client.GetGuild(x.GuildId) != null ? _client.GetGuild(x.GuildId).Name : "已退出的伺服器")) + "` 新增");
                int warningChannelNum = db.TwitterSpaecSpider.Count((x) => x.IsWarningUser);

                await Context.SendPaginatedConfirmAsync(page, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("推特語音空間爬蟲清單")
                        .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20)))
                        .WithFooter($"{Math.Min(list.Count(), (page + 1) * 20)} / {list.Count()}個使用者 ({warningChannelNum}個隱藏的警告使用者)");
                }, list.Count(), 20, false).ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("ListWarningTwitterSpaceSpider")]
        [Summary("顯示警告的推特語音空間爬蟲")]
        [Alias("lwtss")]
        public async Task ListWarningTwitterSpaceSpider(int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = _dbService.GetDbContext())
            {
                var list = db.TwitterSpaecSpider.Where((x) => x.IsWarningUser).Select((x) => Format.Url(x.UserScreenName, $"https://twitter.com/{x.UserScreenName}") +
                    $" 由 `" + (x.GuildId == 0 ? "Bot擁有者" : (_client.GetGuild(x.GuildId) != null ? _client.GetGuild(x.GuildId).Name : "已退出的伺服器")) + "` 新增");

                await Context.SendPaginatedConfirmAsync(page, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("警告的推特語音空間爬蟲清單")
                        .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20)))
                        .WithFooter($"{Math.Min(list.Count(), (page + 1) * 20)} / {list.Count()}個使用者");
                }, list.Count(), 20, false).ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.DM)]
        [Command("ListTwitterSpaceRecord")]
        [Summary("顯示推特語音空間爬蟲")]
        [Alias("lwsr")]
        public async Task ListTwitterSpaceRecord(int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = _dbService.GetDbContext())
            {
                var nowRecordList = db.TwitterSpaecSpider.Where((x) => x.IsRecord && !x.IsWarningUser)
                    .Select((x) => $"{x.UserName} ({Format.Url($"{x.UserScreenName}", $"https://twitter.com/{x.UserScreenName}")})")
                    .ToList();
                int warningUserNum = db.TwitterSpaecSpider.Count((x) => x.IsWarningUser);

                if (nowRecordList.Count > 0)
                {
                    nowRecordList.Sort();
                    await Context.SendPaginatedConfirmAsync(page, page =>
                    {
                        return new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("推特語音空間記錄清單")
                            .WithDescription(string.Join('\n', nowRecordList.Skip(page * 20).Take(20)))
                            .WithFooter($"{Math.Min(nowRecordList.Count, (page + 1) * 20)} / {nowRecordList.Count} 個使用者 ({warningUserNum} 個隱藏的警告頻道)");
                    }, nowRecordList.Count, 20, false);
                }
                else
                {
                    await Context.Channel.SendErrorAsync($"並未設定語音空間通知\n" +
                        $"請先使用 `/help get-command-help twitter-space add` 查看說明並新增語音空間通知").ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("ListWarningTwitterSpaceRecord")]
        [Summary("顯示警告的推特語音空間錄影清單")]
        [Alias("lwtsr")]
        public async Task ListWarningTwitterSpaceRecord(int page = 0)
        {
            await Context.Channel.TriggerTypingAsync().ConfigureAwait(false);
            if (page < 0) page = 0;

            using (var db = _dbService.GetDbContext())
            {
                var nowRecordList = db.TwitterSpaecSpider.Where((x) => x.IsRecord && x.IsWarningUser).Select((x) => $"{x.UserName} ({Format.Url($"{x.UserScreenName}", $"https://twitter.com/{x.UserScreenName}")})").ToList();

                if (nowRecordList.Count > 0)
                {
                    nowRecordList.Sort();
                    await Context.SendPaginatedConfirmAsync(0, page =>
                    {
                        return new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("警告的推特語音空間記錄清單")
                            .WithDescription(string.Join('\n', nowRecordList.Skip(page * 20).Take(20)))
                            .WithFooter($"{Math.Min(nowRecordList.Count, (page + 1) * 20)} / {nowRecordList.Count}個使用者");
                    }, nowRecordList.Count, 20, false);
                }
                else await Context.Channel.SendConfirmAsync($"警告的直播記錄清單中沒有任何頻道").ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.DM)]
        [Command("ToggleTwitterSpaceRecord")]
        [Summary("切換要錄影的推特語音空間")]
        [Alias("ttsr")]
        [RequireOwner]
        [CommandExample("LaplusDarknesss", "@inui_toko")]
        public async Task ToggleTwitterSpaceRecord(string userScreenName = null)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Channel.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            if (!_service.IsEnable)
            {
                await Context.Channel.SendErrorAsync("此 Bot 的 Twitter 功能已關閉，請向擁有者確認").ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "");

            using (var db = _dbService.GetDbContext())
            {
                var user = await _service.GetTwitterUserAsync(userScreenName);

                if (user == null)
                {
                    await Context.Channel.SendErrorAsync($"{userScreenName} 不存在此使用者").ConfigureAwait(false);
                    return;
                }

                TwitterSpaecSpider twitterSpaecSpider = null;
                if (db.TwitterSpaecSpider.Any((x) => x.UserId == user.RestId))
                {
                    twitterSpaecSpider = db.TwitterSpaecSpider.First((x) => x.UserId == user.RestId);
                    twitterSpaecSpider.IsRecord = !twitterSpaecSpider.IsRecord;
                }
                else
                {
                    if (user.Legacy.Protected.HasValue && user.Legacy.Protected.Value)
                    {
                        await Context.Channel.SendErrorAsync($"使用者已開啟推文保護，無法新增").ConfigureAwait(false);
                        return;
                    }

                    twitterSpaecSpider = new TwitterSpaecSpider() { GuildId = 0, UserId = user.RestId, UserScreenName = user.Legacy.ScreenName, UserName = user.Legacy.Name, IsRecord = true };
                    db.TwitterSpaecSpider.Add(twitterSpaecSpider);
                }

                if (db.SaveChanges() >= 1)
                    await Context.Channel.SendConfirmAsync($"已設定 {user.Legacy.Name} 的推特語音紀錄為: " + (twitterSpaecSpider.IsRecord ? "開啟" : "關閉")).ConfigureAwait(false);
                else
                    await Context.Channel.SendErrorAsync("未保存").ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("ToggleWarningTwitterUser")]
        [Summary("切換推特使用者狀態")]
        [CommandExample("LaplusDarknesss")]
        [Alias("twtu")]
        public async Task ToggleWarningTwitterUser(string userScreenName = null)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Channel.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "");

            using (var db = _dbService.GetDbContext())
            {
                if (db.TwitterSpaecSpider.Any((x) => x.UserScreenName == userScreenName))
                {
                    var twitterSpaec = db.TwitterSpaecSpider.First((x) => x.UserScreenName == userScreenName);
                    twitterSpaec.IsWarningUser = !twitterSpaec.IsWarningUser;
                    db.TwitterSpaecSpider.Update(twitterSpaec);
                    db.SaveChanges();

                    await Context.Channel.SendConfirmAsync($"已設定 {twitterSpaec.UserName} 為 " + (twitterSpaec.IsWarningUser ? "警告" : "普通") + " 狀態").ConfigureAwait(false);
                }
            }
        }
    }
}
