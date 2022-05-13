using Discord;
using Discord.Commands;
using Discord_Stream_Notify_Bot.Command.Attribute;
using Discord_Stream_Notify_Bot.SharedService.YoutubeMember;
using Discord_Stream_Notify_Bot.SharedService.Youtube;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;

namespace Discord_Stream_Notify_Bot.Command.YoutubeMember
{
    public class YoutubeMember : TopLevelModule, ICommandService
    {
        private readonly YoutubeMemberService _service;
        private readonly YoutubeStreamService _ytservice;
        private readonly DownloadTracker _tracker;

        public YoutubeMember(YoutubeMemberService youtubeMemberService, YoutubeStreamService youtubeStreamService, DownloadTracker tracker)
        {
            _service = youtubeMemberService;
            _ytservice = youtubeStreamService;
            _tracker = tracker;
        }

        [Command("YoutubeMemberLoginCheck")]
        [Summary("確認是否已到網站登入並記錄至資料庫")]
        [Alias("YMLC")]
        [RequireContext(ContextType.Guild)]
        [RequireBotPermission(GuildPermission.ManageRoles)]
        public async Task YoutubeMemberLoginCheck()
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var guildConfig = db.GuildConfig.Include((x) => x.MemberCheck).First((x) => x.GuildId == Context.Guild.Id);
                if (string.IsNullOrEmpty(guildConfig.MemberCheckChannelId) || guildConfig.MemberCheckGrantRoleId == 0 || string.IsNullOrEmpty(guildConfig.MemberCheckVideoId))
                {
                    await Context.Channel.SendErrorAsync($"請向管理員確認本伺服器是否已開啟會限驗證功能");
                    return;
                }

                if (await _service.IsExistUserTokenAsync(Context.User.Id.ToString()))
                {
                    if (!guildConfig.MemberCheck.Any((x) => x.UserId == Context.User.Id))
                    {
                        guildConfig.MemberCheck.Add(new DataBase.Table.YoutubeMemberCheck() { UserId = Context.User.Id });
                        db.SaveChanges();
                    }
                    await Context.Channel.SendConfirmAsync("已記錄至資料庫，請稍等至多5分鐘讓Bot驗證\n請確認已開啟本伺服器的 `允許來自伺服器成員的私人訊息` ，以避免收不到通知");
                }
                else
                {
                    await Context.Channel.SendErrorAsync($"請先到 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 登入Discord以及Google\n登入完後再輸入一次本指令");
                }
            }
        }

        [Command("SetYoutubeMemberChannel")]
        [Summary("設定會限驗證頻道")]
        [Alias("symc")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator)]
        [CommandExample("https://www.youtube.com/channel/UCR6qhsLpn62WVxCBK1dkLow 837652679303757824")]
        public async Task SetYoutubeMemberChannel([Summary("頻道連結")] string url, [Summary("用戶組Id")] ulong roleId)
        {
            var currentBotUser = await Context.Guild.GetCurrentUserAsync() as SocketGuildUser;
            if (!currentBotUser.GuildPermissions.ManageRoles)
            {
                await Context.Channel.SendErrorAsync("我沒有 `管理身分組` 的權限，請給予權限後再次執行本指令");
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                try
                {
                    var channelId = await _ytservice.GetChannelIdAsync(url);
                    var role = Context.Guild.GetRole(roleId);
                    if (role == null)
                    {
                        await Context.Channel.SendErrorAsync("用戶組Id錯誤");
                        return;
                    }

                    if (currentBotUser.Roles.Max(x => x.Position) < role.Position)
                    {
                        await Context.Channel.SendErrorAsync($"{role.Name} 的順序比我現在的身分組還高，請將我的身分組拉高後再次執行本指令");
                        return;
                    }

                    var guildConfig = db.GuildConfig.FirstOrDefault((x) => x.GuildId == Context.Guild.Id);
                    if (guildConfig == null)
                        db.GuildConfig.Add(new DataBase.Table.GuildConfig() { GuildId = Context.Guild.Id, MemberCheckChannelId = channelId, MemberCheckGrantRoleId = role.Id });
                    else
                    {
                        guildConfig.MemberCheckChannelId = channelId;
                        guildConfig.MemberCheckGrantRoleId = role.Id;
                        guildConfig.MemberCheckVideoId = "-";
                        db.GuildConfig.Update(guildConfig);
                        db.SaveChanges();
                    }

                    await Context.Channel.SendConfirmAsync($"已設定此伺服器使用 `{channelId}` 作為會限驗證頻道\n" +
                        $"驗證成功的成員將會獲得 `{role.Name}` 用戶組\n" +
                        $"請等待五分鐘後才可開始檢測會限");

                    var logChannelId = db.GuildConfig.FirstOrDefault((x) => x.GuildId == Context.Guild.Id).LogMemberStatusChannelId;
                    if (logChannelId == 0)
                    {
                        await Context.Channel.SendErrorAsync("注意: 本伺服器尚未設定會限驗證紀錄頻道\n" +
                            "請新增頻道並設定本機器人`讀取`與`發送`權限後使用 `s!snmsc` 設定紀錄頻道 (`s!h snmsc`)");
                    }
                    else if (Context.Guild.GetChannelAsync(logChannelId) == null)
                    {
                        await Context.Channel.SendErrorAsync("注意: 本伺服器所設定的會限驗證紀錄頻道已刪除\n" +
                            "請新增頻道並設定本機器人`讀取`與`發送`權限後使用 `s!snmsc` 設定紀錄頻道 (`s!h snmsc`)");

                        guildConfig.LogMemberStatusChannelId = 0;
                        db.GuildConfig.Update(guildConfig);
                        db.SaveChanges();
                    }
                }
                catch (System.Exception ex)
                {
                    await Context.Channel.SendErrorAsync(ex.Message);
                    Log.Error(ex.ToString());
                }
            }
        }

        [Command("RemoveYoutubeMemberChannel")]
        [Summary("移除會限驗證頻道")]
        [Alias("rymc")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator)]
        [CommandExample("https://www.youtube.com/channel/UCR6qhsLpn62WVxCBK1dkLow")]
        public async Task RemoveYoutubeMemberChannel([Summary("頻道連結")] string url)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                try
                {
                    var channelId = await _ytservice.GetChannelIdAsync(url);

                    var guildConfig = db.GuildConfig.FirstOrDefault((x) => x.GuildId == Context.Guild.Id);
                    if (guildConfig == null)
                    {
                        db.GuildConfig.Add(new DataBase.Table.GuildConfig() { GuildId = Context.Guild.Id });
                        await Context.Channel.SendErrorAsync("未設定過會限驗證頻道");
                    }
                    else if (string.IsNullOrEmpty(guildConfig.MemberCheckChannelId))
                    {
                        await Context.Channel.SendErrorAsync("未設定過會限驗證頻道");
                    }
                    else
                    {
                        guildConfig.MemberCheckChannelId = "";
                        db.GuildConfig.Update(guildConfig);
                    }
                    db.SaveChanges();
                }
                catch (System.Exception ex)
                {
                    await Context.Channel.SendErrorAsync(ex.Message);
                    Log.Error(ex.ToString());
                }
            }
        }

        [Command("SetNoticeMemberStatusChannel")]
        [Summary("設定會限驗證狀態紀錄頻道")]
        [Alias("snmsc")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        public async Task SetNoticeMemberStatusChannel([Summary("Discord頻道Id")] ulong cId)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var guildConfig = db.GuildConfig.Include((x) => x.MemberCheck).First((x) => x.GuildId == Context.Guild.Id);
                if (string.IsNullOrEmpty(guildConfig.MemberCheckChannelId) || guildConfig.MemberCheckGrantRoleId == 0 || string.IsNullOrEmpty(guildConfig.MemberCheckVideoId))
                {
                    await Context.Channel.SendErrorAsync($"請向管理員確認本伺服器是否已開啟會限驗證功能");
                    return;
                }

                var channel = await Context.Guild.GetTextChannelAsync(cId) as SocketTextChannel;
                if (channel == null)
                {
                    await Context.Channel.SendErrorAsync($"{cId} 不存在頻道");
                    return;
                }

                var permissions = (await Context.Guild.GetCurrentUserAsync()).GetPermissions(channel);
                if (!permissions.ViewChannel || !permissions.SendMessages)
                {
                    await Context.Channel.SendErrorAsync($"我在 `{channel}` 沒有 `讀取\\編輯頻道` 的權限，請給予權限後再次執行本指令");
                    return;
                }

                guildConfig.LogMemberStatusChannelId = cId;
                db.GuildConfig.Update(guildConfig);
                db.SaveChanges();

                await Context.Channel.SendConfirmAsync($"已設定 `{channel}` 為會限驗證狀態通知頻道");
            }
        }

        //https://gitlab.com/Kwoth/nadekobot/-/blob/v4/src/NadekoBot/Modules/Utility/Utility.cs#L113
        [Command("Inrole")]
        [RequireOwner]
        public async Task InRoleTest(IRole role = null)
        {
            await Context.Channel.TriggerTypingAsync();
            await _tracker.EnsureUsersDownloadedAsync(Context.Guild);

            var users = await Context.Guild.GetUsersAsync(
                CacheMode.CacheOnly
            );

            var roleUsers = users.Where(u => role is null ? u.RoleIds.Count == 1 : u.RoleIds.Contains(role.Id))
                            .Select(u => $"`{u.Id,18}` {u}")
                            .ToArray();

            await Context.SendPaginatedConfirmAsync(0,
                cur =>
                {
                    var pageUsers = roleUsers.Skip(cur * 20).Take(20).ToList();

                    if (pageUsers.Count == 0)
                        return new EmbedBuilder().WithErrorColor().WithDescription("No user in role");

                    return new EmbedBuilder()
                              .WithOkColor()
                              .WithTitle($"{role.Name}: {roleUsers.Length}")
                              .WithDescription(string.Join("\n", pageUsers));
                },
                roleUsers.Length,
                20);
        }
    }
}
