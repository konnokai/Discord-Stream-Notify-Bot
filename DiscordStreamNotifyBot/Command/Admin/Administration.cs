using Discord.Commands;
using DiscordStreamNotifyBot.DataBase;

namespace DiscordStreamNotifyBot.Command.Admin
{
    public class Administration : TopLevelModule<AdministrationService>
    {
        private readonly DiscordSocketClient _client;
        private readonly MainDbService _dbService;

        public Administration(DiscordSocketClient discordSocketClient, MainDbService dbService)
        {
            _client = discordSocketClient;
            _dbService = dbService;
        }

        // 暫時移除，ChangeStatus 現在並非 Static
        //[RequireContext(ContextType.DM)]
        //[Command("UpdateStatus")]
        //[Summary("更新機器人的狀態\n參數: Guild, Member, Stream, StreamCount, Info")]
        //[Alias("UpStats")]
        //[RequireOwner]
        //public async Task UpdateStatusAsync([Summary("狀態")] string stats)
        //{
        //    switch (stats.ToLowerInvariant())
        //    {
        //        case "guild":
        //            Bot.Status = Bot.BotPlayingStatus.Guild;
        //            break;
        //        case "member":
        //            Bot.Status = Bot.BotPlayingStatus.Member;
        //            break;
        //        case "stream":
        //            Bot.Status = Bot.BotPlayingStatus.Stream;
        //            break;
        //        case "streamcount":
        //            Bot.Status = Bot.BotPlayingStatus.StreamCount;
        //            break;
        //        case "info":
        //            Bot.Status = Bot.BotPlayingStatus.Info;
        //            break;
        //        default:
        //            await Context.Channel.SendConfirmAsync(string.Format("找不到 {0} 狀態", stats));
        //            return;
        //    }

        //    Bot.ChangeStatus();
        //}

        [RequireContext(ContextType.DM)]
        [Command("ListServer")]
        [Summary("顯示所有的伺服器")]
        [Alias("LS")]
        [RequireOwner]
        public async Task ListServerAsync([Summary("頁數")] int page = 0)
        {
            await Context.SendPaginatedConfirmAsync(page, (cur) =>
            {
                EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor().WithTitle("目前所在的伺服器有");

                foreach (var item in _client.Guilds.Skip(cur * 5).Take(5))
                {
                    int totalMember = item.MemberCount;

                    embedBuilder.AddField(item.Name, "Id: " + item.Id +
                        "\nOwner Id: " + item.OwnerId +
                        "\n人數: " + totalMember.ToString());
                }

                return embedBuilder;
            }, _client.Guilds.Count, 5);
        }

        [RequireContext(ContextType.DM)]
        [Command("SearchServer")]
        [Summary("查詢伺服器")]
        [Alias("SS")]
        [RequireOwner]
        public async Task SearchServerAsync([Summary("關鍵字")] string keyword = "", [Summary("頁數")] int page = 0)
        {
            if (ulong.TryParse(keyword, out ulong guildId))
            {
                var guild = _client.GetGuild(guildId);
                if (guild != null)
                {
                    var embed = new EmbedBuilder().WithOkColor().AddField(guild.Name,
                        $"Id: {guild.Id}\n" +
                        $"擁有者Id: {guild.OwnerId}\n" +
                        $"人數: {guild.MemberCount}\n").Build();

                    await Context.Channel.SendMessageAsync(embed: embed);
                    return;
                }
            }

            var list = _client.Guilds.Where((x) => x.Name.Contains(keyword, StringComparison.InvariantCultureIgnoreCase));
            if (!list.Any())
            {
                await Context.Channel.SendErrorAsync("該關鍵字無伺服器");
                return;
            }

            await Context.SendPaginatedConfirmAsync(page, (cur) =>
            {
                EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor().WithTitle($"查詢 `{keyword}` 後的伺服器有");

                foreach (var item in list.Skip(cur * 5).Take(5))
                {
                    embedBuilder.AddField(item.Name,
                        $"Id: {item.Id}\n" +
                        $"擁有者Id: {item.OwnerId}\n" +
                        $"人數: {item.MemberCount}\n");
                }

                embedBuilder.WithFooter($"總數量: {list.Count()}");

                return embedBuilder;
            }, list.Count(), 5, false);
        }

        [RequireContext(ContextType.DM)]
        [Command("Die")]
        [Summary("關閉機器人")]
        [Alias("Bye")]
        [RequireOwner]
        public async Task DieAsync()
        {
            Bot.IsDisconnect = true;
            Bot.IsHoloChannelSpider = false;
            Bot.IsNijisanjiChannelSpider = false;
            Bot.IsOtherChannelSpider = false;
            await Context.Channel.SendConfirmAsync("關閉中");
        }

        [RequireContext(ContextType.DM)]
        [Command("Leave")]
        [Summary("讓機器人離開指定的伺服器")]
        [RequireOwner]
        public async Task LeaveAsync([Summary("伺服器Id")] ulong gid = 0)
        {
            if (gid == 0) { await Context.Channel.SendErrorAsync("伺服器Id為空"); return; }

            var guild = _client.GetGuild(gid);
            if (guild == null)
            {
                await Context.Channel.SendErrorAsync("伺服器不存在");
                return;
            }

            try { await guild.LeaveAsync(); }
            catch (Exception) { await Context.Channel.SendErrorAsync("失敗，請確認Id是否正確"); return; }

            await Context.Channel.SendConfirmAsync("✅");
        }

        [RequireContext(ContextType.DM)]
        [Command("GetInviteURL")]
        [Summary("取得伺服器的邀請連結")]
        [Alias("invite")]
        [RequireOwner]
        public async Task GetInviteURLAsync([Summary("伺服器Id")] ulong gid = 0, [Summary("頻道Id")] ulong cid = 0)
        {
            if (gid == 0) gid = Context.Guild.Id;
            SocketGuild guild = _client.GetGuild(gid);
            if (guild == null)
            {
                await Context.Channel.SendErrorAsync($"伺服器 {gid} 不存在");
                return;
            }

            try
            {
                if (cid == 0)
                {
                    // 忽略 ticket- & closed- 開頭的頻道
                    IReadOnlyCollection<SocketTextChannel> socketTextChannels = [.. guild.TextChannels.Where((x) => !x.Name.StartsWith("ticket-") && !x.Name.StartsWith("closed-"))];

                    await Context.SendPaginatedConfirmAsync(0, (cur) =>
                    {
                        EmbedBuilder embedBuilder = new EmbedBuilder()
                           .WithOkColor()
                           .WithTitle("以下為 " + guild.Name + " 所有的文字頻道")
                           .WithDescription(string.Join('\n', socketTextChannels.Skip(cur * 20).Take(20).Select((x) => x.Id + " / " + x.Name)));

                        return embedBuilder;
                    }, socketTextChannels.Count, 20);
                }
                else
                {
                    IInviteMetadata invite = await guild.GetTextChannel(cid).CreateInviteAsync(300, 1, false);
                    await Context.Channel.SendConfirmAsync(invite.Url);
                }
            }
            catch (Discord.Net.HttpException httpEx)
            {
                if (httpEx.DiscordCode == DiscordErrorCode.InsufficientPermissions || httpEx.DiscordCode == DiscordErrorCode.MissingPermissions)
                    await Context.Channel.SendErrorAsync("缺少邀請權限");
                else
                    Log.Error(httpEx.ToString());
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
        }

        [RequireContext(ContextType.DM)]
        [Command("GuildInfo")]
        [Summary("顯示伺服器資訊")]
        [Alias("ginfo")]
        [RequireOwner]
        public async Task GuildInfo(ulong gid = 0)
        {
            try
            {
                if (gid == 0)
                {
                    await Context.Channel.SendErrorAsync("GuildId 不可為空").ConfigureAwait(false);
                    return;
                }

                var guild = _client.GetGuild(gid);
                if (guild == null)
                {
                    await Context.Channel.SendErrorAsync("找不到指定的伺服器").ConfigureAwait(false);
                    return;
                }

                string result = $"伺服器名稱: **{guild.Name}**\n" +
                            $"伺服器Id: {guild.Id}\n" +
                            $"擁有者Id: {guild.OwnerId}\n" +
                            $"人數: {guild.MemberCount}\n";

                using (var db = _dbService.GetDbContext())
                {
                    var guildConfig = await db.GuildConfig.AsNoTracking().FirstOrDefaultAsync((x) => x.GuildId == gid);
                    if (guildConfig != null && guildConfig.LogMemberStatusChannelId != 0)
                    {
                        var channel = guild.GetChannel(guildConfig.LogMemberStatusChannelId);
                        if (channel != null)
                            result += $"伺服器會限記錄頻道: {channel.Name} ({channel.Id})\n";
                        else
                            result += $"伺服器會限記錄頻道: (不存在) {guildConfig.LogMemberStatusChannelId}\n";
                    }

                    var youtubeChannelSpiders = db.YoutubeChannelSpider.AsNoTracking().Where((x) => x.GuildId == gid);
                    if (youtubeChannelSpiders.Any())
                    {
                        bool isTooMany = youtubeChannelSpiders.Count() > 20;
                        if (isTooMany)
                        {
                            result += $"設定的 YouTube 爬蟲: \n```{string.Join('\n', youtubeChannelSpiders.Take(20).Select((x) => $"{x.ChannelTitle}: {x.ChannelId}"))}\n(還有 {youtubeChannelSpiders.Count() - 20} 個爬蟲...)```\n";
                        }
                        else
                        {
                            result += $"設定的 YouTube 爬蟲: \n```{string.Join('\n', youtubeChannelSpiders.Select((x) => $"{x.ChannelTitle}: {x.ChannelId}"))}```\n";
                        }
                    }

                    var youtubeChannelList = db.NoticeYoutubeStreamChannel.AsNoTracking().Where((x) => x.GuildId == guild.Id);
                    if (youtubeChannelList.Any())
                    {
                        List<string> channelListResult = new List<string>();

                        foreach (var item in youtubeChannelList)
                        {
                            var noticeChannel = guild.GetChannel(item.DiscordNoticeVideoChannelId);

                            if (noticeChannel != null)
                                channelListResult.Add($"{noticeChannel}: {item.YouTubeChannelId}");
                            else
                                channelListResult.Add($"(不存在) {item.DiscordNoticeVideoChannelId}: {item.YouTubeChannelId}");
                        }

                        bool isTooMany = channelListResult.Count > 20;
                        if (isTooMany)
                        {
                            result += $"設定 YouTube 通知的頻道: \n```{string.Join('\n', channelListResult.Take(20))}\n(還有 {channelListResult.Count - 20} 個爬蟲...)```\n";
                        }
                        else
                        {
                            result += $"設定 YouTube 通知的頻道: \n```{string.Join('\n', channelListResult)}```\n";
                        }
                    }

                    var memberChcekList = db.GuildYoutubeMemberConfig.AsNoTracking().Where((x) => x.GuildId == guild.Id);
                    if (memberChcekList.Any())
                    {
                        result += $"設定會限的頻道: \n```{string.Join('\n', memberChcekList.Select((x) => $"{x.MemberCheckChannelTitle}: {x.MemberCheckGrantRoleId}"))}```\n";
                    }

                    var twitchSpiders = db.TwitchSpider.AsNoTracking().Where((x) => x.GuildId == gid);
                    if (twitchSpiders.Any())
                    {
                        result += $"設定的 Twitch 爬蟲: \n```{string.Join('\n', twitchSpiders.Select((x) => $"{x.UserName}: {x.UserLogin}"))}```\n";
                    }

                    var noticeTwitchStreamChannels = db.NoticeTwitchStreamChannels.AsNoTracking().Where((x) => x.GuildId == guild.Id);
                    if (noticeTwitchStreamChannels.Any())
                    {
                        List<string> channelListResult = new List<string>();

                        foreach (var item in noticeTwitchStreamChannels)
                        {
                            var noticeChannel = guild.GetChannel(item.DiscordChannelId);

                            if (noticeChannel != null)
                                channelListResult.Add($"{noticeChannel}: {item.NoticeTwitchUserId}");
                            else
                                channelListResult.Add($"(不存在) {item.DiscordChannelId}: {item.NoticeTwitchUserId}");
                        }

                        result += $"設定 Twitch 通知的頻道: \n```{string.Join('\n', channelListResult)}```\n";
                    }

                    var twitterSpiders = db.TwitterSpaceSpider.AsNoTracking().Where((x) => x.GuildId == gid);
                    if (twitterSpiders.Any())
                    {
                        result += $"設定的 Twitter 爬蟲: \n```{string.Join('\n', twitterSpiders.Select((x) => $"{x.UserName}: {x.UserScreenName}"))}```\n";
                    }

                    var twitterChannelList = db.NoticeTwitterSpaceChannel.AsNoTracking().Where((x) => x.GuildId == guild.Id);
                    if (twitterChannelList.Any())
                    {
                        List<string> channelListResult = new List<string>();

                        foreach (var item in twitterChannelList)
                        {
                            var noticeChannel = guild.GetChannel(item.DiscordChannelId);

                            if (noticeChannel != null)
                                channelListResult.Add($"{noticeChannel}: {item.NoticeTwitterSpaceUserScreenName}");
                            else
                                channelListResult.Add($"(不存在) {item.DiscordChannelId}: {item.NoticeTwitterSpaceUserScreenName}");
                        }

                        result += $"設定 Twitter 通知的頻道: \n```{string.Join('\n', channelListResult)}```\n";
                    }

                    await Context.Channel.SendConfirmAsync(result).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
        }

        [RequireContext(ContextType.DM)]
        [Command("UserInfo")]
        [Summary("顯示使用者資訊")]
        [Alias("uinfo")]
        [RequireOwner]
        public async Task UserInfo(ulong uid = 0)
        {
            try
            {
                if (uid == 0)
                {
                    await Context.Channel.SendErrorAsync("UserId 不可為空").ConfigureAwait(false);
                    return;
                }

                var user = await _client.Rest.GetUserAsync(uid);
                if (user == null)
                {
                    await Context.Channel.SendErrorAsync("找不到指定的使用者").ConfigureAwait(false);
                    return;
                }

                string result = $"使用者名稱: **{user.Username}**\n" +
                            $"使用者 Id: {user.Id}\n";

                List<string> guildList = new();
                foreach (var item in _client.Guilds)
                {
                    if (item.GetUser(uid) != null)
                        guildList.Add($"{item.Name} ({item.Id})");
                }

                if (guildList.Any())
                {
                    result += $"共同的伺服器: \n```{string.Join('\n', guildList)}```";
                }

                await Context.Channel.SendConfirmAsync(result).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
        }

        [RequireContext(ContextType.DM)]
        [Command("AddOfficialList")]
        [Summary("新增官方伺服器白名單")]
        [Alias("aol")]
        [RequireOwner]
        public async Task AddOfficialListAsync(ulong guildId)
        {
            if (Utility.OfficialGuildList.Contains(guildId))
            {
                await Context.Channel.SendErrorAsync("此伺服器已存在於名單內");
                return;
            }

            Utility.OfficialGuildList.Add(guildId);

            if (_service.WriteAndReloadOfficialListFile())
            {
                await Context.Channel.SendConfirmAsync($"已添加 {guildId} 至官方伺服器名單內");
            }
            else
            {
                await Context.Channel.SendErrorAsync($"添加 {guildId} 至官方伺服器名單內失敗");
            }
        }

        [RequireContext(ContextType.DM)]
        [Command("RemoveOfficialList")]
        [Summary("移除官方伺服器白名單")]
        [Alias("rol")]
        [RequireOwner]
        public async Task RemoveOfficialListAsync(ulong guildId)
        {
            if (!Utility.OfficialGuildList.Contains(guildId))
            {
                await Context.Channel.SendErrorAsync("此伺服器不存在於名單內");
                return;
            }

            Utility.OfficialGuildList.Remove(guildId);

            if (_service.WriteAndReloadOfficialListFile())
            {
                await Context.Channel.SendConfirmAsync($"已從官方伺服器名單內移除 {guildId}");
            }
            else
            {
                await Context.Channel.SendErrorAsync($"從官方伺服器名單內移除 {guildId} 失敗");
            }
        }

        [RequireContext(ContextType.DM)]
        [Command("ListOfficialList")]
        [Summary("顯示官方伺服器白名單列表")]
        [Alias("lol")]
        [RequireOwner]
        public async Task ListOfficialListAsync(int page = 0)
        {
            if (Utility.OfficialGuildList.Count == 0)
            {
                await Context.Channel.SendErrorAsync("官方伺服器白名單為空");
                return;
            }

            List<string> officialList = new();
            foreach (var item in Utility.OfficialGuildList)
            {
                try
                {
                    var guild = _client.GetGuild(item);
                    if (guild == null)
                    {
                        officialList.Add($"*已離開的伺服器* `({item})`");
                        continue;
                    }

                    officialList.Add($"{guild.Name} `({item})`");
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Demystify(), $"取得伺服器資料失敗: {item}");
                }
            }

            if (page <= 0)
                page = 0;

            await Context.SendPaginatedConfirmAsync(page, (page) => (
                new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle("官方伺服器白名單清單")
                    .WithDescription(string.Join('\n', officialList.Skip(page * 20).Take(20)))),
                officialList.Count, 20);
        }

        [RequireContext(ContextType.DM)]
        [Command("ListNoNotifyGuild")]
        [Summary("顯示未設定通知的伺服器列表")]
        [Alias("lnng")]
        [RequireOwner]
        public async Task ListNoNotifyGuildAsync(int page = 0)
        {
            try
            {
                var guilds = _service.GetNoNotifyGuilds();

                File.WriteAllText(Utility.GetDataFilePath("NoNotifyGuildList.txt"), string.Join('\n', guilds.Select(g => $"{g.Name} | {g.Id} | {g.MemberCount} 人")));

                if (page <= 0)
                    page = 0;

                await Context.SendPaginatedConfirmAsync(page, (page) =>
                    new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("未設定通知的伺服器列表")
                        .WithDescription(string.Join('\n', guilds.Skip(page * 20).Take(20).Select(g => $"{g.Name} | {g.Id} | {g.MemberCount} 人"))),
                    guilds.Count, 20);
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "ListNoNotifyGuild Error");
                await Context.Channel.SendErrorAsync("取得未設定通知的伺服器列表失敗，請查看日誌").ConfigureAwait(false);
            }
        }


        [RequireContext(ContextType.DM)]
        [Command("LeaveNoNotifyGuild")]
        [Summary("離開未設定通知的伺服器")]
        [Alias("leavenng")]
        [RequireOwner]
        public async Task LeaveNoNotifyGuildAsync()
        {
            try
            {
                await Context.Channel.TriggerTypingAsync().ConfigureAwait(false);

                var guilds = _service.GetNoNotifyGuilds();

                if (guilds.Count == 0)
                {
                    await Context.Channel.SendErrorAsync("沒有未設定通知的伺服器");
                    return;
                }

                foreach (var item in guilds)
                {
                    await item.LeaveAsync().ConfigureAwait(false);
                    Log.Info($"已離開未設定通知的伺服器: {item.Name} ({item.Id})");
                }

                await Context.Channel.SendConfirmAsync($"已離開 {guilds.Count} 個未設定通知的伺服器").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "LeaveNoNotifyGuild Error");
                await Context.Channel.SendErrorAsync("取得未設定通知的伺服器列表失敗，請查看日誌").ConfigureAwait(false);
            }
        }
    }
}