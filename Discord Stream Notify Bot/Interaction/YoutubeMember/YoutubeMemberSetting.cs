using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Discord_Stream_Notify_Bot.Interaction.Attribute;
using Discord_Stream_Notify_Bot.SharedService.Youtube;
using Discord_Stream_Notify_Bot.SharedService.YoutubeMember;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Interaction.YoutubeMember
{
    [Group("member-set", "會限驗證設定")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [EnabledInDm(false)]
    public class YoutubeMemberSetting : TopLevelModule<YoutubeMemberService>
    {
        private readonly DiscordSocketClient _client;
        private readonly YoutubeStreamService _ytservice;

        public YoutubeMemberSetting(DiscordSocketClient client, YoutubeStreamService youtubeStreamService)
        {
            _client = client;
            _ytservice = youtubeStreamService;
        }

        public class GuildYoutubeMemberCheckChannelIdAutocompleteHandler : AutocompleteHandler
        {
            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
            {
                using var db = DataBase.DBContext.GetDbContext();
                if (!db.GuildYoutubeMemberConfig.Any((x) => x.GuildId == context.Guild.Id))
                    return AutocompletionResult.FromSuccess();

                var channelIdList = db.GuildYoutubeMemberConfig.Where((x) => x.GuildId == context.Guild.Id).Select((x) => new KeyValuePair<string, string>(x.MemberCheckChannelTitle, x.MemberCheckChannelId));

                var channelIdList2 = new Dictionary<string, string>();
                try
                {
                    string value = autocompleteInteraction.Data.Current.Value.ToString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        foreach (var item in channelIdList)
                        {
                            if (item.Key.Contains(value, StringComparison.CurrentCultureIgnoreCase) || item.Value.Contains(value, StringComparison.CurrentCultureIgnoreCase))
                            {
                                channelIdList2.Add(item.Key, item.Value);
                            }
                        }
                    }
                    else
                    {
                        foreach (var item in channelIdList)
                        {
                            channelIdList2.Add(item.Key, item.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"GuildYoutubeMemberCheckChannelIdAutocompleteHandler - {ex}");
                }

                List<AutocompleteResult> results = new();
                foreach (var item in channelIdList2)
                {
                    results.Add(new AutocompleteResult(item.Key, item.Value));
                }

                return AutocompletionResult.FromSuccess(results.Take(25));
            }
        }

        [SlashCommand("set-notice-member-status-channel", "設定會限驗證狀態紀錄頻道")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        public async Task SetNoticeMemberStatusChannel([Summary("紀錄頻道")] ITextChannel textChannel)
        {
            await DeferAsync(true);
            
            if (!_service.Enable)
            {
                await Context.Interaction.SendErrorAsync($"該Bot未啟用會限驗證系統，請向 {Program.ApplicatonOwner} 確認", true);
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var permissions = Context.Guild.GetUser(_client.CurrentUser.Id).GetPermissions(textChannel);
                if (!permissions.ViewChannel || !permissions.SendMessages)
                {
                    await Context.Interaction.SendErrorAsync($"我在 `{textChannel}` 沒有 `讀取&編輯頻道` 的權限，請給予權限後再次執行本指令", true);
                    return;
                }

                if (!permissions.EmbedLinks)
                {
                    await Context.Interaction.SendErrorAsync($"我在 `{textChannel}` 沒有 `嵌入連結` 的權限，請給予權限後再次執行本指令", true);
                    return;
                }

                var guildConfig = db.GuildConfig.FirstOrDefault((x) => x.GuildId == Context.Guild.Id);
                if (guildConfig == null)
                {
                    guildConfig = new DataBase.Table.GuildConfig() { GuildId = Context.Guild.Id };
                    db.GuildConfig.Add(guildConfig);
                }

                guildConfig.LogMemberStatusChannelId = textChannel.Id;
                db.GuildConfig.Update(guildConfig);
                db.SaveChanges();

                await Context.Interaction.SendConfirmAsync($"已設定 `{textChannel}` 為會限驗證狀態通知頻道", true);
            }
        }

        [SlashCommand("add-member-check", "新增會限驗證頻道")]
        [CommandSummary("新增會限驗證頻道，目前可上限為20個頻道\n" +
           "如新增同個頻道則可變更要授予的用戶組")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [CommandExample("https://www.youtube.com/channel/UCR6qhsLpn62WVxCBK1dkLow @peeps🕊")]
        public async Task AddMemberCheckAsync([Summary("頻道連結")] string url, [Summary("用戶組Id")] IRole role)
        {
            if (!_service.Enable)
            {
                await Context.Interaction.SendErrorAsync($"該Bot未啟用會限驗證系統，請向 {Program.ApplicatonOwner} 確認");
                return;
            }

            var currentBotUser = Context.Guild.GetUser(_client.CurrentUser.Id);
            if (!currentBotUser.GuildPermissions.ManageRoles)
            {
                await Context.Interaction.SendErrorAsync("我沒有 `管理身分組` 的權限，請給予權限後再次執行本指令");
                return;
            }

            if (role == Context.Guild.EveryoneRole)
            {
                await Context.Interaction.SendErrorAsync("不可設定everyone用戶組，這用戶組每個人都有了你怎麼還會想設定?");
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                try
                {
                    await DeferAsync(true);

                    if (currentBotUser.Roles.Max(x => x.Position) < role.Position)
                    {
                        await Context.Interaction.SendErrorAsync($"{role.Name} 的順序比我現在的身分組還高\n" +
                            $"請將我的身分組拉高後再次執行本指令", true);
                        return;
                    }

                    var guildConfig = db.GuildConfig.FirstOrDefault((x) => x.GuildId == Context.Guild.Id);
                    if (guildConfig == null)
                    {
                        guildConfig = new DataBase.Table.GuildConfig() { GuildId = Context.Guild.Id };
                        db.GuildConfig.Add(guildConfig);
                    }

                    if (db.GuildYoutubeMemberConfig.Count((x) => x.GuildId == Context.Guild.Id) > 20)
                    {
                        await Context.Interaction.SendErrorAsync($"此伺服器已使用20個頻道做為會限驗證用\n" +
                            $"請移除未使用到的頻道來繼續新增驗證頻道", true);
                        return;
                    }

                    if (guildConfig.LogMemberStatusChannelId == 0)
                    {
                        await Context.Interaction.SendErrorAsync("本伺服器尚未設定會限驗證紀錄頻道\n" +
                            "請新增頻道並設定本機器人 `讀取` & `發送` 與 `嵌入連結` 權限後使用 `/youtube-member-set set-notice-member-status-channel` 設定紀錄頻道\n" +
                            "紀錄頻道為強制需要，若無頻道則無法驗證會限", true);
                        return;
                    }
                    else if (Context.Guild.GetTextChannel(guildConfig.LogMemberStatusChannelId) == null)
                    {
                        await Context.Interaction.SendErrorAsync("本伺服器所設定的會限驗證紀錄頻道已刪除\n" +
                            "請新增頻道並設定本機器人 `讀取` & `發送` 與 `嵌入連結` 權限後使用 `/youtube-member-set set-notice-member-status-channel` 設定紀錄頻道\n" +
                            "紀錄頻道為強制需要，若無頻道則無法驗證會限", true);

                        guildConfig.LogMemberStatusChannelId = 0;
                        db.GuildConfig.Update(guildConfig);
                        db.SaveChanges();
                        return;
                    }

                    var channelId = await _ytservice.GetChannelIdAsync(url);
                    bool channelDataExist = false;
                    var guildYoutubeMemberConfig = db.GuildYoutubeMemberConfig.FirstOrDefault((x) => x.GuildId == Context.Guild.Id && x.MemberCheckChannelId == channelId);
                    if (guildYoutubeMemberConfig == null)
                    {
                        guildYoutubeMemberConfig = new DataBase.Table.GuildYoutubeMemberConfig()
                        {
                            GuildId = Context.Guild.Id,
                            MemberCheckChannelId = channelId,
                            MemberCheckGrantRoleId = role.Id
                        };

                        var youtubeChannel = db.GuildYoutubeMemberConfig.FirstOrDefault((x) => x.MemberCheckChannelId == channelId && !string.IsNullOrEmpty(x.MemberCheckChannelTitle) && x.MemberCheckVideoId != "-");
                        if (youtubeChannel != null)
                        {
                            guildYoutubeMemberConfig.MemberCheckChannelTitle = youtubeChannel.MemberCheckChannelTitle;
                            guildYoutubeMemberConfig.MemberCheckVideoId = youtubeChannel.MemberCheckVideoId;
                            channelDataExist = true;
                        }

                        db.GuildYoutubeMemberConfig.Add(guildYoutubeMemberConfig);
                    }
                    else
                    {
                        channelDataExist = true;
                        guildYoutubeMemberConfig.MemberCheckGrantRoleId = role.Id;
                        db.GuildYoutubeMemberConfig.Update(guildYoutubeMemberConfig);
                    }
                    db.SaveChanges();

                    await Context.Interaction.SendConfirmAsync($"已設定使用 `{channelId}` 作為會限驗證頻道\n" +
                        $"驗證成功的成員將會獲得 `{role.Name}` 用戶組\n" +
                        (channelDataExist ? "可直接開始檢測會限" : "請等待五分鐘後才可開始檢測會限"), true, true);

                    try
                    {
                        await (await Program.ApplicatonOwner.CreateDMChannelAsync()).SendMessageAsync(embed: new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("已新增會限驗證頻道")
                            .AddField("頻道", Format.Url(channelId, $"https://www.youtube.com/channel/{channelId}"), false)
                            .AddField("伺服器", $"{Context.Guild.Name} ({Context.Guild.Id})", false)
                            .AddField("執行者", $"{Context.User.Username} ({Context.User.Id})", false).Build());
                    }
                    catch (Exception ex) { Log.Error(ex.ToString()); }
                }
                catch (Exception ex)
                {
                    await Context.Interaction.SendErrorAsync(ex.Message, true);
                    Log.Error(ex.ToString());
                }
            }
        }

        [SlashCommand("remove-member-check", "移除會限驗證頻道")]
        [CommandSummary("移除會限驗證頻道")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [CommandExample("https://www.youtube.com/channel/UCR6qhsLpn62WVxCBK1dkLow")]
        public async Task RemoveMemberCheckAsync([Summary("頻道連結"), Autocomplete(typeof(GuildYoutubeMemberCheckChannelIdAutocompleteHandler))] string url)
        {
            await DeferAsync(true);

            using (var db = DataBase.DBContext.GetDbContext())
            {
                try
                {
                    var channelId = await _ytservice.GetChannelIdAsync(url);
                    var guildYoutubeMemberConfig = db.GuildYoutubeMemberConfig.FirstOrDefault((x) => x.GuildId == Context.Guild.Id && x.MemberCheckChannelId == channelId);

                    if (guildYoutubeMemberConfig == null)
                    {
                        await Context.Interaction.SendErrorAsync("未設定過該頻道的會限驗證", true);
                    }
                    else
                    {
                        db.GuildYoutubeMemberConfig.Remove(guildYoutubeMemberConfig);
                        await Context.Interaction.SendConfirmAsync($"已移除 `{channelId}` 的會限驗證", true);

                        try
                        {
                            await (await Program.ApplicatonOwner.CreateDMChannelAsync()).SendMessageAsync(embed: new EmbedBuilder()
                                .WithOkColor()
                                .WithTitle("已移除會限驗證頻道")
                                .AddField("頻道", Format.Url(channelId, $"https://www.youtube.com/channel/{channelId}"), false)
                                .AddField("伺服器", $"{Context.Guild.Name} ({Context.Guild.Id})", false)
                                .AddField("執行者", $"{Context.User.Username} ({Context.User.Id})", false).Build());
                        }
                        catch (Exception ex) { Log.Error(ex.ToString()); }
                    }

                    db.SaveChanges();
                }
                catch (Exception ex)
                {
                    await Context.Interaction.SendErrorAsync("資料保存失敗，請向孤之界回報", true);
                    Log.Error(ex.ToString());
                }
            }
        }


        [SlashCommand("list-checked-member", "顯示現在已成功驗證的成員清單")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ListCheckedMemberAsync([Summary("頁數")] int page = 1)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var youtubeMemberChecks = db.YoutubeMemberCheck.Where((x) => x.GuildId == Context.Guild.Id && x.IsChecked);
                if (!youtubeMemberChecks.Any())
                {
                    await Context.Interaction.SendErrorAsync("尚無成員驗證成功");
                    return;
                }
                page -= 1;
                page = Math.Max(0, page);

                await Context.SendPaginatedConfirmAsync(page, (page) =>
                {
                    return new EmbedBuilder().WithOkColor()
                    .WithTitle("已驗證成功清單")
                    .WithDescription(string.Join('\n',
                        youtubeMemberChecks.Skip(page * 20).Take(20)
                            .Select((x) => $"<@{x.UserId}>: {x.CheckYTChannelId}")));
                }, youtubeMemberChecks.Count(), 20, true, true);
            }
        }
    }
}
