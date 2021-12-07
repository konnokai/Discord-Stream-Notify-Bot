using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord_Stream_Notify_Bot.Command.Attribute;
using System.Threading.Tasks;
using System.Linq;
using System;
using SocialOpinionAPI.Models.Users;
using Discord_Stream_Notify_Bot.DataBase.Table;
using System.Collections.Generic;

namespace Discord_Stream_Notify_Bot.Command.Twitter
{
    public partial class Twitter : TopLevelModule<Service.TwitterService>
    {
        private readonly DiscordSocketClient _client;

        public Twitter(DiscordSocketClient client)
        {
            _client = client;
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [Command("AddNoticeTwitterSpaceChannel")]
        [Summary("新增推特語音空間開台通知的頻道\r\n" +
            "請使用@後面的使用者名稱來新增\r\n" +
            "可以使用`s!lntsc`查詢有哪些頻道\r\n")]
        [CommandExample("LaplusDarknesss", "@inui_toko")]
        [Alias("ANTSC")]
        public async Task AddNoticeTwitterSpaceChannel(string userScreenName = null)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Channel.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "");

            UserModel user = _service.GetTwitterUser(userScreenName);
            if (user == null)
            {
                await Context.Channel.SendErrorAsync($"{userScreenName} 不存在此使用者").ConfigureAwait(false);
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (db.NoticeTwitterSpaceChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeTwitterSpaceUserId == user.data.id))
                {
                    await Context.Channel.SendConfirmAsync($"{user.data.name} 已在語音空間通知清單內").ConfigureAwait(false);
                    return;
                }

                db.NoticeTwitterSpaceChannel.Add(new NoticeTwitterSpaceChannel() { GuildId = Context.Guild.Id, DiscordChannelId = Context.Channel.Id, NoticeTwitterSpaceUserId = user.data.id, NoticeTwitterSpaceUserScreenName = user.data.username });
                await Context.Channel.SendConfirmAsync($"已將 {user.data.name} 加入到語音空間通知頻道清單內").ConfigureAwait(false);

                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [Command("RemoveNoticeTwitterSpaceChannel")]
        [Summary("移除推特語音空間通知的頻道\r\n" +
             "請使用@後面的使用者名稱來移除")]
        [CommandExample("LaplusDarknesss", "@inui_toko")]
        [Alias("RNTSC")]
        public async Task RemoveNoticeTwitterSpaceChannel(string userScreenName = "")
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Channel.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (!db.NoticeTwitterSpaceChannel.Any((x) => x.GuildId == Context.Guild.Id))
                {
                    await Context.Channel.SendConfirmAsync("並未設定推特語音空間通知...").ConfigureAwait(false);
                    return;
                }

                if (!db.NoticeTwitterSpaceChannel.Any((x) => x.GuildId == Context.Guild.Id && x.NoticeTwitterSpaceUserScreenName == userScreenName))
                {
                    await Context.Channel.SendConfirmAsync($"並未設定`{userScreenName}`的推特語音空間通知...").ConfigureAwait(false);
                    return;
                }
                else
                {
                    db.NoticeTwitterSpaceChannel.Remove(db.NoticeTwitterSpaceChannel.First((x) => x.GuildId == Context.Guild.Id && x.NoticeTwitterSpaceUserScreenName == userScreenName));
                    await Context.Channel.SendConfirmAsync($"已移除 {userScreenName}").ConfigureAwait(false);

                    await db.SaveChangesAsync().ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.Guild)]
        [Command("ListNoticeTwitterSpaceChannel")]
        [Summary("顯示現在已加入推特語音空間通知的頻道")]
        [Alias("LNTSC")]
        public async Task ListNoticeTwitterSpaceChannel()
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var list = db.NoticeTwitterSpaceChannel.Where((x) => x.GuildId == Context.Guild.Id)
                .Select((x) => new KeyValuePair<string, ulong>(x.NoticeTwitterSpaceUserScreenName, x.DiscordChannelId)).ToList();

                if (list.Count() == 0) { await Context.Channel.SendConfirmAsync("推特語音空間通知清單為空").ConfigureAwait(false); return; }
                var twitterSpaceList = list.Select((x) => $"{x.Key} => <#{x.Value}>").ToList();

                await Context.SendPaginatedConfirmAsync(0, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("推特語音空間通知清單")
                        .WithDescription(string.Join('\n', twitterSpaceList.Skip(page * 20).Take(20)))
                        .WithFooter($"{Math.Min(twitterSpaceList.Count, (page + 1) * 20)} / {twitterSpaceList.Count}個頻道");
                }, twitterSpaceList.Count, 20, false);
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages | GuildPermission.MentionEveryone)]
        [RequireBotPermission(GuildPermission.MentionEveryone)]
        [Command("SetTwitterSpaceNoticeMessage")]
        [Summary("設定通知訊息\r\n" +
            "不輸入通知訊息的話則會關閉通知訊息\r\n" +
            "需先新增直播通知後才可設定通知訊息(`s!h antsc`)\r\n\r\n" +
            "(考慮到有伺服器需Ping特定用戶組的情況，故Bot需提及所有身分組權限)\r\n" +
            "(建議在私人頻道中設定以免Ping到用戶組造成不必要的誤會)")]
        [CommandExample("LaplusDarknesss",
            "LaplusDarknesss @直播通知 總帥突襲開語音啦")]
        [Alias("STSNM")]
        public async Task SetTwitterSpaceNoticeMessage(string userScreenName, [Remainder] string message = "")
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Channel.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "");

            UserModel user = _service.GetTwitterUser(userScreenName);
            if (user == null)
            {
                await Context.Channel.SendErrorAsync($"{userScreenName} 不存在此使用者").ConfigureAwait(false);
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

                    if (message != "") await Context.Channel.SendConfirmAsync($"已設定 {user.data.name} 的推特語音空間通知訊息為:\r\n{message}").ConfigureAwait(false);
                    else await Context.Channel.SendConfirmAsync($"已取消 {user.data.name} 的推特語音空間通知訊息").ConfigureAwait(false);
                }
                else
                {
                    await Context.Channel.SendConfirmAsync($"並未設定 {user.data.name} 的推特語音空間通知訊息\r\n請先使用 `s!antsc {user.data.username}` 新增後再設定語音空間通知").ConfigureAwait(false);
                }
            }
        }


        [RequireContext(ContextType.Guild)]
        [Command("ListTwitterSpaceNoticeMessage")]
        [Summary("列出已設定的推特語音空間通知訊息")]
        [Alias("LTSNM")]
        public async Task ListTwitterSpaceNoticeMessage(int page = 0)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (db.NoticeTwitterSpaceChannel.Any((x) => x.GuildId == Context.Guild.Id))
                {
                    var noticeTwitterSpaces = db.NoticeTwitterSpaceChannel.ToList().Where((x) => x.GuildId == Context.Guild.Id);
                    Dictionary<string, string> dic = new Dictionary<string, string>();

                    foreach (var item in noticeTwitterSpaces)
                    {
                        var channelTitle = item.NoticeTwitterSpaceUserScreenName;

                        dic.Add(channelTitle, item.StratTwitterSpaceMessage);
                    }

                    await Context.SendPaginatedConfirmAsync(page, (page) =>
                    {
                        EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor().WithTitle("推特語音空間通知訊息清單")
                            .WithDescription("如果沒訊息的話就代表沒設定\r\n不用擔心會Tag到用戶組，Embed不會有Ping的反應");

                        foreach (var item in dic.Skip(page * 10).Take(10))
                        {
                            embedBuilder.AddField(item.Key, item.Value);
                        }

                        return embedBuilder;
                    }, dic.Count, 10);
                }
                else
                {
                    await Context.Channel.SendConfirmAsync($"並未設定推特語音空間通知\r\n請先使用 `s!h antsc` 查看說明並新增語音空間通知").ConfigureAwait(false);
                }
            }
        }

        [Command("AddTwitterSpaceSpider")]
        [Summary("新增推特語音空間爬蟲\r\n" +
            "(請使用@後面的使用者名稱來新增)\r\n\r\n" +
            "每個伺服器可新增最多五個頻道爬蟲\r\n" +
            "伺服器需大於500人才可使用\r\n" +
            "未來會根據情況增減可新增的頻道數量\r\n" +
            "如有任何需要請向擁有者詢問\r\n")]
        [Alias("atss")]
        [RequireGuildMemberCount(500)]
        [RequireUserPermission(GuildPermission.Administrator)]
        [CommandExample("LaplusDarknesss", "@inui_toko")]
        public async Task AddTwitterSpaceSpider(string userScreenName = null)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Channel.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "");

            using (var db = DataBase.DBContext.GetDbContext())
            {
                UserModel user = _service.GetTwitterUser(userScreenName);

                if (user == null)
                {
                    await Context.Channel.SendErrorAsync($"@{userScreenName} 不存在此使用者").ConfigureAwait(false);
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

                    await Context.Channel.SendConfirmAsync($"{userScreenName} 已在爬蟲清單內\r\n" +
                        $"可直接到通知頻道內使用 `s!antsc {userScreenName}` 開啟通知\r\n" +
                        $"(由 `{guild}` 設定)").ConfigureAwait(false);
                    return;
                }

                if (user.data.is_protected)
                {
                    await Context.Channel.SendErrorAsync($"使用者推文被保護，無法查看").ConfigureAwait(false);
                    return;
                }

                if (db.TwitterSpaecSpider.Count((x) => x.GuildId == Context.Guild.Id) >= 5)
                {
                    await Context.Channel.SendConfirmAsync($"此伺服器已設定五個檢測頻道，請移除後再試\r\n" +
                        $"如有特殊需求請向Bot擁有者詢問").ConfigureAwait(false);
                    return;
                }

                db.TwitterSpaecSpider.Add(new TwitterSpaecSpider() { GuildId = Context.Message.Author.Id == Program.ApplicatonOwner.Id ? 0 : Context.Guild.Id, UserId = user.data.id, UserName = user.data.name, UserScreenName = user.data.username });
                await db.SaveChangesAsync();

                await Context.Channel.SendConfirmAsync($"已將 {userScreenName} 加入到推特語音爬蟲清單內\r\n" +
                    $"請到通知頻道內使用 `s!antsc {userScreenName}` 來開啟通知").ConfigureAwait(false);

                Program.SendMessageToDiscord($"{Context.Guild.Name} 已新增推特語音爬蟲 {Format.Url(userScreenName, $"https://twitter.com/{userScreenName}")}");
            }
        }

        [Command("RemoveTwitterSpaceSpider")]
        [Summary("移除推特語音空間爬蟲\r\n" +
           "爬蟲必須由本伺服器新增才可移除")]
        [Alias("rtss")]
        [RequireUserPermission(GuildPermission.Administrator)]
        [CommandExample("LaplusDarknesss", "@inui_toko")]
        public async Task RemoveTwitterSpaceSpider(string userScreenName = null)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Channel.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "");

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (!db.TwitterSpaecSpider.Any((x) => x.UserScreenName == userScreenName))
                {
                    await Context.Channel.SendConfirmAsync($"並未設定 {userScreenName} 語音空間檢測爬蟲...").ConfigureAwait(false);
                    return;
                }

                if (Context.Message.Author.Id != Program.ApplicatonOwner.Id && !db.TwitterSpaecSpider.Any((x) => x.UserScreenName == userScreenName && x.GuildId == Context.Guild.Id))
                {
                    await Context.Channel.SendConfirmAsync($"該語音空間爬蟲並非本伺服器新增，無法移除").ConfigureAwait(false);
                    return;
                }

                db.TwitterSpaecSpider.Remove(db.TwitterSpaecSpider.First((x) => x.UserScreenName == userScreenName));
                await db.SaveChangesAsync().ConfigureAwait(false);

                await Context.Channel.SendConfirmAsync($"已移除 {userScreenName}").ConfigureAwait(false);
                Program.SendMessageToDiscord($"{Context.Guild.Name} 已移除推特語音爬蟲 <https://twitter.com/{userScreenName}>");
            }
        }

        [RequireContext(ContextType.Guild)]
        [Command("ListTwitterSpaceSpider")]
        [Summary("顯示已加入的推特語音空間爬蟲")]
        [Alias("ltss")]
        public async Task ListTwitterSpaceSpider(int page = 0)
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
                        .WithFooter($"{Math.Min(list.Count(), (page + 1) * 10)} / {list.Count()}個頻道 ({warningChannelNum}個隱藏的警告爬蟲)");
                }, list.Count(), 10, false).ConfigureAwait(false);
            }
        }

        [Command("AddRecordTwitterSpace")]
        [Summary("新增要錄影的推特語音空間")]
        [Alias("arts")]
        [RequireOwner]
        [CommandExample("LaplusDarknesss", "@inui_toko")]
        public async Task AddRecordTwitterSpace(string userScreenName = null)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Channel.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "");

            using (var db = DataBase.DBContext.GetDbContext())
            {
                UserModel user = _service.GetTwitterUser(userScreenName);

                if (user == null)
                {
                    await Context.Channel.SendErrorAsync($"{userScreenName} 不存在此使用者").ConfigureAwait(false);
                    return;
                }

                if (db.RecordTwitterSpaceChannel.Any((x) => x.TwitterUserId == user.data.id))
                {
                    await Context.Channel.SendErrorAsync($"{userScreenName} 已存在於推特語音空間記錄清單");
                    return;
                }

                if (user.data.is_protected)
                {
                    await Context.Channel.SendErrorAsync($"使用者推文被保護，無法查看").ConfigureAwait(false);
                    return;
                }

                if (await PromptUserConfirmAsync(new EmbedBuilder().WithTitle("新增頻道至推特語音空間記錄清單?").WithDescription(user.data.name)))
                {
                    db.RecordTwitterSpaceChannel.Add(new RecordTwitterSpaceChannel() { TwitterUserId = user.data.id, TwitterScreenName = user.data.username });
                    await db.SaveChangesAsync();
                    await Context.Channel.SendConfirmAsync($"已新增 {user.data.name} 至推特語音空間記錄清單").ConfigureAwait(false);
                }
            }
        }

        [Command("RemoveRecordTwitterSpace")]
        [Summary("移除直播記錄頻道")]
        [Alias("rrts")]
        [RequireOwner]
        public async Task RemoveRecordTwitterSpace(string userScreenName = null)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
            {
                await Context.Channel.SendErrorAsync("使用者名稱不可空白").ConfigureAwait(false);
                return;
            }

            userScreenName = userScreenName.Replace("@", "");

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (!db.RecordTwitterSpaceChannel.Any((x) => x.TwitterScreenName == userScreenName))
                {
                    await Context.Channel.SendErrorAsync($"推特語音空間記錄清單中沒有 {userScreenName}");
                    return;
                }

                var userName = "";
                UserModel user = _service.GetTwitterUser(userScreenName);
                if (user != null) userName = user.data.name;
                else userName = userScreenName;

                if (await PromptUserConfirmAsync(new EmbedBuilder().WithTitle("從推特語音空間記錄清單移除頻道?").WithDescription(userName)))
                {
                    db.RecordTwitterSpaceChannel.Remove(db.RecordTwitterSpaceChannel.First((x) => x.TwitterUserId == user.data.id));
                    await db.SaveChangesAsync();
                    await Context.Channel.SendConfirmAsync($"已從推特語音空間記錄清單中移除 {userName}").ConfigureAwait(false);
                }
            }
        }
    }
}
