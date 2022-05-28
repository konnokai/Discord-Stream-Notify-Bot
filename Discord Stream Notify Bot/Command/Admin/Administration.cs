using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Command.Admin
{
    public class Administration : TopLevelModule<AdministrationService>
    {
        private readonly DiscordSocketClient _client;
        public Administration(DiscordSocketClient discordSocketClient)
        {
            _client = discordSocketClient;
        }

        [RequireContext(ContextType.Guild)]
        [RequireOwner]
        [Command("Clear")]
        [Summary("清除機器人的發言")]
        public async Task Clear()
        {
            await _service.ClearUser((ITextChannel)Context.Channel);
        }

        [Command("UpdateStatus")]
        [Summary("更新機器人的狀態\n參數: Guild, Member, Stream, StreamCount, Info")]
        [Alias("UpStats")]
        [RequireOwner]
        public async Task UpdateStatusAsync([Summary("狀態")] string stats)
        {
            switch (stats.ToLowerInvariant())
            {
                case "guild":
                    Program.Status = Program.BotPlayingStatus.Guild;
                    break;
                case "member":
                    Program.Status = Program.BotPlayingStatus.Member;
                    break;
                case "stream":
                    Program.Status = Program.BotPlayingStatus.Stream;
                    break;
                case "streamcount":
                    Program.Status = Program.BotPlayingStatus.StreamCount;
                    break;
                case "info":
                    Program.Status = Program.BotPlayingStatus.Info;
                    break;
                default:
                    await Context.Channel.SendConfirmAsync(string.Format("找不到 {0} 狀態", stats));
                    return;
            }
            Program.ChangeStatus();
            return;
        }

        [Command("Say")]
        [Summary("說話")]
        [RequireOwner]
        public async Task SayAsync([Summary("內容")][Remainder] string text)
        {
            await Context.Channel.SendConfirmAsync(text);
        }

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
                    bool isBotOwnerInGuild = item.GetUser(Program.ApplicatonOwner.Id) != null;

                    embedBuilder.AddField(item.Name, "Id: " + item.Id +
                        "\nOwner Id: " + item.OwnerId +
                        "\n人數: " + totalMember.ToString() +
                        "\nBot擁有者是否在該伺服器: " + (isBotOwnerInGuild ? "是" : "否"));
                }

                return embedBuilder;
            }, _client.Guilds.Count, 5);
        }

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
                    bool isBotOwnerInGuild = guild.GetUser(Program.ApplicatonOwner.Id) != null;

                    var embed = new EmbedBuilder().WithOkColor().AddField(guild.Name, "Id: " + guild.Id +
                        "\nOwner Id: " + guild.OwnerId +
                        "\n人數: " + guild.MemberCount +
                        "\nBot擁有者是否在該伺服器: " + (isBotOwnerInGuild ? "是" : "否")).Build();
                    await Context.Channel.SendMessageAsync(embed: embed);
                    return;
                }
            }

            var list = _client.Guilds.Where((x) => x.Name.Contains(keyword));
            if (list.Count() == 0)
            {
                await Context.Channel.SendErrorAsync("該關鍵字無伺服器");
                return;
            }

            await Context.SendPaginatedConfirmAsync(page, (cur) =>
            {
                EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor().WithTitle("目前所在的伺服器有");

                foreach (var item in list.Skip(cur * 5).Take(5))
                {
                    bool isBotOwnerInGuild = item.GetUser(Program.ApplicatonOwner.Id) != null;

                    embedBuilder.AddField(item.Name, "Id: " + item.Id +
                        "\nOwner Id: " + item.OwnerId +
                        "\n人數: " + item.MemberCount.ToString() +
                        "\nBot擁有者是否在該伺服器: " + (isBotOwnerInGuild ? "是" : "否"));
                }

                return embedBuilder;
            }, list.Count(), 5);
        }

        [Command("Die")]
        [Summary("關閉機器人")]
        [Alias("Bye")]
        [RequireOwner]
        public async Task DieAsync()
        {
            Program.isDisconnect = true;
            Program.isHoloChannelSpider = false;
            Program.isNijisanjiChannelSpider = false;
            Program.isOtherChannelSpider = false;
            await Context.Channel.SendConfirmAsync("關閉中");
        }

        [Command("Leave")]
        [Summary("讓機器人離開指定的伺服器")]
        [RequireOwner]
        public async Task LeaveAsync([Summary("伺服器Id")] ulong gid = 0)
        {
            if (gid == 0) { await Context.Channel.SendConfirmAsync("伺服器Id為空"); return; }

            try { await _client.GetGuild(gid).LeaveAsync(); }
            catch (Exception) { await Context.Channel.SendConfirmAsync("失敗，請確認Id是否正確"); return; }

            await Context.Channel.SendConfirmAsync("✅");
        }

        [Command("GetInviteURL")]
        [Summary("取得伺服器的邀請連結")]
        [Alias("invite")]
        [RequireBotPermission(GuildPermission.CreateInstantInvite)]
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
                    IReadOnlyCollection<SocketTextChannel> socketTextChannels = guild.TextChannels;

                    await Context.SendPaginatedConfirmAsync(0, (cur) =>
                    {
                        EmbedBuilder embedBuilder = new EmbedBuilder()
                           .WithOkColor()
                           .WithTitle("以下為 " + guild.Name + " 所有的文字頻道")
                           .WithDescription(string.Join('\n', socketTextChannels.Skip(cur * 10).Take(10).Select((x) => x.Id + " / " + x.Name)));

                        return embedBuilder;
                    }, socketTextChannels.Count, 10);
                }
                else
                {
                    IInviteMetadata invite = await guild.GetTextChannel(cid).CreateInviteAsync(300, 1, false);
                    await Context.Channel.SendConfirmAsync(invite.Url);
                }
            }
            catch (Exception ex) { Log.FormatColorWrite(ex.Message + "\n" + ex.StackTrace, ConsoleColor.Red); }
        }

        [Command("SendMsgToAllGuild")]
        [Summary("傳送訊息到所有伺服器")]
        [Alias("GuildMsg")]
        [RequireOwner]
        public async Task SendMsgToAllGuild(string imageUrl = "", [Remainder] string message = "")
        {
            if (message == "")
            {
                await Context.Channel.SendConfirmAsync("訊息為空");
                return;
            }

            if (await PromptUserConfirmAsync(new EmbedBuilder().WithOkColor().WithDescription(message).WithImageUrl(imageUrl)))
            {
                EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor()
                    .WithUrl("https://konnokai.me/")
                    .WithTitle("來自開發者消息")
                    .WithAuthor(Context.Message.Author)
                    .WithDescription(message)
                    .WithImageUrl(imageUrl)
                    .WithFooter("若看到此消息出現在非通知頻道上，請通知管理員重新設定直播通知");

                using (var db = DataBase.DBContext.GetDbContext())
                {
                    try
                    {
                        int i = 1, num = _client.Guilds.Count;
                        var list = db.NoticeYoutubeStreamChannel.Distinct((x) => x.GuildId).Select((x) => new KeyValuePair<ulong, ulong>(x.GuildId, x.DiscordChannelId));
                        foreach (var item in _client.Guilds)
                        {
                            try
                            {
                                SocketTextChannel channel;
                                if (list.Any((x) => x.Key == item.Id))
                                {
                                    var noticeItem = list.FirstOrDefault((x) => x.Key == item.Id);
                                    channel = item.GetTextChannel(noticeItem.Value);
                                }
                                else
                                {
                                    channel = item.TextChannels.FirstOrDefault((x) => item.GetUser(_client.CurrentUser.Id).GetPermissions(x).SendMessages);
                                }

                                if (channel != null)
                                    await channel.SendMessageAsync(embed: embedBuilder.Build());
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"MSG: {item.Name}({item.Id})");
                                Log.Error(ex.Message);

                                try
                                {
                                    db.NoticeYoutubeStreamChannel.RemoveRange(Queryable.Where(db.NoticeYoutubeStreamChannel, (x) => x.GuildId == item.Id));
                                    db.SaveChanges();
                                }
                                catch { }
                            }
                            finally
                            {
                                Log.Info($"({i++}/{num}) {item.Name}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"{ex.Message}\n{ex.StackTrace}");
                    }

                    await Context.Channel.SendConfirmAsync("已發送完成");
                }
            }
        }

        [Command("GuildInfo")]
        [Summary("顯示伺服器資訊")]
        [Alias("ginfo")]
        [RequireOwner]
        public async Task GuildInfo(ulong gid = 0)
        {
            try
            {
                if (gid == 0) gid = Context.Guild.Id;
                var guild = _client.GetGuild(gid);

                if (guild == null)
                {
                    await Context.Channel.SendErrorAsync("找不到指定的伺服器").ConfigureAwait(false);
                    return;
                }

                using (var db = DataBase.DBContext.GetDbContext())
                {
                    var channelList = db.NoticeYoutubeStreamChannel.ToList().Where((x) => x.GuildId == guild.Id).Select((x) => $"<#{x.DiscordChannelId}>: {x.NoticeStreamChannelId}");

                    await Context.Channel.SendConfirmAsync($"伺服器名稱: {guild.Name}\n" +
                            $"伺服器Id: {guild.Id}\n" +
                            $"擁有者: {guild.Owner.Username} ({guild.Owner.Id})" +
                            $"設定通知的頻道: \n{string.Join('\n', channelList)}").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message + "\n" + ex.StackTrace);
            }
        }
    }
}