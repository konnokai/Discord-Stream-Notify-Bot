using Discord.Commands;
using Discord_Stream_Notify_Bot.Command.Attribute;
using Discord_Stream_Notify_Bot.DataBase.Table;

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

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                try
                {
                    await Context.Channel.TriggerTypingAsync();

                    var list = new List<string>();
                    var checkedGuildHashSet = new HashSet<ulong>();
                    foreach (var item in db.YoutubeChannelSpider.Where((x) => x.GuildId != 0))
                    {
                        if (checkedGuildHashSet.Contains(item.GuildId))
                            continue;

                        try
                        {
                            var guild = _client.GetGuild(item.GuildId);
                            if (guild == null)
                                list.AddRange(db.YoutubeChannelSpider.Where((x) => x.GuildId == item.GuildId).Select((x) => Format.Url(x.ChannelTitle, $"https://www.youtube.com/channel/{x.ChannelId}")));
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, $"{item.ChannelTitle}: {item.GuildId}");
                        }
                        finally
                        {
                            checkedGuildHashSet.Add(item.GuildId);
                        }
                    }

                    if (!list.Any())
                    {
                        await Context.Channel.SendConfirmAsync("無已死去的爬蟲...");
                        return;
                    }

                    await Context.SendPaginatedConfirmAsync(page, page =>
                    {
                        return new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("死去的直播爬蟲清單")
                            .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20)))
                            .WithFooter($"{Math.Min(list.Count, (page + 1) * 20)} / {list.Count}個頻道");
                    }, list.Count, 20, false).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ListDeathChannelSpider");
                    await Context.Channel.SendErrorAsync(ex.ToString());
                    return;
                }
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("ListNoNoticeChannelSpider")]
        [Summary("顯示未設定通知的爬蟲頻道")]
        [Alias("lnncs")]
        public async Task ListNoNoticeChannelSpider(int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                try
                {
                    await Context.Channel.TriggerTypingAsync();

                    var list = new List<YoutubeChannelSpider>();
                    foreach (var item in db.YoutubeChannelSpider.Where((x) => x.GuildId != 0))
                    {
                        try
                        {
                            if (!db.NoticeYoutubeStreamChannel.Any((x) => x.NoticeStreamChannelId == item.ChannelId))
                                list.Add(item);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, $"{item.ChannelTitle}: {item.GuildId}");
                        }
                    }

                    if (!list.Any())
                    {
                        await Context.Channel.SendConfirmAsync("無未設定通知的爬蟲...");
                        return;
                    }

                    await Context.SendPaginatedConfirmAsync(page, page =>
                    {
                        return new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("無未設定通知的爬蟲")
                            .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20).Select((x) => Format.Url(x.ChannelTitle, $"https://www.youtube.com/channel/{x.ChannelId}"))))
                            .WithFooter($"{Math.Min(list.Count, (page + 1) * 20)} / {list.Count}個頻道");
                    }, list.Count, 20, false).ConfigureAwait(false);

                    if (await PromptUserConfirmAsync(new EmbedBuilder().WithOkColor().WithDescription("是否要一次移除全部清單內的爬蟲?")))
                    {
                        try
                        {
                            db.YoutubeChannelSpider.RemoveRange(list);
                            db.SaveChanges();
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "儲存資料庫失敗");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ListNoNoticeChannelSpider");
                    await Context.Channel.SendErrorAsync(ex.ToString());
                    return;
                }
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("AddSpiderToGuild")]
        [Summary("新增爬蟲並指定伺服器")]
        [Alias("astg")]
        public async Task AddSpiderToGuild(string channelId, ulong guildId)
        {
            channelId = await _service.GetChannelIdAsync(channelId);
            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var youtubeChannelSpider = db.YoutubeChannelSpider.SingleOrDefault((x) => x.ChannelId == channelId);
                if (youtubeChannelSpider != null)
                {
                    await Context.Channel.SendErrorAsync($"`{channelId}` 已被 `{youtubeChannelSpider.GuildId}` 設定");
                    return;
                }

                string channelTitle = await _service.GetChannelTitle(channelId).ConfigureAwait(false);
                if (channelTitle == "")
                {
                    await Context.Channel.SendErrorAsync($"頻道 `{channelId}` 不存在").ConfigureAwait(false);
                    return;
                }

                db.YoutubeChannelSpider.Add(new DataBase.Table.YoutubeChannelSpider() { ChannelId = channelId, GuildId = guildId, ChannelTitle = channelTitle, IsTrustedChannel = true });
                db.SaveChanges();

                await Context.Channel.SendConfirmAsync($"已將 `{channelTitle}` 設定至 `{guildId}`，等待爬蟲註冊...");
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("ListNotTrustedChannelSpider")]
        [Summary("顯示已加入爬蟲檢測的\"未認可\"頻道\n" +
            "注意: 本清單可能含有中之人或前世頻道")]
        [Alias("lnvcs")]
        public async Task ListNotVTuberChannelSpider()
        {
            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var list = db.YoutubeChannelSpider.Where((x) => !x.IsTrustedChannel).Select((x) => Format.Url(x.ChannelTitle, $"https://www.youtube.com/channel/{x.ChannelId}") +
                    $" 由 `" + (x.GuildId == 0 ? "Bot擁有者" : $"{_client.GetGuild(x.GuildId).Name}") + "` 新增");

                await Context.SendPaginatedConfirmAsync(0, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("未認可的爬蟲清單")
                        .WithDescription(string.Join('\n', list.Skip(page * 10).Take(10)))
                        .WithFooter($"{Math.Min(list.Count(), (page + 1) * 10)} / {list.Count()}個頻道");
                }, list.Count(), 10, false).ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("ToggleIsTrustedChannel")]
        [Summary("切換頻道是否為認可頻道")]
        [CommandExample("https://www.youtube.com/channel/UC0qt9BfrpQo-drjuPKl_vdA")]
        [Alias("ttc")]
        public async Task ToggleIsTrustedChannel([Summary("頻道網址")] string channelUrl = "")
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

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                if (db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId))
                {
                    var channel = db.YoutubeChannelSpider.First((x) => x.ChannelId == channelId);
                    channel.IsTrustedChannel = !channel.IsTrustedChannel;
                    db.YoutubeChannelSpider.Update(channel);
                    db.SaveChanges();

                    await Context.Channel.SendConfirmAsync($"已設定 `{channel.ChannelTitle}` 為" + (channel.IsTrustedChannel ? "已" : "未") + "認可頻道").ConfigureAwait(false);
                }
                else
                {
                    await Context.Channel.SendErrorAsync($"尚未設定 ` {channelId} ` 的爬蟲").ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("SetChannelSpiderGuildId")]
        [Summary("設定爬蟲頻道的伺服器 Id")]
        [CommandExample("https://www.youtube.com/channel/UC0qt9BfrpQo-drjuPKl_vdA 0")]
        [Alias("scsg")]
        public async Task SetChannelSpiderGuildId([Summary("頻道網址")] string channelUrl = "", ulong guildId = 0)
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

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                if (db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId))
                {
                    var channel = db.YoutubeChannelSpider.First((x) => x.ChannelId == channelId);
                    channel.GuildId = guildId;
                    db.YoutubeChannelSpider.Update(channel);
                    db.SaveChanges();

                    await Context.Channel.SendConfirmAsync($"已設定 `{channel.ChannelTitle}` 的 GuildId 為 `{guildId}`").ConfigureAwait(false);
                }
                else
                {
                    await Context.Channel.SendErrorAsync($"尚未設定 `{channelId}` 的爬蟲").ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("RemoveChannelSpider")]
        [Summary("移除頻道爬蟲")]
        [CommandExample("https://www.youtube.com/channel/UC0qt9BfrpQo-drjuPKl_vdA")]
        [Alias("rcs")]
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

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                if (db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId))
                {
                    var channel = db.YoutubeChannelSpider.First((x) => x.ChannelId == channelId);
                    db.YoutubeChannelSpider.Remove(channel);
                    db.SaveChanges();

                    await Context.Channel.SendConfirmAsync($"已移除 `{channel.ChannelTitle}` 的爬蟲").ConfigureAwait(false);
                }
                else
                {
                    await Context.Channel.SendErrorAsync($"尚未設定 `{channelId}` 的爬蟲").ConfigureAwait(false);
                }
            }
        }
    }
}
