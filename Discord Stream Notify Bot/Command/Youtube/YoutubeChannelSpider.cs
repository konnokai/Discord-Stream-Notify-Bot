using Discord;
using Discord.Commands;
using System;
using System.Linq;
using System.Threading.Tasks;
using Discord_Stream_Notify_Bot.Command.Attribute;

namespace Discord_Stream_Notify_Bot.Command.Youtube
{
    public partial class YoutubeStream : TopLevelModule, ICommandService
    {
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator,Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [RequireGuildMemberCount(300)]
        [Command("AddChannelSpider")]
        [Summary("新增非兩大箱的頻道檢測爬蟲\n" +
           "**禁止新增非VTuber的頻道**\n" +
            "伺服器需大於300人才可使用\n" +
            "如有任何需要請向擁有者詢問")]
        [CommandExample("https://www.youtube.com/channel/UC0qt9BfrpQo-drjuPKl_vdA",
            "https://www.youtube.com/c/かぐらななななかぐ辛党Ch")]
        [Alias("ACS")]
        public async Task AddChannelSpider([Summary("頻道網址")] string channelUrl = "")
        {
            string channelId = "";
            try
            {
                channelId = await _service.GetChannelIdAsync(channelUrl).ConfigureAwait(false);
            }
            catch (FormatException fex)
            {
                await Context.Channel.SendErrorAsync(fex.Message);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Channel.SendErrorAsync("網址不可空白");
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if ((db.HoloStreamVideo.Any((x) => x.ChannelId == channelId) || db.NijisanjiStreamVideo.Any((x) => x.ChannelId == channelId)) && !db.YoutubeChannelOwnedType.Any((x) => x.ChannelId == channelId))
                {
                    await Context.Channel.SendConfirmAsync($"不可新增兩大箱的頻道").ConfigureAwait(false);
                    return;
                }

                if (db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId))
                {
                    var item = db.YoutubeChannelSpider.FirstOrDefault((x) => x.ChannelId == channelId);
                    string guild = "";
                    try
                    {
                        guild = item.GuildId == 0 ? "Bot擁有者" : $"{_client.GetGuild(item.GuildId).Name}";
                    }
                    catch (Exception)
                    {
                        guild = "已退出的伺服器";
                    }

                    await Context.Channel.SendConfirmAsync($"{channelId} 已在爬蟲清單內\n" +
                        $"可直接到通知頻道內使用 `s!ansc {channelId}` 開啟通知\n" +
                        $"(由 `{guild}` 設定)").ConfigureAwait(false);
                    return;
                }

                string channelTitle = await GetChannelTitle(channelId).ConfigureAwait(false);
                if (channelTitle == "")
                {
                    await Context.Channel.SendConfirmAsync($"頻道 {channelId} 不存在").ConfigureAwait(false);
                    return;
                }

                var spider = new DataBase.Table.YoutubeChannelSpider() { GuildId = Context.Guild.Id, ChannelId = channelId, ChannelTitle = channelTitle };
                if (Context.User.Id == Program.ApplicatonOwner.Id && !await PromptUserConfirmAsync(new EmbedBuilder().WithOkColor().WithDescription("設定該爬蟲為本伺服器使用?")))                
                    spider.GuildId = 0;

                db.YoutubeChannelSpider.Add(spider);
                db.SaveChanges();

                await Context.Channel.SendConfirmAsync($"已將 {channelTitle} 加入到爬蟲清單內\n" +
                    $"請到通知頻道內使用 `s!ansc https://www.youtube.com/channel/{channelId}` 來開啟通知").ConfigureAwait(false);

                try
                {
                    await (await Program.ApplicatonOwner.CreateDMChannelAsync()).SendMessageAsync(embed: new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("已新增檢測頻道")
                        .AddField("頻道", Format.Url(channelTitle, $"https://www.youtube.com/channel/{channelId}"), false)
                        .AddField("伺服器", $"{Context.Guild.Name} ({Context.Guild.Id})", false)
                        .AddField("執行者", $"{Context.User.Username} ({Context.User.Id})", false).Build());
                }
                catch (Exception ex) { Log.Error(ex.ToString()); }
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [Command("RemoveChannelSpider")]
        [Summary("移除非兩大箱的頻道檢測爬蟲\n" +
            "爬蟲必須由本伺服器新增才可移除\n")]
        [CommandExample("https://www.youtube.com/channel/UC0qt9BfrpQo-drjuPKl_vdA",
            "https://www.youtube.com/c/かぐらななななかぐ辛党Ch")]
        [Alias("RCS")]
        public async Task RemoveChannelSpider([Summary("頻道網址")] string channelUrl = "")
        {
            string channelId = "";
            try
            {
                channelId = await _service.GetChannelIdAsync(channelUrl).ConfigureAwait(false);
            }
            catch (FormatException fex)
            {
                await Context.Channel.SendErrorAsync(fex.Message);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Channel.SendErrorAsync("網址不可空白");
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (!db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId))
                {
                    await Context.Channel.SendConfirmAsync($"並未設定 {channelId} 頻道檢測爬蟲...").ConfigureAwait(false);
                    return;
                }

                if (Context.Message.Author.Id != Program.ApplicatonOwner.Id && !db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId && x.GuildId == Context.Guild.Id))
                {
                    await Context.Channel.SendConfirmAsync($"該頻道爬蟲並非本伺服器新增，無法移除").ConfigureAwait(false);
                    return;
                }

                db.YoutubeChannelSpider.Remove(db.YoutubeChannelSpider.First((x) => x.ChannelId == channelId));
                db.SaveChanges();
            }
            await Context.Channel.SendConfirmAsync($"已移除 {channelId}").ConfigureAwait(false);

            try
            {
                await (await Program.ApplicatonOwner.CreateDMChannelAsync()).SendMessageAsync(embed: new EmbedBuilder()
                    .WithErrorColor()
                    .WithTitle("已移除檢測頻道")
                    .AddField("頻道", $"https://www.youtube.com/channel/{channelId}", false)
                    .AddField("伺服器", $"{Context.Guild.Name} ({Context.Guild.Id})", false)
                    .AddField("執行者", $"{Context.User.Username} ({Context.User.Id})", false).Build());
            }
            catch (Exception ex) { Log.Error(ex.ToString()); }
        }

        [RequireContext(ContextType.Guild)]
        [Command("ListChannelSpider")]
        [Summary("顯示已加入爬蟲檢測的頻道")]
        [Alias("lcs")]
        public async Task ListChannelSpider(int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var list = db.YoutubeChannelSpider.ToList().Where((x) => !x.IsWarningChannel).Select((x) => Format.Url(x.ChannelTitle, $"https://www.youtube.com/channel/{x.ChannelId}") +
                    $" 由 `" + (x.GuildId == 0 ? "Bot擁有者" : (_client.GetGuild(x.GuildId) != null ? _client.GetGuild(x.GuildId).Name : "已退出的伺服器")) + "` 新增");
                int warningChannelNum = db.YoutubeChannelSpider.Count((x) => x.IsWarningChannel);

                await Context.SendPaginatedConfirmAsync(page, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("直播爬蟲清單")
                        .WithDescription(string.Join('\n', list.Skip(page * 10).Take(10)))
                        .WithFooter($"{Math.Min(list.Count(), (page + 1) * 10)} / {list.Count()}個頻道 ({warningChannelNum}個隱藏的警告爬蟲)");
                }, list.Count(), 10, false).ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireOwner]
        [Command("ListWarningChannelSpider")]
        [Summary("顯示已加入爬蟲檢測的\"警告\"頻道")]
        [Alias("lwcs")]
        public async Task ListWarningChannelSpider()
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var list = db.YoutubeChannelSpider.ToList().Where((x) => x.IsWarningChannel).Select((x) => Format.Url(x.ChannelTitle, $"https://www.youtube.com/channel/{x.ChannelId}") +
                    $" 由 `" + (x.GuildId == 0 ? "Bot擁有者" : $"{_client.GetGuild(x.GuildId).Name}") + "` 新增");

                await Context.SendPaginatedConfirmAsync(0, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("警告的直播爬蟲清單")
                        .WithDescription(string.Join('\n', list.Skip(page * 10).Take(10)))
                        .WithFooter($"{Math.Min(list.Count(), (page + 1) * 10)} / {list.Count()}個頻道");
                }, list.Count(), 10, false).ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireOwner]
        [Command("ToggleWarningChannel")]
        [Summary("切換警告頻道狀態")]
        [CommandExample("https://www.youtube.com/channel/UC0qt9BfrpQo-drjuPKl_vdA")]
        [Alias("twc")]
        public async Task ToggleWarningChannel([Summary("頻道網址")] string channelUrl = "")
        {
            string channelId = "";
            try
            {
                channelId = await _service.GetChannelIdAsync(channelUrl).ConfigureAwait(false);
            }
            catch (FormatException fex)
            {
                await Context.Channel.SendErrorAsync(fex.Message);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Channel.SendErrorAsync("網址不可空白");
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId))
                {
                    var channel = db.YoutubeChannelSpider.First((x) => x.ChannelId == channelId);
                    channel.IsWarningChannel = !channel.IsWarningChannel;
                    db.YoutubeChannelSpider.Update(channel);
                    db.SaveChanges();

                    await Context.Channel.SendConfirmAsync($"已設定 {channel.ChannelTitle} 為 " + (channel.IsWarningChannel ? "警告" : "普通") + " 狀態").ConfigureAwait(false);
                }
            }       
        }
    }
}
