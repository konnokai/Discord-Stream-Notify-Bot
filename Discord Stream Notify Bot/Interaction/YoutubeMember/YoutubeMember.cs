using Discord.Interactions;
using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.SharedService.YoutubeMember;
using System.Diagnostics;

namespace Discord_Stream_Notify_Bot.Interaction.YoutubeMember
{
    [Group("member", "YouTube 會限驗證相關指令")]
    public class YoutubeMember : TopLevelModule<YoutubeMemberService>
    {
        private readonly MainDbService _dbService;
        public YoutubeMember(MainDbService dbService)
        {
            _dbService = dbService;
        }

        [RequireContext(ContextType.Guild)]
        [SlashCommand("check", "確認是否已到網站登入綁定")]
        public async Task CheckAsync()
        {
            await DeferAsync(true);

            if (!_service.IsEnable)
            {
                await Context.Interaction.SendErrorAsync($"該 Bot 未啟用會限驗證系統，請向 {Bot.ApplicatonOwner} 確認", true);
                return;
            }

            try
            {
                using (var db = _dbService.GetDbContext())
                {
                    var guildYoutubeMemberConfigs = db.GuildYoutubeMemberConfig.Where((x) => x.GuildId == Context.Guild.Id);
                    if (!guildYoutubeMemberConfigs.Any())
                    {
                        await Context.Interaction.SendErrorAsync($"請向管理員確認本伺服器是否已使用會限驗證功能", true);
                        return;
                    }

                    if (guildYoutubeMemberConfigs.Any((x) => string.IsNullOrEmpty(x.MemberCheckChannelTitle) || x.MemberCheckVideoId == "-"))
                    {
                        await Context.Interaction.SendErrorAsync($"尚有無法檢測的頻道，請等待五分鐘 Bot 初始化完後重新執行此指令", true);
                        return;
                    }

                    if (!await _service.IsExistUserTokenAsync(Context.User.Id.ToString()))
                    {
                        await Context.Interaction.SendErrorAsync($"請先到 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 登入 Discord 以及 Google\n登入完後再輸入一次本指令", true);
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
                        // Todo: 超過 25 個選項時需提供換頁的選項
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
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "Member Check Error");
                await Context.Interaction.SendErrorAsync($"出現錯誤: {ex.Message}", true);
            }
        }

        [RequireContext(ContextType.Guild)]
        [SlashCommand("cancel-member-check", "取消本伺服器的會限驗證，會一併移除會限驗證用戶組")]
        public async Task CancelMemberCheckAsync()
        {
            await DeferAsync(true);

            using (var db = _dbService.GetDbContext())
            {
                try
                {
                    var youtubeMemberChecks = db.YoutubeMemberCheck.Where((x) => x.UserId == Context.User.Id && x.GuildId == Context.Guild.Id);
                    if (!youtubeMemberChecks.Any())
                    {
                        await Context.Interaction.SendErrorAsync("你尚未在本伺服器上運行會限驗證", true);
                        return;
                    }

                    var guildYoutubeMemberConfigs = db.GuildYoutubeMemberConfig.Where((x) => x.GuildId == Context.Guild.Id);
                    foreach (var item in guildYoutubeMemberConfigs)
                    {
                        try
                        {
                            await Context.Client.Rest.RemoveRoleAsync(Context.Guild.Id, Context.User.Id, item.MemberCheckGrantRoleId);
                        }
                        catch { }
                    }

                    db.YoutubeMemberCheck.RemoveRange(youtubeMemberChecks);
                    db.SaveChanges();

                    await Context.Interaction.SendConfirmAsync($"已移除你在本伺服器上會限驗證", true);
                }
                catch (Exception ex)
                {
                    await Context.Interaction.SendErrorAsync($"資料庫儲存失敗，請向 {Bot.ApplicatonOwner} 確認", true);
                    Log.Error(ex.ToString());
                }
            }
        }

        [SlashCommand("unlink", "解除 Discord 與 Google 綁定並移除授權")]
        public async Task UnlinkAsync()
        {
            await DeferAsync(true);

            if (!_service.IsEnable)
            {
                await Context.Interaction.SendErrorAsync($"該 Bot 未啟用會限驗證系統，請向 {Bot.ApplicatonOwner} 確認", true);
                return;
            }

            using (var db = _dbService.GetDbContext())
            {
                if (await _service.IsExistUserTokenAsync(Context.User.Id.ToString()))
                {
                    if (!await PromptUserConfirmAsync("確定解除綁定?\n" +
                        "(注意: 解除綁定後也會一併解除會限用戶組，如要重新獲得需重新至網站綁定)"))
                        return;

                    await Bot.RedisSub.PublishAsync(new RedisChannel("member.revokeToken", RedisChannel.PatternMode.Literal), Context.User.Id);

                    try
                    {
                        await _service.RevokeUserGoogleCertAsync(Context.User.Id.ToString());
                        await Context.Interaction.SendConfirmAsync("已解除完成", true, true);
                    }
                    catch (NullReferenceException nullEx)
                    {
                        await Context.Interaction.SendErrorAsync($"已解除綁定但無法取消 Google 端授權\n" +
                           $"請到 {Format.Url("Google 安全性", "https://myaccount.google.com/permissions")} 移除 `直播小幫手會限確認` 的應用程式存取權", true, true);
                        Log.Warn($"RevokeTokenNull: {nullEx.Message} ({Context.User.Id})");
                    }
                    catch (Exception)
                    {
                        await Context.Interaction.SendErrorAsync($"解除綁定失敗，請向 {Bot.ApplicatonOwner} 確認問題", true, true);
                    }
                }
                else
                {
                    await Context.Interaction.SendErrorAsync($"無資料可供解除綁定...", true, true);
                }
            }
        }

        [RequireContext(ContextType.Guild)]
        [SlashCommand("list-can-check-channel", "顯示現在可供驗證的會限頻道清單")]
        public async Task ListCheckChannel()
        {
            using (var db = _dbService.GetDbContext())
            {
                var guildYoutubeMemberConfigs = db.GuildYoutubeMemberConfig.Where((x) => x.GuildId == Context.Guild.Id);
                if (!guildYoutubeMemberConfigs.Any())
                {
                    await Context.Interaction.SendErrorAsync($"清單為空");
                    return;
                }

                if (guildYoutubeMemberConfigs.Any((x) => string.IsNullOrEmpty(x.MemberCheckChannelTitle) || x.MemberCheckVideoId == "-"))
                {
                    await Context.Interaction.SendErrorAsync($"尚有無法檢測的頻道，請等待五分鐘 Bot 初始化完後重新執行此指令");
                    return;
                }

                await Context.Interaction.SendConfirmAsync("現在可供驗證的會限頻道清單\n" +
                    string.Join('\n', guildYoutubeMemberConfigs.Select((x) =>
                        $"{Format.Url(x.MemberCheckChannelTitle, $"https://www.youtube.com/channel/{x.MemberCheckChannelId}")}: <@&{x.MemberCheckGrantRoleId}>")),
                    false, true);
            }
        }

        [SlashCommand("show-my-youtube-account", "顯示現在綁定的 Youtube 帳號")]
        public async Task ShowYoutubeAccountAsync()
        {
            await DeferAsync(true);

            if (!_service.IsEnable)
            {
                await Context.Interaction.SendErrorAsync($"該 Bot 未啟用會限驗證系統，請向 {Bot.ApplicatonOwner} 確認", true);
                return;
            }

            try
            {
                var channelUrl = await _service.GetYoutubeDataAsync(Context.User.Id.ToString());
                await Context.Interaction.SendConfirmAsync($"你已綁定的頻道: {channelUrl}", true);
            }
            catch (NullReferenceException nullEx)
            {
                switch (nullEx.Message)
                {
                    case "userId":
                        await Context.Interaction.SendErrorAsync("UserId 錯誤", true);
                        break;
                    case "token":
                    case "userCert":
                    case "channel":
                        await Context.Interaction.SendErrorAsync("錯誤，請確認是否已到網站上綁定或此 Google 帳號存在 Youtube 頻道", true);
                        break;
                    default:
                        await Context.Interaction.SendErrorAsync($"錯誤，請確認是否已到網站上綁定或此 Google 帳號存在 Youtube 頻道\n" +
                            $"如有疑問請向 `{Bot.ApplicatonOwner}` 詢問", true);
                        Log.Error(nullEx.ToString());
                        break;
                }
            }
            catch (Exception ex)
            {
                await Context.Interaction.SendErrorAsync($"錯誤，請確認是否已到網站上綁定或此 Google 帳號存在 Youtube 頻道\n" +
                    $"如有疑問請向 `{Bot.ApplicatonOwner}` 詢問", true);
                Log.Error(ex.ToString());
            }
        }
    }
}