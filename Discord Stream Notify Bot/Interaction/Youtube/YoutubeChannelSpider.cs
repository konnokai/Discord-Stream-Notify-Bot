using Discord;
using System;
using System.Linq;
using System.Threading.Tasks;
using Discord_Stream_Notify_Bot.Interaction.Attribute;
using Discord.Interactions;

namespace Discord_Stream_Notify_Bot.Interaction.Youtube
{
    [Group("youtube", "YT")]
    public partial class YoutubeStream : TopLevelModule<SharedService.Youtube.YoutubeStreamService>
    {
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator)]
        [RequireGuildMemberCount(300)]
        [CommandSummary("新增非兩大箱的頻道檢測爬蟲\n" +
           "**禁止新增非VTuber的頻道**\n" +
           "伺服器需大於300人才可使用\n" +
           "未來會根據情況增減可新增的頻道數量\n" +
           "如有任何需要請向擁有者詢問")]
        [CommandExample("https://www.youtube.com/channel/UC0qt9BfrpQo-drjuPKl_vdA",
            "https://www.youtube.com/c/かぐらななななかぐ辛党Ch")]
        [SlashCommand("add-youtube-spider", "新增非兩大箱的頻道檢測爬蟲")]
        public async Task AddChannelSpider([Summary("頻道網址")] string channelUrl)
        {
            await DeferAsync().ConfigureAwait(false);

            string channelId = "";
            try
            {
                channelId = await _service.GetChannelId(channelUrl).ConfigureAwait(false);
            }
            catch (FormatException fex)
            {
                await Context.Interaction.SendErrorAsync(fex.Message, true);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Interaction.SendErrorAsync("網址不可空白", true);
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if ((db.HoloStreamVideo.Any((x) => x.ChannelId == channelId) || db.NijisanjiStreamVideo.Any((x) => x.ChannelId == channelId)) && !db.YoutubeChannelOwnedType.Any((x) => x.ChannelId == channelId))
                {
                    await Context.Interaction.SendErrorAsync($"不可新增兩大箱的頻道", true).ConfigureAwait(false);
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

                    await Context.Interaction.SendConfirmAsync($"{channelId} 已在爬蟲清單內\n" +
                        $"可直接到通知頻道內使用 `/youtube add-youtube-notice {channelId}` 開啟通知\n" +
                        $"(由 `{guild}` 設定)", true).ConfigureAwait(false);
                    return;
                }

                string channelTitle = await GetChannelTitle(channelId).ConfigureAwait(false);
                if (channelTitle == "")
                {
                    await Context.Interaction.SendErrorAsync($"頻道 {channelId} 不存在", true).ConfigureAwait(false);
                    return;
                }

                db.YoutubeChannelSpider.Add(new DataBase.Table.YoutubeChannelSpider() { GuildId = Context.Interaction.User.Id == Program.ApplicatonOwner.Id ? 0 : Context.Guild.Id, ChannelId = channelId, ChannelTitle = channelTitle });
                await db.SaveChangesAsync();

                await Context.Interaction.SendConfirmAsync($"已將 {channelTitle} 加入到爬蟲清單內\n" +
                    $"請到通知頻道內使用 `/youtube add-youtube-notice https://www.youtube.com/channel/{channelId}` 來開啟通知", true).ConfigureAwait(false);

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
        [RequireUserPermission(GuildPermission.Administrator)]
        [CommandSummary("移除非兩大箱的頻道檢測爬蟲\n" +
            "爬蟲必須由本伺服器新增才可移除")]
        [CommandExample("https://www.youtube.com/channel/UC0qt9BfrpQo-drjuPKl_vdA",
            "https://www.youtube.com/c/かぐらななななかぐ辛党Ch")]
        [SlashCommand("remove-youtube-spider", "移除非兩大箱的頻道檢測爬蟲")]
        public async Task RemoveChannelSpider([Summary("頻道網址")] string channelUrl)
        {
            await DeferAsync().ConfigureAwait(false);

            string channelId = "";
            try
            {
                channelId = await _service.GetChannelId(channelUrl).ConfigureAwait(false);
            }
            catch (FormatException fex)
            {
                await Context.Interaction.SendErrorAsync(fex.Message, true);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Interaction.SendErrorAsync("網址不可空白", true);
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (!db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId))
                {
                    await Context.Interaction.SendErrorAsync($"並未設定 {channelId} 頻道檢測爬蟲...", true).ConfigureAwait(false);
                    return;
                }

                if (Context.Interaction.User.Id != Program.ApplicatonOwner.Id && !db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId && x.GuildId == Context.Guild.Id))
                {
                    await Context.Interaction.SendErrorAsync($"該頻道爬蟲並非本伺服器新增，無法移除", true).ConfigureAwait(false);
                    return;
                }

                db.YoutubeChannelSpider.Remove(db.YoutubeChannelSpider.First((x) => x.ChannelId == channelId));
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            await Context.Interaction.SendConfirmAsync($"已移除 {channelId}", true).ConfigureAwait(false);

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
        [SlashCommand("list-youtube-spider", "顯示已加入爬蟲檢測的頻道")]
        public async Task ListChannelSpider([Summary("頁數")] int page = 0)
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
    }
}