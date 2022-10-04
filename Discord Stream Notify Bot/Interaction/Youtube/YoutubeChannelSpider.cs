using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Discord_Stream_Notify_Bot.Interaction.Attribute;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Interaction.Youtube
{
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [Group("youtube-spider", "YouTube爬蟲設定")]
    [EnabledInDm(false)]
    public class YoutubeChannelSpider : TopLevelModule<SharedService.Youtube.YoutubeStreamService>
    {
        private readonly DiscordSocketClient _client;
        private readonly HttpClients.DiscordWebhookClient _discordWebhookClient;
        public class GuildYoutubeChannelSpiderAutocompleteHandler : AutocompleteHandler
        {
            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
            {
                using var db = DataBase.DBContext.GetDbContext();
                IQueryable<DataBase.Table.YoutubeChannelSpider> channelList;

                if (autocompleteInteraction.User.Id == Program.ApplicatonOwner.Id)
                {
                    channelList = db.YoutubeChannelSpider;
                }
                else
                {
                    if (!db.YoutubeChannelSpider.Any((x) => x.GuildId == autocompleteInteraction.GuildId))
                        return AutocompletionResult.FromSuccess();

                    channelList = db.YoutubeChannelSpider.Where((x) => x.GuildId == autocompleteInteraction.GuildId);
                }

                var channelList2 = new List<DataBase.Table.YoutubeChannelSpider>();
                try
                {
                    string value = autocompleteInteraction.Data.Current.Value.ToString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        foreach (var item in channelList)   
                        {
                            if (item.ChannelTitle.Contains(value, StringComparison.CurrentCultureIgnoreCase) || item.ChannelId.Contains(value, StringComparison.CurrentCultureIgnoreCase))
                            {
                                channelList2.Add(item);
                            }
                        }
                    }
                    else
                    {
                        channelList2 = channelList.ToList();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"GuildYoutubeChannelSpiderAutocompleteHandler - {ex}");
                }

                List<AutocompleteResult> results = new();
                foreach (var item in channelList2)
                {
                    results.Add(new AutocompleteResult(item.ChannelTitle, item.ChannelId));
                }

                return AutocompletionResult.FromSuccess(results.Take(25));
            }
        }

        public YoutubeChannelSpider(DiscordSocketClient client, HttpClients.DiscordWebhookClient discordWebhookClient)
        {
            _client = client;
            _discordWebhookClient = discordWebhookClient;

            _client.ButtonExecuted += async (button) =>
            {
                try
                {
                    if (button.HasResponded || !button.Data.CustomId.StartsWith("spider:"))
                        return;

                    Log.Info($"\"{button.User}\" Click Button: {button.Data.CustomId}");
                    await button.DeferAsync(false);

                    string[] buttonData = button.Data.CustomId.Split(new char[] { ':' });
                    if (buttonData.Length != 3)
                    {
                        await button.SendErrorAsync("此按鈕無法使用", true, false);
                        return;
                    }

                    using var db = DataBase.DBContext.GetDbContext();
                    var youtubeChannelSpider = db.YoutubeChannelSpider.FirstOrDefault((x) => x.ChannelId == buttonData[2]);
                    if (youtubeChannelSpider == null)
                    {
                        await button.SendErrorAsync("找不到此按鈕的頻道，可能已被移除", true, false);
                        return;
                    }

                    if (buttonData[1].Contains("trusted"))
                    {

                        youtubeChannelSpider.IsTrustedChannel = buttonData[1] == "trusted";
                        db.YoutubeChannelSpider.Update(youtubeChannelSpider);
                        db.SaveChanges();

                        await button.SendConfirmAsync($"已設定 {youtubeChannelSpider.ChannelTitle} 為`" + (youtubeChannelSpider.IsTrustedChannel ? "已" : "未") + "`認可頻道", true);
                    }
                    else if (buttonData[1].Contains("record"))
                    {
                        if (buttonData[1] == "record")
                        {
                            if (db.RecordYoutubeChannel.Any((x) => x.YoutubeChannelId == buttonData[2]))
                            {
                                await button.SendErrorAsync("該頻道已存在於錄影清單內", true);
                                return;
                            }
                            else
                            {
                                db.RecordYoutubeChannel.Add(new DataBase.Table.RecordYoutubeChannel() { YoutubeChannelId = buttonData[2] });
                                await button.SendConfirmAsync("已新增到錄影清單內", true);
                                db.SaveChanges();
                            }
                        }
                        else if (buttonData[1] == "unrecord")
                        {
                            if (!db.RecordYoutubeChannel.Any((x) => x.YoutubeChannelId == buttonData[2]))
                            {
                                await button.SendErrorAsync("該頻道未存在於錄影清單內", true);
                                return;
                            }
                            else
                            {
                                db.RecordYoutubeChannel.Remove(db.RecordYoutubeChannel.First((x) => x.YoutubeChannelId == buttonData[2]));
                                await button.SendConfirmAsync("已於錄影清單移除", true);
                            }
                        }
                    }

                    db.SaveChanges();

                    try
                    {
                        var guild = button.Message.Embeds.First().Fields.FirstOrDefault((x) => x.Name == "伺服器").Value;
                        var user = button.Message.Embeds.First().Fields.FirstOrDefault((x) => x.Name == "執行者").Value;
                        var embed = new EmbedBuilder()
                                        .WithOkColor()
                                        .WithTitle("已新增爬蟲頻道")
                                        .AddField("頻道", Format.Url(youtubeChannelSpider.ChannelTitle, $"https://www.youtube.com/channel/{youtubeChannelSpider.ChannelId}"), false)
                                        .AddField("伺服器", guild, false)
                                        .AddField("執行者", user, false)
                                        .AddField("認可頻道", youtubeChannelSpider.IsTrustedChannel ? "是" : "否", true)
                                        .AddField("錄影頻道", db.RecordYoutubeChannel.Any((x) => x.YoutubeChannelId == buttonData[2]) ? "是" : "否", true).Build();
                        try
                        {
                            await button.UpdateAsync((func) =>
                            {
                                func.Embed = embed;
                            });
                        }
                        catch
                        {
                            await button.ModifyOriginalResponseAsync((func) =>
                            {
                                func.Embed = embed;
                            });
                        }
                    }
                    catch (Exception)
                    {

                        throw;
                    }
                }
                catch (Exception ex)
                {
                    await button.SendErrorAsync(ex.Message, true);
                    Log.Error(ex.ToString());
                }                
            };
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [RequireGuildMemberCount(300)]
        [CommandSummary("新增非兩大箱的頻道檢測爬蟲\n" +
           "**禁止新增非VTuber的頻道**\n" +
           "伺服器需大於300人才可使用\n" +
           "未來會根據情況增減可新增的頻道數量\n" +
           "如有任何需要請向擁有者詢問")]
        [CommandExample("https://www.youtube.com/channel/UC0qt9BfrpQo-drjuPKl_vdA",
            "https://www.youtube.com/c/かぐらななななかぐ辛党Ch")]
        [SlashCommand("add", "新增非兩大箱的頻道檢測爬蟲")]
        public async Task AddChannelSpider([Summary("頻道網址")] string channelUrl)
        {
            await DeferAsync(true).ConfigureAwait(false);

            string channelId = "";
            try
            {
                channelId = await _service.GetChannelIdAsync(channelUrl).ConfigureAwait(false);
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
                bool isTwoBox = false;
                using (var Holodb = DataBase.HoloVideoContext.GetDbContext())
                    if (Holodb.Video.Any((x) => x.ChannelId == channelId)) isTwoBox =  true;
                using (var Nijidb = DataBase.NijisanjiVideoContext.GetDbContext())
                    if (Nijidb.Video.Any((x) => x.ChannelId == channelId)) isTwoBox = true;

                if (isTwoBox && !db.YoutubeChannelOwnedType.Any((x) => x.ChannelId == channelId))
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

                string channelTitle = await _service.GetChannelTitle(channelId).ConfigureAwait(false);
                if (channelTitle == "")
                {
                    await Context.Interaction.SendErrorAsync($"頻道 {channelId} 不存在", true).ConfigureAwait(false);
                    return;
                }

                var spider = new DataBase.Table.YoutubeChannelSpider() { GuildId = Context.Guild.Id, ChannelId = channelId, ChannelTitle = channelTitle };
                if (Context.User.Id == Program.ApplicatonOwner.Id && !await PromptUserConfirmAsync("設定該爬蟲為本伺服器使用?"))
                    spider.GuildId = 0;

                db.YoutubeChannelSpider.Add(spider);
                db.SaveChanges();

                await Context.Interaction.SendConfirmAsync($"已將 {channelTitle} 加入到爬蟲清單內\n" +
                    $"請到通知頻道內使用 `/youtube add-youtube-notice https://www.youtube.com/channel/{channelId}` 來開啟通知", true,true).ConfigureAwait(false);

                try
                {
                    await (await Program.ApplicatonOwner.CreateDMChannelAsync()).SendMessageAsync(embed: new EmbedBuilder()
                            .WithOkColor()
                            .WithTitle("已新增爬蟲頻道")
                            .AddField("頻道", Format.Url(channelTitle, $"https://www.youtube.com/channel/{channelId}"), false)
                            .AddField("伺服器", spider.GuildId != 0 ? $"{Context.Guild.Name} ({Context.Guild.Id})" : "擁有者", false)
                            .AddField("執行者", $"{Context.User.Username} ({Context.User.Id})", false)
                            .AddField("認可頻道", "否", true)
                            .AddField("錄影頻道", "否", true).Build(),
                        components: new ComponentBuilder()
                            .WithButton("加入認可頻道", $"spider:trusted:{channelId}", ButtonStyle.Success)
                            .WithButton("移除認可頻道", $"spider:untrusted:{channelId}", ButtonStyle.Danger)
                            .WithButton("加入錄影頻道", $"spider:record:{channelId}", ButtonStyle.Success, row: 1)
                            .WithButton("移除錄影頻道", $"spider:unrecord:{channelId}", ButtonStyle.Danger, row: 1).Build());
                }
                catch (Exception ex) { Log.Error(ex.ToString()); }
            }
        }

        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.Administrator, Group = "bot_owner")]
        [RequireOwner(Group = "bot_owner")]
        [CommandSummary("移除非兩大箱的頻道檢測爬蟲\n" +
            "爬蟲必須由本伺服器新增才可移除")]
        [CommandExample("https://www.youtube.com/channel/UC0qt9BfrpQo-drjuPKl_vdA",
            "https://www.youtube.com/c/かぐらななななかぐ辛党Ch")]
        [SlashCommand("remove", "移除非兩大箱的頻道檢測爬蟲")]
        public async Task RemoveChannelSpider([Summary("頻道網址"), Autocomplete(typeof(GuildYoutubeChannelSpiderAutocompleteHandler))] string channelUrl)
        {
            await DeferAsync(true).ConfigureAwait(false);

            string channelId = "";
            try
            {
                channelId = await _service.GetChannelIdAsync(channelUrl).ConfigureAwait(false);
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
                db.SaveChanges();
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
        [SlashCommand("list", "顯示已加入爬蟲檢測的頻道")]
        public async Task ListChannelSpider([Summary("頁數")] int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var list = db.YoutubeChannelSpider.ToList().Where((x) => !x.IsTrustedChannel).Select((x) => Format.Url(x.ChannelTitle, $"https://www.youtube.com/channel/{x.ChannelId}") +
                    $" 由 `" + (x.GuildId == 0 ? "Bot擁有者" : (_client.GetGuild(x.GuildId) != null ? _client.GetGuild(x.GuildId).Name : "已退出的伺服器")) + "` 新增");
                int warningChannelNum = db.YoutubeChannelSpider.Count((x) => !x.IsTrustedChannel);

                await Context.SendPaginatedConfirmAsync(page, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("直播爬蟲清單")
                        .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20)))
                        .WithFooter($"{Math.Min(list.Count(), (page + 1) * 20)} / {list.Count()}個頻道 ({warningChannelNum}個非VTuber的爬蟲)");
                }, list.Count(), 10, false).ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.Guild)]
        [SlashCommand("list-not-vtuber", "顯示已加入爬蟲檢測的頻道 (本清單可能內含中之人或前世的頻道)")]
        public async Task ListNotVTuberChannelSpider([Summary("頁數")] int page = 0)
        {
            if (page < 0) page = 0;

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var list = db.YoutubeChannelSpider.ToList().Where((x) => !x.IsTrustedChannel).Select((x) => Format.Url(x.ChannelTitle, $"https://www.youtube.com/channel/{x.ChannelId}") +
                    $" 由 `" + (x.GuildId == 0 ? "Bot擁有者" : (_client.GetGuild(x.GuildId) != null ? _client.GetGuild(x.GuildId).Name : "已退出的伺服器")) + "` 新增");

                await Context.SendPaginatedConfirmAsync(page, page =>
                {
                    return new EmbedBuilder()
                        .WithOkColor()
                        .WithTitle("非VTuber爬蟲清單")
                        .WithDescription(string.Join('\n', list.Skip(page * 20).Take(20)))
                        .WithFooter($"{Math.Min(list.Count(), (page + 1) * 20)} / {list.Count()}個頻道");
                }, list.Count(), 10, false, true).ConfigureAwait(false);
            }
        }
    }
}