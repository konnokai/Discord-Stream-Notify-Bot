﻿using Discord;
using Discord.Commands;
using System;
using System.Linq;
using System.Threading.Tasks;
using Discord_Stream_Notify_Bot.Command.Attribute;

namespace Discord_Stream_Notify_Bot.Command.Youtube
{
    public partial class YoutubeStream : TopLevelModule, ICommandService
    {
        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("ListDeathChannelSpider")]
        [Summary("顯示已死去的爬蟲檢測頻道")]
        [Alias("ldcs")]
        public async Task ListDeathChannelSpider(int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var list = db.YoutubeChannelSpider.ToList().Where((x) => x.GuildId != 0 && _client.GetGuild(x.GuildId) == null).Select((x) => Format.Url(x.ChannelTitle, $"https://www.youtube.com/channel/{x.ChannelId}"));

                await Context.SendPaginatedConfirmAsync(page, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("死去的直播爬蟲清單")
                        .WithDescription(string.Join('\n', list.Skip(page * 10).Take(10)))
                        .WithFooter($"{Math.Min(list.Count(), (page + 1) * 10)} / {list.Count()}個頻道");
                }, list.Count(), 10, false).ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.DM)]
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

        [RequireContext(ContextType.DM)]
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
