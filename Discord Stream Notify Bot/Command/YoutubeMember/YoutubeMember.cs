using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord_Stream_Notify_Bot.Command.Attribute;
using Discord_Stream_Notify_Bot.SharedService.Youtube;
using Discord_Stream_Notify_Bot.SharedService.YoutubeMember;
using System.Linq;
using System.Threading.Tasks;

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
        [Alias("ymlc")]
        [RequireContext(ContextType.Guild)]
        [RequireBotPermission(GuildPermission.ManageRoles)]
        public async Task YoutubeMemberLoginCheck()
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var guildYoutubeMemberConfigs = db.GuildYoutubeMemberConfig.Where((x) => x.GuildId == Context.Guild.Id);
                if (!guildYoutubeMemberConfigs.Any())
                {
                    await Context.Channel.SendErrorAsync($"請向管理員確認本伺服器是否已使用會限驗證功能");
                    return;
                }

                if (guildYoutubeMemberConfigs.Any((x) => string.IsNullOrEmpty(x.MemberCheckChannelTitle) || x.MemberCheckVideoId == "-"))
                {
                    await Context.Channel.SendErrorAsync($"尚有無法檢測的頻道，請等待五分鐘Bot初始化完後重新執行此指令");
                    return;
                }

                if (!await _service.IsExistUserTokenAsync(Context.User.Id.ToString()))
                {
                    await Context.Channel.SendErrorAsync($"請先到 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 登入Discord以及Google\n登入完後再輸入一次本指令");
                    return;
                }

                IUserMessage msg;
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
                    msg = await Context.Channel.SendConfirmAsync("已記錄至資料庫，請稍等至多5分鐘讓Bot驗證\n請確認已開啟本伺服器的 `允許來自伺服器成員的私人訊息` ，以避免收不到通知");
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

                    msg = await Context.Channel.SendMessageAsync("選擇你要驗證的頻道\n" +
                        "(注意: 將移除你現有的會限驗證用戶組並重新驗證)", components: new ComponentBuilder()
                   .WithSelectMenu(selectMenuBuilder)
                   .Build());

                }

                try { msg.DeleteAfter(30); }
                catch { }
            }
        }


        [Command("AddYoutubeMemberCheckChannel")]
        [Summary("新增會限驗證頻道，目前可上限為10個頻道\n" +
            "如新增同個頻道則可變更要授予的用戶組")]
        [Alias("aymcc")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator)]
        [CommandExample("https://www.youtube.com/channel/UCdn5BQ06XqgXoAxIhbqw5Rg <@&977481980222521344>",
            "https://www.youtube.com/channel/UCR6qhsLpn62WVxCBK1dkLow 837652679303757824")]
        [Priority(0)]
        public async Task AddYoutubeMemberCheckChannel([Summary("頻道連結")] string url, [Summary("@用戶組")] IRole role)
            => await AddYoutubeMemberCheckChannel(url, role.Id);

        public async Task AddYoutubeMemberCheckChannel([Summary("頻道連結")] string url, [Summary("用戶組Id")] ulong roleId)
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
                        await Context.Channel.SendErrorAsync($"{role.Name} 的順序比我現在的身分組還高\n" +
                            $"請將我的身分組拉高後再次執行本指令");
                        return;
                    }

                    var guildConfig = db.GuildConfig.FirstOrDefault((x) => x.GuildId == Context.Guild.Id);
                    if (guildConfig == null)
                    {
                        guildConfig = new DataBase.Table.GuildConfig() { GuildId = Context.Guild.Id };
                        db.GuildConfig.Add(guildConfig);
                    }

                    if (db.GuildYoutubeMemberConfig.Count((x) => x.GuildId == Context.Guild.Id) > 10)
                    {
                        await Context.Channel.SendErrorAsync($"此伺服器已使用10個頻道做為會限驗證用\n" +
                            $"請移除未使用到的頻道來繼續新增驗證頻道");
                        return;
                    }

                    var guildYoutubeMemberConfig = db.GuildYoutubeMemberConfig.FirstOrDefault((x) => x.GuildId == Context.Guild.Id && x.MemberCheckChannelId == channelId);
                    if (guildYoutubeMemberConfig == null)
                    {
                        guildYoutubeMemberConfig = new DataBase.Table.GuildYoutubeMemberConfig()
                        { 
                            GuildId = Context.Guild.Id, 
                            MemberCheckChannelId = channelId,
                            MemberCheckGrantRoleId = roleId
                        };
                        db.GuildYoutubeMemberConfig.Add(guildYoutubeMemberConfig);
                    }
                    else
                    {
                        guildYoutubeMemberConfig.MemberCheckGrantRoleId = role.Id;
                        guildYoutubeMemberConfig.MemberCheckVideoId = "-";
                        db.GuildYoutubeMemberConfig.Update(guildYoutubeMemberConfig);
                    }
                    db.SaveChanges();

                    await Context.Channel.SendConfirmAsync($"已設定使用 `{channelId}` 作為會限驗證頻道\n" +
                        $"驗證成功的成員將會獲得 `{role.Name}` 用戶組\n" +
                        $"請等待五分鐘後才可開始檢測會限");

                    if (guildConfig.LogMemberStatusChannelId == 0)
                    {
                        await Context.Channel.SendErrorAsync("注意: 本伺服器尚未設定會限驗證紀錄頻道\n" +
                            "請新增頻道並設定本機器人`讀取`與`發送`權限後使用 `s!snmsc` 設定紀錄頻道 (`s!h snmsc`)");
                    }
                    else if (Context.Guild.GetChannelAsync(guildConfig.LogMemberStatusChannelId) == null)
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

        [Command("RemoveYoutubeMemberCheckChannel")]
        [Summary("移除會限驗證頻道")]
        [Alias("rymcc")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator)]
        [CommandExample("https://www.youtube.com/channel/UCR6qhsLpn62WVxCBK1dkLow")]
        public async Task RemoveYoutubeMemberCheckChannel([Summary("頻道連結")] string url)
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
                        await Context.Channel.SendErrorAsync("未設定過任何會限驗證");
                    }
                    else
                    {
                        var guildYoutubeMemberConfig = db.GuildYoutubeMemberConfig.FirstOrDefault((x) => x.GuildId == Context.Guild.Id && x.MemberCheckChannelId == channelId);
                        if (guildYoutubeMemberConfig == null)
                        {
                            await Context.Channel.SendErrorAsync("未設定過該頻道的會限驗證");
                        }
                        else
                        {
                            db.GuildYoutubeMemberConfig.Remove(guildYoutubeMemberConfig);
                            await Context.Channel.SendConfirmAsync($"已移除 `{channelId}` 的會限驗證");
                        }
                    }
                    db.SaveChanges();
                }
                catch (System.Exception ex)
                {
                    await Context.Channel.SendErrorAsync("資料保存失敗，請向孤之界回報");
                    Log.Error(ex.ToString());
                }
            }
        }

        [Command("ListYoutubeMemberCheckChannel")]
        [Summary("顯示現在可供驗證的會限頻道清單")]
        [Alias("lymcc")]
        [RequireContext(ContextType.Guild)]
        public async Task ListYoutubeMemberCheckChannel()
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var guildYoutubeMemberConfigs = db.GuildYoutubeMemberConfig.Where((x) => x.GuildId == Context.Guild.Id);
                if (!guildYoutubeMemberConfigs.Any())
                {
                    await Context.Channel.SendErrorAsync($"清單為空");
                    return;
                }

                if (guildYoutubeMemberConfigs.Any((x) => string.IsNullOrEmpty(x.MemberCheckChannelTitle) || x.MemberCheckVideoId == "-"))
                {
                    await Context.Channel.SendErrorAsync($"尚有無法檢測的頻道，請等待五分鐘Bot初始化完後重新執行此指令");
                    return;
                }

                await Context.Channel.SendConfirmAsync("現在可供驗證的會限頻道清單\n" +
                    string.Join('\n', guildYoutubeMemberConfigs.Select((x) =>
                        $"{Format.Url(x.MemberCheckChannelTitle, $"https://www.youtube.com/channel/{x.MemberCheckChannelId}")}: <@&{x.MemberCheckGrantRoleId}>")));
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

                var channel = await Context.Guild.GetTextChannelAsync(cId) as SocketTextChannel;
                if (channel == null)
                {
                    await Context.Channel.SendErrorAsync($"{cId} 不存在頻道");
                    return;
                }

                var permissions = (await Context.Guild.GetCurrentUserAsync()).GetPermissions(channel);
                if (!permissions.ViewChannel || !permissions.SendMessages)
                {
                    await Context.Channel.SendErrorAsync($"我在 `{channel}` 沒有 `讀取&編輯頻道` 的權限，請給予權限後再次執行本指令");
                    return;
                }

                var guildConfig = db.GuildConfig.FirstOrDefault((x) => x.GuildId == Context.Guild.Id);
                if (guildConfig == null)
                {
                    guildConfig = new DataBase.Table.GuildConfig() { GuildId = Context.Guild.Id };
                    db.GuildConfig.Add(guildConfig);
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
