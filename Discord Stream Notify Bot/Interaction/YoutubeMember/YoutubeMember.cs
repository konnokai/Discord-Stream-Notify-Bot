using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Interaction.YoutubeMember
{
    [Group("youtube-member", "yt-member")]
    public class YoutubeMember : TopLevelModule<SharedService.YoutubeMember.YoutubeMemberService>
    {
        private readonly DiscordSocketClient _client;
        public YoutubeMember(DiscordSocketClient client)
        {
            _client = client;
        }

        [SlashCommand("check", "確認是否已到網站登入綁定")]
        [RequireContext(ContextType.Guild)]
        public async Task CheckAsync()
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var guildConfig = db.GuildConfig.Include((x) => x.MemberCheck).FirstOrDefault((x) => x.GuildId == Context.Guild.Id);
                if (guildConfig == null)
                {
                    guildConfig = new DataBase.Table.GuildConfig() { GuildId = Context.Guild.Id };
                    db.GuildConfig.Add(guildConfig);
                    db.SaveChanges();
                }

                if (string.IsNullOrEmpty(guildConfig.MemberCheckChannelId) || guildConfig.MemberCheckGrantRoleId == 0 || string.IsNullOrEmpty(guildConfig.MemberCheckVideoId))
                {
                    await Context.Interaction.SendErrorAsync($"請向管理員確認本伺服器是否已開啟會限驗證功能", ephemeral: true);
                    return;
                }

                if (await _service.IsExistUserTokenAsync(Context.User.Id.ToString()))
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

        [SlashCommand("unlink", "解除Discord與Google綁定並移除授權")]
        public async Task UnlinkAsync()
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var guildConfigs = db.GuildConfig.Include((x) => x.MemberCheck).Where((x) => x.MemberCheck.Any((x2) => x2.UserId == Context.User.Id));
                var youtubeMembers = db.YoutubeMemberCheck.Where((x) => x.UserId == Context.User.Id);

                if (await _service.IsExistUserTokenAsync(Context.User.Id.ToString()))
                {
                    if (!await PromptUserConfirmAsync("確定解除綁定?\n" +
                        "(注意: 解除綁定後也會一併解除會限用戶組，如要重新獲得需重新至網站綁定)"))
                        return;

                    await Context.Interaction.DeferAsync(true);

                    if (guildConfigs.Any())
                    {
                        foreach (var item in guildConfigs)
                        {
                            try { await _client.Rest.RemoveRoleAsync(item.GuildId, Context.User.Id, item.MemberCheckGrantRoleId); }
                            catch (Exception) { }
                        }
                    }

                    db.YoutubeMemberCheck.RemoveRange(youtubeMembers);
                    db.SaveChanges();

                    try
                    {
                        await _service.RevokeUserGoogleCert(Context.User.Id.ToString());
                        await Context.Interaction.SendConfirmAsync("已解除完成", true);
                    }
                    catch (NullReferenceException nullEx)
                    {
                        await Context.Interaction.SendErrorAsync($"已解除綁定但無法取消Google端授權\n" +
                           $"請到 {Format.Url("Google安全性", "https://myaccount.google.com/permissions")} 移除 `直播小幫手會限確認` 的應用程式存取權", true);
                        Log.Warn($"RevokeTokenNull: {nullEx.Message} ({Context.User.Id})");
                    }
                    catch (Exception)
                    {
                        await Context.Interaction.SendErrorAsync($"解除綁定失敗，請向 {Program.ApplicatonOwner} 確認問題", true);
                    }
                }
                else
                {
                    await Context.Interaction.SendErrorAsync($"無資料可供解除綁定...", ephemeral: true);
                }
            }
        }
    }
}