using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction.Attribute;
using SocialOpinionAPI.Models.Users;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Interaction.Twitter
{
    [Group("twitter-space", "推特語音空間")]
    public class TwitterSpaces : TopLevelModule<SharedService.Twitter.TwitterSpacesService>
    {
        private readonly DiscordSocketClient _client;
        private readonly HttpClients.DiscordWebhookClient _discordWebhookClient;

        public TwitterSpaces(DiscordSocketClient client,HttpClients.DiscordWebhookClient discordWebhookClient)
        {
            _client = client;
            _discordWebhookClient = discordWebhookClient;
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [CommandSummary("新增推特語音空間開台通知的頻道\n" +
            "請使用@後面的使用者名稱來新增\n" +
            "可以使用`/twitter-space list-space-notice`查詢有哪些頻道\n")]
        [CommandExample("LaplusDarknesss", "@inui_toko")]
        [SlashCommand("add-space-notice", "新增推特語音空間開台通知的頻道")]
        public async Task AddChannel([Summary("推特使用者名稱")]string userScreenName, [Summary("發送通知的頻道")] ITextChannel textChannel)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Interaction.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            await DeferAsync(true).ConfigureAwait(false);

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
                        await db.SaveChangesAsync();
                        await Context.Interaction.SendConfirmAsync($"已將 {user.data.name} 的語音空間通知頻道變更至: {textChannel}", true).ConfigureAwait(false);
                    }
                    return;
                }

                string addString = "";
                if (!db.IsTwitterUserInDb(user.data.id)) addString += $"\n\n(注意: 該使用者未加入爬蟲清單\n如長時間無通知請使用 `/help get-command-help add-twitter-spider` 查看說明並加入爬蟲)";
                db.NoticeTwitterSpaceChannel.Add(new NoticeTwitterSpaceChannel() { GuildId = Context.Guild.Id, DiscordChannelId = textChannel.Id, NoticeTwitterSpaceUserId = user.data.id, NoticeTwitterSpaceUserScreenName = user.data.username.ToLower() });
                await Context.Interaction.SendConfirmAsync($"已將 {user.data.name} 加入到語音空間通知頻道清單內{addString}", true).ConfigureAwait(false);

                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [CommandSummary("移除推特語音空間通知的頻道\n" +
             "請使用@後面的使用者名稱來移除")]
        [CommandExample("LaplusDarknesss", "@inui_toko")]
        [SlashCommand("remove-space-notice", "移除推特語音空間開台通知的頻道")]
        public async Task RemoveChannel([Summary("推特使用者名稱")] string userScreenName)
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

                    await db.SaveChangesAsync().ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.Guild)]
        [SlashCommand("list-space-notice", "顯示現在已加入推特語音空間通知的頻道")]
        public async Task ListChannel()
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var list = db.NoticeTwitterSpaceChannel.ToList().Where((x) => x.GuildId == Context.Guild.Id)
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

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages | GuildPermission.MentionEveryone)]
        [RequireBotPermission(GuildPermission.MentionEveryone)]
        [CommandSummary("設定通知訊息\n" +
            "不輸入通知訊息的話則會關閉通知訊息\n" +
            "需先新增直播通知後才可設定通知訊息(`/help get-command-help add-space-notice`)\n\n" +
            "(考慮到有伺服器需Ping特定用戶組的情況，故Bot需提及所有身分組權限)\n" +
            "(建議在私人頻道中設定以免Ping到用戶組造成不必要的誤會)")]
        [CommandExample("LaplusDarknesss", "LaplusDarknesss @直播通知 總帥突襲開語音啦")]
        [SlashCommand("set-space-notice-message", "設定通知訊息")]
        public async Task SetMessage([Summary("推特使用者名稱")] string userScreenName, [Summary("通知訊息")] string message)
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
                    await db.SaveChangesAsync();

                    if (message != "") await Context.Interaction.SendConfirmAsync($"已設定 {user.data.name} 的推特語音空間通知訊息為:\n{message}").ConfigureAwait(false);
                    else await Context.Interaction.SendConfirmAsync($"已取消 {user.data.name} 的推特語音空間通知訊息").ConfigureAwait(false);
                }
                else
                {
                    await Context.Interaction.SendConfirmAsync($"並未設定推特語音空間通知\n" +
                        $"請先使用 `/help get-command-help add-space-notice` 查看說明並新增語音空間通知").ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.Guild)]
        [SlashCommand("list-notice-message", "列出已設定的推特語音空間通知訊息")]
        public async Task ListMessage([Summary("頁數")]int page = 0)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
               if (db.NoticeTwitterSpaceChannel.Any((x) => x.GuildId == Context.Guild.Id))
                {
                    var noticeTwitterSpaces = db.NoticeTwitterSpaceChannel.ToList().Where((x) => x.GuildId == Context.Guild.Id);
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
                        $"請先使用 `/help get-command-help add-space-notice` 查看說明並新增語音空間通知").ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireGuildMemberCount(500)]
        [RequireUserPermission(GuildPermission.Administrator)]
        [CommandSummary("新增推特語音空間爬蟲\n" +
            "(請使用@後面的使用者名稱來新增)\n\n" +
           "**禁止新增非VTuber的推主**\n" +
            "每個伺服器可新增最多五個頻道爬蟲\n" +
            "伺服器需大於500人才可使用\n" +
            "未來會根據情況增減可新增的頻道數量\n" +
            "如有任何需要請向擁有者詢問\n")]
        [CommandExample("LaplusDarknesss", "@inui_toko")]
        [SlashCommand("add-twitter-spider", "新增推特語音空間爬蟲")]
        public async Task AddSpider([Summary("推特使用者名稱")] string userScreenName)
        {
            await DeferAsync(false).ConfigureAwait(false);

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
                    await Context.Interaction.SendErrorAsync($"@{userScreenName} 不存在此使用者", true).ConfigureAwait(false);
                    return;
                }

                if (db.TwitterSpaecSpider.Any((x) => x.UserId == user.data.id))
                {
                    var item = db.TwitterSpaecSpider.FirstOrDefault((x) => x.UserId == user.data.id);
                    string guild = "";
                    try
                    {
                        guild = item.GuildId == 0 ? "Bot擁有者" : $"{_client.GetGuild(item.GuildId).Name}";
                    }
                    catch (Exception)
                    {
                        guild = "已退出的伺服器";
                    }

                    await Context.Interaction.SendConfirmAsync($"{userScreenName} 已在爬蟲清單內\n" +
                        $"可直接到通知頻道內使用 `/twitter-space add-space-notice {userScreenName}` 開啟通知\n" +
                        $"(由 `{guild}` 設定)", true).ConfigureAwait(false);
                    return;
                }

                if (user.data.is_protected)
                {
                    await Context.Interaction.SendErrorAsync($"使用者推文被保護，無法查看", true).ConfigureAwait(false);
                    return;
                }

                if (db.TwitterSpaecSpider.Count((x) => x.GuildId == Context.Guild.Id) >= 5)
                {
                    await Context.Interaction.SendErrorAsync($"此伺服器已設定五個檢測頻道，請移除後再試\n" +
                        $"如有特殊需求請向Bot擁有者詢問", true).ConfigureAwait(false);
                    return;
                }

                db.TwitterSpaecSpider.Add(new TwitterSpaecSpider() { GuildId = Context.User.Id == Program.ApplicatonOwner.Id ? 0 : Context.Guild.Id, UserId = user.data.id, UserName = user.data.name, UserScreenName = user.data.username.ToLower() });
                await db.SaveChangesAsync();

                await Context.Interaction.SendConfirmAsync($"已將 {userScreenName} 加入到推特語音爬蟲清單內\n" +
                    $"請到通知頻道內使用 `/twitter-space add-space-notice {userScreenName}` 來開啟通知", true).ConfigureAwait(false);

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
        [RequireUserPermission(GuildPermission.Administrator)]
        [CommandExample("LaplusDarknesss", "@inui_toko")]
        [SlashCommand("remove-twitter-spider", "移除推特語音空間爬蟲")]
        public async Task RemoveSpider([Summary("推特使用者名稱")]string userScreenName)
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
                await db.SaveChangesAsync().ConfigureAwait(false);

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
        [SlashCommand("list-twitter-spider", "顯示推特語音空間爬蟲")]
        public async Task ListSpider([Summary("頁數")]int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var list = db.TwitterSpaecSpider.ToList().Where((x) => !x.IsWarningUser).Select((x) => Format.Url(x.UserScreenName, $"https://twitter.com/{x.UserScreenName}") +
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

        [RequireContext(ContextType.Guild)]
        [SlashCommand("list-record-user", "顯示推特語音空間錄影清單")]
        public async Task ListRecord([Summary("頁數")]int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var nowRecordList = db.TwitterSpaecSpider.ToList().Where((x) => x.IsRecord && !x.IsWarningUser).Select((x) => $"{x.UserName} ({Format.Url($"{x.UserScreenName}", $"https://twitter.com/{x.UserScreenName}")})").ToList();
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
