using Discord.Interactions;

namespace Discord_Stream_Notify_Bot.Interaction.OwnerOnly.Service
{
    public class SendMsgToAllGuildService : IInteractionService
    {
        public enum NoticeType
        {
            [ChoiceDisplay("一般")]
            Normal,
            [ChoiceDisplay("工商")]
            Sponsor
        }

        private class ButtonCheckData
        {
            public ulong UserId { get; set; }
            public ulong ChannelId { get; set; }
            public string Guid { get; set; }
            public NoticeType NoticeType { get; set; }
            public Embed Embed { get; set; }

            public ButtonCheckData(ulong userId, ulong channelId, string guid, NoticeType noticeType, Embed embed)
            {
                UserId = userId;
                ChannelId = channelId;
                Guid = guid;
                NoticeType = noticeType;
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
                NoticeType noticeType = components.First(x => x.CustomId == "notice_type").Value == "工商" ? NoticeType.Sponsor : NoticeType.Normal;
                string imageUrl = components.First(x => x.CustomId == "image_url").Value ?? "";
                string message = components.First(x => x.CustomId == "message").Value;

                Embed embed = new EmbedBuilder().WithOkColor()
                    .WithUrl("https://konnokai.me/")
                    .WithTitle("來自開發者消息")
                    .WithAuthor(modal.User)
                    .WithDescription(message)
                    .WithImageUrl(imageUrl)
                    .WithFooter("管理員可以透過 `/utility set-global-notice-channel` 來設定由哪個頻道來接收小幫手相關的通知").Build();

                var guid = Guid.NewGuid().ToString().Replace("-", "");
                ComponentBuilder component = new ComponentBuilder()
                    .WithButton("是", $"{guid}-yes", ButtonStyle.Success)
                    .WithButton("否", $"{guid}-no", ButtonStyle.Danger);

                await modal.FollowupAsync(text: $"本次發送的類型為: {noticeType}", embed: embed, components: component.Build(), ephemeral: true).ConfigureAwait(false);
                checkData = new ButtonCheckData(modal.User.Id, modal.ChannelId.Value, guid, noticeType, embed);
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

                if (button.Data.CustomId.EndsWith("yes"))
                {
                    isSending = true;

                    await Task.Run(async () =>
                    {
                        var isSendMessageGuildId = new HashSet<ulong>();
                        using (var db = DataBase.MainDbContext.GetDbContext())
                        {
                            if (checkData.NoticeType == NoticeType.Normal)
                            {
                                try
                                {
                                    List<KeyValuePair<ulong, ulong>> list = db.GuildConfig
                                        .Distinct((x) => x.GuildId)
                                        .Where((x) => x.NoticeChannelId != 0)
                                        .Select((x) => new KeyValuePair<ulong, ulong>(x.GuildId, x.NoticeChannelId))
                                        .ToList();

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
                                                db.GuildConfig.RemoveRange(db.GuildConfig.Where((x) => x.GuildId == item.Key));
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
                                                db.GuildConfig.RemoveRange(db.GuildConfig.Where((x) => x.NoticeChannelId == item.Value));
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
                                            isSendMessageGuildId.Add(item.Key);
                                        }
                                        catch (Discord.Net.HttpException ex) when (ex.DiscordCode.HasValue && ex.DiscordCode == DiscordErrorCode.MissingPermissions ||
                                            ex.DiscordCode == DiscordErrorCode.InsufficientPermissions)
                                        {
                                            Log.Warn($"缺少權限導致無法傳送訊息至: {guild.Name} / {textChannel.Name}");
                                            db.GuildConfig.Single((x) => x.GuildId == guild.Id).NoticeChannelId = 0;
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
                                    Log.Error(ex, "Send Message To Global Notice Channel Error");
                                }

                                db.SaveChanges();
                                await button.SendConfirmAsync("已於全球訊息專用通知頻道發送完成", true, true);
                            }
                            else if (checkData.NoticeType == NoticeType.Sponsor)
                            {
                                foreach (var item in Discord_Stream_Notify_Bot.Utility.OfficialGuildList)
                                {
                                    isSendMessageGuildId.Add(item);
                                }

                                Log.Info($"工商訊息已忽略的官方伺服器數: {isSendMessageGuildId.Count}");
                            }

                            try
                            {
                                List<KeyValuePair<ulong, ulong>> list = db.NoticeYoutubeStreamChannel
                                    .Distinct((x) => x.GuildId)
                                    .Where((x) => !isSendMessageGuildId.Contains(x.GuildId) && _client.Guilds.Any((x2) => x2.Id == x.GuildId))
                                    .Select((x) => new KeyValuePair<ulong, ulong>(x.GuildId, x.DiscordChannelId))
                                    .ToList();

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
                                        isSendMessageGuildId.Add(item.Key);
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
                                Log.Error(ex, "Send Message To YouTube Notice Channel Error");
                            }

                            db.SaveChanges();
                            await button.SendConfirmAsync("已於 YT 通知頻道發送完成", true, true);

                            try
                            {
                                List<KeyValuePair<ulong, ulong>> list = db.NoticeTwitchStreamChannels
                                    .Distinct((x) => x.GuildId)
                                    .Where((x) => !isSendMessageGuildId.Contains(x.GuildId) && _client.Guilds.Any((x2) => x2.Id == x.GuildId))
                                    .Select((x) => new KeyValuePair<ulong, ulong>(x.GuildId, x.DiscordChannelId))
                                    .ToList();

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
                                            db.NoticeTwitchStreamChannels.RemoveRange(db.NoticeTwitchStreamChannels.Where((x) => x.GuildId == item.Key));
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
                                            db.NoticeTwitchStreamChannels.RemoveRange(db.NoticeTwitchStreamChannels.Where((x) => x.DiscordChannelId == item.Value));
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
                                        isSendMessageGuildId.Add(item.Key);
                                    }
                                    catch (Discord.Net.HttpException ex) when (ex.DiscordCode.HasValue && ex.DiscordCode == DiscordErrorCode.MissingPermissions ||
                                        ex.DiscordCode == DiscordErrorCode.InsufficientPermissions)
                                    {
                                        Log.Warn($"缺少權限導致無法傳送訊息至: {guild.Name} / {textChannel.Name}");
                                        db.NoticeTwitchStreamChannels.RemoveRange(db.NoticeTwitchStreamChannels.Where((x) => x.DiscordChannelId == item.Value));
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
                                Log.Error(ex, "Send Message To Twitch Notice Channel Error");
                            }

                            db.SaveChanges();
                            await button.SendConfirmAsync("已於 Twitch 通知頻道發送完成", true, true);

                            try
                            {
                                var memberList = db.GuildConfig
                                    .Distinct((x) => x.GuildId)
                                    .Where((x) => x.LogMemberStatusChannelId != 0 && !isSendMessageGuildId.Contains(x.GuildId) && _client.Guilds.Any((x2) => x2.Id == x.GuildId))
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
                                        isSendMessageGuildId.Add(item.Key);
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
                                Log.Error(ex, "Send Message To YouTube Memeber Notice Channel Error");
                            }

                            db.SaveChanges();
                            await button.SendConfirmAsync("已於會限驗證紀錄頻道發送完成", true, true);
                        }
                    });
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
