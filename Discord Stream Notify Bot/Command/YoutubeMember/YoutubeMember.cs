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

        public YoutubeMember(YoutubeMemberService youtubeMemberService, YoutubeStreamService youtubeStreamService)
        {
            _service = youtubeMemberService;
            _ytservice = youtubeStreamService;
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

                if (db.MemberAccessToken.Any((x) => x.DiscordUserId == Context.User.Id.ToString()))
                {
                    if (!guildConfig.MemberCheck.Any((x) => x.UserId == Context.User.Id))
                    {
                        guildConfig.MemberCheck.Add(new DataBase.Table.YoutubeMemberCheck() { UserId = Context.User.Id });
                        await db.SaveChangesAsync();
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
        public async Task SetNoticeMemberStatusChannel([Summary("頻道連結")]string url,[Summary("用戶組Id")] ulong roleId)
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
                        guildConfig.MemberCheckGrantRoleId= role.Id;
                        db.GuildConfig.Update(guildConfig);
                        await db.SaveChangesAsync();
                    }

                    string result = $"已設定此伺服器使用 `{channelId}` 作為會限驗證頻道\n" +
                        $"驗證成功的成員將會獲得 `{role.Name}` 用戶組";
                    if (!db.GuildConfig.Any((x) => x.MemberCheckChannelId == channelId))
                        result += "\n(此YT頻道是第一次設定，最久需等待一天後才可開始檢測會限)";

                    await Context.Channel.SendConfirmAsync(result);
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
        public async Task SetNoticeMemberStatusChannel([Summary("Discord頻道Id")]ulong cId)
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
                    await db.SaveChangesAsync();

                    await Context.Channel.SendConfirmAsync($"已設定 `{channel}` 為會限驗證狀態通知頻道");                
            }
        }

        [Command("YoutubeMemberTest")]
        [Summary("TEST")]
        [Alias("YMT")]
        [RequireOwner]
        public async Task YoutubeMemberTest()
        {
            await _service.CheckMemberShip(false);
        }
    }
}
