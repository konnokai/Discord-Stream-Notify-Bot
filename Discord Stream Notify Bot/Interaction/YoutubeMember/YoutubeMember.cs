using Discord;
using Discord.Interactions;
using Discord.WebSocket;
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
            await DeferAsync(true);

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var guildYoutubeMemberConfigs = db.GuildYoutubeMemberConfig.Where((x) => x.GuildId == Context.Guild.Id);
                if (!guildYoutubeMemberConfigs.Any())
                {
                    await Context.Interaction.SendErrorAsync($"請向管理員確認本伺服器是否已使用會限驗證功能", true);
                    return;
                }

                if (guildYoutubeMemberConfigs.Any((x) => string.IsNullOrEmpty(x.MemberCheckChannelTitle) || x.MemberCheckVideoId == "-"))
                {
                    await Context.Interaction.SendErrorAsync($"尚有無法檢測的頻道，請等待五分鐘Bot初始化完後重新執行此指令", true);
                    return;
                }

                if (!await _service.IsExistUserTokenAsync(Context.User.Id.ToString()))
                {
                    await Context.Interaction.SendErrorAsync($"請先到 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 登入Discord以及Google\n登入完後再輸入一次本指令", true);
                    return;
                }

                if (guildYoutubeMemberConfigs.Count() == 1)
                {
                    if (!db.YoutubeMemberCheck.Any((x) =>
                        x.UserId == Context.User.Id &&
                        x.GuildId == Context.Guild.Id &&
                        x.CheckYTChannelId == guildYoutubeMemberConfigs.First().MemberCheckChannelId))
                    {
                        db.YoutubeMemberCheck.Add(new DataBase.Table.YoutubeMemberCheck()
                        {
                            UserId = Context.User.Id,
                            GuildId = Context.Guild.Id,
                            CheckYTChannelId = guildYoutubeMemberConfigs.First().MemberCheckChannelId
                        });
                        db.SaveChanges();
                    }
                    await Context.Interaction.SendConfirmAsync("已記錄至資料庫，請稍等至多5分鐘讓Bot驗證\n請確認已開啟本伺服器的 `允許來自伺服器成員的私人訊息` ，以避免收不到通知", true, true);
                }
                else
                {
                    SelectMenuBuilder selectMenuBuilder = new SelectMenuBuilder()
                       .WithPlaceholder("頻道")
                       .WithMinValues(1)
                       .WithMaxValues(guildYoutubeMemberConfigs.Count())
                       .WithCustomId($"member:check:{Context.Guild.Id}:{Context.User.Id}");

                    foreach (var item in guildYoutubeMemberConfigs)
                        selectMenuBuilder.AddOption(item.MemberCheckChannelTitle, item.MemberCheckChannelId);

                    await Context.Interaction.FollowupAsync("選擇你要驗證的頻道\n" +
                        "(注意: 將移除你現有的會限驗證用戶組並重新驗證)", components: new ComponentBuilder()
                   .WithSelectMenu(selectMenuBuilder)
                   .Build());
                }
            }
        }

        [SlashCommand("unlink", "解除Discord與Google綁定並移除授權")]
        public async Task UnlinkAsync()
        {
            await DeferAsync(true);

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (await _service.IsExistUserTokenAsync(Context.User.Id.ToString()))
                {
                    if (!await PromptUserConfirmAsync("確定解除綁定?\n" +
                        "(注意: 解除綁定後也會一併解除會限用戶組，如要重新獲得需重新至網站綁定)"))
                        return;

                    await Program.RedisSub.PublishAsync("member.revokeToken", Context.User.Id);

                    try
                    {
                        await _service.RevokeUserGoogleCert(Context.User.Id.ToString());
                        await Context.Interaction.SendConfirmAsync("已解除完成", true, true);
                    }
                    catch (NullReferenceException nullEx)
                    {
                        await Context.Interaction.SendErrorAsync($"已解除綁定但無法取消Google端授權\n" +
                           $"請到 {Format.Url("Google安全性", "https://myaccount.google.com/permissions")} 移除 `直播小幫手會限確認` 的應用程式存取權", true, true);
                        Log.Warn($"RevokeTokenNull: {nullEx.Message} ({Context.User.Id})");
                    }
                    catch (Exception)
                    {
                        await Context.Interaction.SendErrorAsync($"解除綁定失敗，請向 {Program.ApplicatonOwner} 確認問題", true, true);
                    }
                }
                else
                {
                    await Context.Interaction.SendErrorAsync($"無資料可供解除綁定...", true, true);
                }
            }
        }

        [SlashCommand("list-check-channel", "顯示現在可供驗證的會限頻道清單")]
        [RequireContext(ContextType.Guild)]
        public async Task ListCheckChannel()
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var guildYoutubeMemberConfigs = db.GuildYoutubeMemberConfig.Where((x) => x.GuildId == Context.Guild.Id);
                if (!guildYoutubeMemberConfigs.Any())
                {
                    await Context.Interaction.SendErrorAsync($"清單為空");
                    return;
                }

                if (guildYoutubeMemberConfigs.Any((x) => string.IsNullOrEmpty(x.MemberCheckChannelTitle) || x.MemberCheckVideoId == "-"))
                {
                    await Context.Interaction.SendErrorAsync($"尚有無法檢測的頻道，請等待五分鐘Bot初始化完後重新執行此指令");
                    return;
                }

                await Context.Interaction.SendConfirmAsync("現在可供驗證的會限頻道清單\n" +
                    string.Join('\n', guildYoutubeMemberConfigs.Select((x) =>
                        $"{Format.Url(x.MemberCheckChannelTitle, $"https://www.youtube.com/channel/{x.MemberCheckChannelId}")}")),
                    false, true);
            }
        }
    }
}