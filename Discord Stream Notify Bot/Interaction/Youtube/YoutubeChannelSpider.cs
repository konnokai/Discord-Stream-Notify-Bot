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
        [CommandSummary("新增非兩大箱的頻道檢測爬蟲\r\n" +
           "頻道Id必須為24字數+UC開頭\r\n" +
           "或是完整的Youtube頻道網址\r\n" +
           //"每個伺服器可新增最多五個頻道爬蟲\r\n" +
           "伺服器需大於300人才可使用\r\n" +
           "未來會根據情況增減可新增的頻道數量\r\n" +
           "如有任何需要請向擁有者詢問")]
        [CommandExample("UC0qt9BfrpQo-drjuPKl_vdA")]
        [SlashCommand("add-youtube-spider", "新增非兩大箱的頻道檢測爬蟲")]
        public async Task AddChannelSpider([Summary("頻道Id")] string channelId)
        {
            channelId = channelId.Trim();
            if (string.IsNullOrEmpty(channelId))
            {
                await Context.Interaction.SendErrorAsync("未輸入頻道Id").ConfigureAwait(false);
                return;
            }
            if (!channelId.Contains("UC"))
            {
                await Context.Interaction.SendErrorAsync("頻道Id錯誤").ConfigureAwait(false);
                return;
            }
            try
            {
                channelId = channelId.Substring(channelId.IndexOf("UC"), 24);
            }
            catch
            {
                await Context.Interaction.SendErrorAsync("頻道Id格式錯誤").ConfigureAwait(false);
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if ((db.HoloStreamVideo.Any((x) => x.ChannelId == channelId) || db.NijisanjiStreamVideo.Any((x) => x.ChannelId == channelId)) && !db.YoutubeChannelOwnedType.Any((x) => x.ChannelId == channelId))
                {
                    await Context.Interaction.SendErrorAsync($"不可新增兩大箱的頻道").ConfigureAwait(false);
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

                    await Context.Interaction.SendConfirmAsync($"{channelId} 已在爬蟲清單內\r\n" +
                        $"可直接到通知頻道內使用 `/youtube add-youtube-notice {channelId}` 開啟通知\r\n" +
                        $"(由 `{guild}` 設定)").ConfigureAwait(false);
                    return;
                }

                //if (db.ChannelSpider.Count((x) => x.GuildId == Context.Guild.Id) >= 5)
                //{
                //    await Context.Interaction.SendConfirmAsync($"此伺服器已設定五個檢測頻道，請移除後再試\r\n" +
                //        $"如有特殊需求請向Bot擁有者詢問").ConfigureAwait(false);
                //    return;
                //}

                string channelTitle = await GetChannelTitle(channelId).ConfigureAwait(false);
                if (channelTitle == "")
                {
                    await Context.Interaction.SendErrorAsync($"頻道 {channelId} 不存在").ConfigureAwait(false);
                    return;
                }

                db.YoutubeChannelSpider.Add(new DataBase.Table.YoutubeChannelSpider() { GuildId = Context.Interaction.User.Id == Program.ApplicatonOwner.Id ? 0 : Context.Guild.Id, ChannelId = channelId, ChannelTitle = channelTitle });
                await db.SaveChangesAsync();

                await Context.Interaction.SendConfirmAsync($"已將 {channelTitle} 加入到爬蟲清單內\r\n" +
                    $"請到通知頻道內使用 `/youtube add-youtube-notice {channelId}` 來開啟通知").ConfigureAwait(false);
                _discordWebhookClient.SendMessageToDiscord($"{Context.Guild.Name} 已新增檢測頻道 {Format.Url(channelTitle, $"https://www.youtube.com/channel/{channelId}")}");
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator)]
        [CommandSummary("移除非兩大箱的頻道檢測爬蟲\r\n" +
            "爬蟲必須由本伺服器新增才可移除\r\n" +
            "頻道Id必須為24字數+UC開頭\r\n" +
            "或是完整的Youtube頻道網址")]
        [CommandExample("UC0qt9BfrpQo-drjuPKl_vdA")]
        [SlashCommand("remove-youtube-spider", "移除非兩大箱的頻道檢測爬蟲")]
        public async Task RemoveChannelSpider([Summary("頻道Id")] string channelId)
        {
            channelId = channelId.Trim();
            if (string.IsNullOrEmpty(channelId))
            {
                await Context.Interaction.SendErrorAsync("未輸入頻道Id").ConfigureAwait(false);
                return;
            }
            if (!channelId.Contains("UC"))
            {
                await Context.Interaction.SendErrorAsync("頻道Id錯誤").ConfigureAwait(false);
                return;
            }
            try
            {
                channelId = channelId.Substring(channelId.IndexOf("UC"), 24);
            }
            catch
            {
                await Context.Interaction.SendErrorAsync("頻道Id格式錯誤").ConfigureAwait(false);
                return;
            }

            using (var db = DataBase.DBContext.GetDbContext())
            {
                if (!db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId))
                {
                    await Context.Interaction.SendErrorAsync($"並未設定 {channelId} 頻道檢測爬蟲...").ConfigureAwait(false);
                    return;
                }

                if (Context.Interaction.User.Id != Program.ApplicatonOwner.Id && !db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId && x.GuildId == Context.Guild.Id))
                {
                    await Context.Interaction.SendErrorAsync($"該頻道爬蟲並非本伺服器新增，無法移除").ConfigureAwait(false);
                    return;
                }

                db.YoutubeChannelSpider.Remove(db.YoutubeChannelSpider.First((x) => x.ChannelId == channelId));
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            await Context.Interaction.SendConfirmAsync($"已移除 {channelId}").ConfigureAwait(false);
            _discordWebhookClient.SendMessageToDiscord($"{Context.Guild.Name} 已移除檢測頻道 <https://www.youtube.com/channel/{channelId}>");
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