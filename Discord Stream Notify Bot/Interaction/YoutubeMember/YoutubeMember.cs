using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Interaction.YoutubeMember
{
    [Group("youtube-member", "yt-member")]
    public class YoutubeMember : TopLevelModule<SharedService.YoutubeMember.YoutubeMemberService>
    {
        [SlashCommand("check", "確認是否已到網站登入並記錄至資料庫")]
        [RequireContext(ContextType.Guild)]
        public async Task CheckAsync()
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var guildConfig = db.GuildConfig.Include((x) => x.MemberCheck).First((x) => x.GuildId == Context.Guild.Id);
                if (string.IsNullOrEmpty(guildConfig.MemberCheckChannelId) || guildConfig.MemberCheckGrantRoleId == 0 || string.IsNullOrEmpty(guildConfig.MemberCheckVideoId))
                {
                    await Context.Interaction.SendErrorAsync($"請向管理員確認本伺服器是否已開啟會限驗證功能", ephemeral: true);
                    return;
                }

                if (db.MemberAccessToken.Any((x) => x.DiscordUserId == Context.User.Id.ToString()))
                {
                    if (!guildConfig.MemberCheck.Any((x) => x.UserId == Context.User.Id))
                    {
                        guildConfig.MemberCheck.Add(new DataBase.Table.YoutubeMemberCheck() { UserId = Context.User.Id });
                        db.SaveChanges();
                    }
                    await Context.Interaction.SendConfirmAsync("已記錄至資料庫，請稍等至多5分鐘讓Bot驗證\n請確認已開啟本伺服器的 `允許來自伺服器成員的私人訊息` ，以避免收不到通知", ephemeral: true);
                }
                else
                {
                    await Context.Interaction.SendErrorAsync($"請先到 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 登入Discord以及Google\n登入完後再輸入一次本指令", ephemeral: true);
                }
            }
        }
    }
}