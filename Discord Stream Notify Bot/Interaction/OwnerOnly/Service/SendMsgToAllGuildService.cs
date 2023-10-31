namespace Discord_Stream_Notify_Bot.Interaction.OwnerOnly.Service
{
    public class SendMsgToAllGuildService : IInteractionService
    {
        private class ButtonCheckData
        {
            public ulong UserId { get; set; }
            public ulong ChannelId { get; set; }
            public string Guid { get; set; }
            public Embed Embed { get; set; }

            public ButtonCheckData(ulong userId, ulong channelId, string guid, Embed embed)
            {
                UserId = userId;
                ChannelId = channelId;
                Guid = guid;
                Embed = embed;
            }
        }

        private readonly DiscordSocketClient _client;
        private ButtonCheckData checkData;
        private bool isSending = false;

        public SendMsgToAllGuildService(DiscordSocketClient discordSocketClient)
        {
            _client = discordSocketClient;
            _client.ModalSubmitted += async modal =>
            {
                if (modal.Data.CustomId != "send_message")
                    return;

                await modal.DeferAsync(true);

                List<SocketMessageComponentData> components = modal.Data.Components.ToList();
                string imageUrl = components.First(x => x.CustomId == "image_url").Value ?? "";
                string message = components.First(x => x.CustomId == "message").Value;

                Embed embed = new EmbedBuilder().WithOkColor()
                    .WithUrl("https://konnokai.me/")
                    .WithTitle("來自開發者消息")
                    .WithAuthor(modal.User)
                    .WithDescription(message)
                    .WithImageUrl(imageUrl)
                    .WithFooter("若看到此消息出現在非通知頻道上，請通知管理員重新設定直播通知").Build();

                var guid = Guid.NewGuid().ToString().Replace("-", "");
                ComponentBuilder component = new ComponentBuilder()
                    .WithButton("是", $"{guid}-yes", ButtonStyle.Success)
                    .WithButton("否", $"{guid}-no", ButtonStyle.Danger);

                await modal.FollowupAsync(embed: embed, components: component.Build(), ephemeral: true).ConfigureAwait(false);
                checkData = new ButtonCheckData(modal.User.Id, modal.ChannelId.Value, guid, embed);
            };

            _client.ButtonExecuted += async button =>
            {
                if (checkData == null || isSending)
                    return;

                if (!button.Data.CustomId.StartsWith(checkData.Guid))
                    return;

                if (!(button is SocketMessageComponent userMsg) ||
                    !(userMsg.Channel is ITextChannel chan) ||
                    userMsg.User.Id != checkData.UserId ||
                    userMsg.Channel.Id != checkData.ChannelId)
                {
                    await button.SendErrorAsync("你無法使用本功能", true).ConfigureAwait(false);
                    return;
                }

                try
                {
                    await button.UpdateAsync((x) => x.Components = new ComponentBuilder()
                            .WithButton("是", $"{checkData.Guid}-yes", ButtonStyle.Success, disabled: true)
                            .WithButton("否", $"{checkData.Guid}-no", ButtonStyle.Danger, disabled: true).Build())
                        .ConfigureAwait(false);
                }
                catch { }

                //await button.DeferAsync(true);

                if (button.Data.CustomId.EndsWith("yes"))
                {
                    isSending = true;

                    using (var db = DataBase.DBContext.GetDbContext())
                    {
                        List<KeyValuePair<ulong, ulong>> list = db.NoticeYoutubeStreamChannel
                            .Distinct((x) => x.GuildId)
                            .Where((x) => _client.Guilds.Any((x2) => x2.Id == x.GuildId))
                            .Select((x) => new KeyValuePair<ulong, ulong>(x.GuildId, x.DiscordChannelId))
                            .ToList(); ;

                        try
                        {
                            int i = 0, num = list.Count;
                            foreach (var item in list)
                            {
                                i++;

                                var guild = _client.GetGuild(item.Key);
                                if (guild == null)
                                {
                                    Log.Warn($"伺服器不存在: {item.Key}");
                                    try
                                    {
                                        db.NoticeYoutubeStreamChannel.RemoveRange(db.NoticeYoutubeStreamChannel.Where((x) => x.GuildId == item.Key));
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(ex.ToString());
                                    }
                                    continue;
                                }

                                var textChannel = guild.GetTextChannel(item.Value);
                                if (textChannel == null)
                                {
                                    Log.Warn($"頻道不存在: {guild.Name} / {item.Value}");
                                    try
                                    {
                                        db.NoticeYoutubeStreamChannel.RemoveRange(db.NoticeYoutubeStreamChannel.Where((x) => x.DiscordChannelId == item.Value));
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(ex.ToString());
                                    }
                                    continue;
                                }

                                try
                                {
                                    await textChannel.SendMessageAsync(embed: checkData.Embed);
                                }
                                catch (Discord.Net.HttpException ex) when (ex.DiscordCode.HasValue && ex.DiscordCode == DiscordErrorCode.MissingPermissions ||
                                    ex.DiscordCode == DiscordErrorCode.InsufficientPermissions)
                                {
                                    Log.Warn($"缺少權限導致無法傳送訊息至: {guild.Name} / {textChannel.Name}");
                                    db.NoticeYoutubeStreamChannel.RemoveRange(db.NoticeYoutubeStreamChannel.Where((x) => x.DiscordChannelId == item.Value));
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, $"MSG: {guild.Name} / {textChannel.Name}");
                                }
                                finally
                                {
                                    Log.Info($"({i}/{num}) {item.Key}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"{ex}");
                        }

                        db.SaveChanges();
                        await button.Channel.SendMessageAsync("已於通知頻道發送完成");

                        try
                        {
                            var memberList = db.GuildConfig
                                .Distinct((x) => x.GuildId)
                                .Where((x) => x.LogMemberStatusChannelId != 0 && !list.Any((x2) => x.GuildId == x2.Key) && _client.Guilds.Any((x2) => x2.Id == x.GuildId))
                                .Select((x) => new KeyValuePair<ulong, ulong>(x.GuildId, x.LogMemberStatusChannelId))
                                .ToList();

                            int i = 0, num = memberList.Count;
                            foreach (var item in memberList)
                            {
                                i++;

                                var guild = _client.GetGuild(item.Key);
                                if (guild == null)
                                {
                                    Log.Warn($"伺服器不存在: {item.Key}");
                                    try
                                    {
                                        db.GuildConfig.RemoveRange(db.GuildConfig.Where((x) => x.GuildId == item.Key));
                                        db.GuildYoutubeMemberConfig.RemoveRange(db.GuildYoutubeMemberConfig.Where((x) => x.GuildId == item.Key));
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(ex.ToString());
                                    }
                                    continue;
                                }

                                var textChannel = guild.GetTextChannel(item.Value);
                                if (textChannel == null)
                                {
                                    Log.Warn($"頻道不存在: {guild.Name} / {item.Value}");
                                    try
                                    {
                                        db.GuildConfig.RemoveRange(db.GuildConfig.Where((x) => x.GuildId == item.Key));
                                        db.GuildYoutubeMemberConfig.RemoveRange(db.GuildYoutubeMemberConfig.Where((x) => x.GuildId == item.Key));
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(ex.ToString());
                                    }
                                    continue;
                                }

                                try
                                {
                                    await textChannel.SendMessageAsync(embed: checkData.Embed);
                                }
                                catch (Discord.Net.HttpException ex) when (ex.DiscordCode.HasValue && ex.DiscordCode == DiscordErrorCode.MissingPermissions ||
                                    ex.DiscordCode == DiscordErrorCode.InsufficientPermissions)
                                {
                                    Log.Warn($"缺少權限導致無法傳送訊息至: {guild.Name} / {textChannel.Name}");
                                    db.GuildConfig.RemoveRange(db.GuildConfig.Where((x) => x.GuildId == item.Key));
                                    db.GuildYoutubeMemberConfig.RemoveRange(db.GuildYoutubeMemberConfig.Where((x) => x.GuildId == item.Key));
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, $"MSG: {guild.Name} / {textChannel.Name}");
                                }
                                finally
                                {
                                    Log.Info($"({i}/{num}) {item.Key}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"{ex}");
                        }

                        db.SaveChanges();
                        await button.Channel.SendMessageAsync("已於會限驗證紀錄頻道發送完成");
                    }
                }
                else
                {
                    await button.SendErrorAsync("已取消發送", true);
                }

                checkData = null;
                isSending = false;
            };
        }
    }
}
