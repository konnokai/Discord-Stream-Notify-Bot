using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;

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
                        try
                        {
                            var list = db.NoticeYoutubeStreamChannel.Distinct((x) => x.GuildId).Select((x) => new KeyValuePair<ulong, ulong>(x.GuildId, x.DiscordChannelId));
                            int i = 0, num = list.Count();
                            foreach (var item in list)
                            {
                                i++;

                                var guild = _client.GetGuild(item.Key);
                                if (guild == null)
                                {
                                    Log.Warn($"伺服器不存在: {item.Key}");
                                    db.NoticeYoutubeStreamChannel.RemoveRange(db.NoticeYoutubeStreamChannel.Where((x) => x.GuildId == item.Key));
                                    db.SaveChanges();
                                    continue;
                                }

                                var textChannel = guild.GetTextChannel(item.Value);
                                if (textChannel == null)
                                {
                                    Log.Warn($"頻道不存在: {guild.Name} / {item.Value}");
                                    db.NoticeYoutubeStreamChannel.RemoveRange(db.NoticeYoutubeStreamChannel.Where((x) => x.GuildId == item.Key));
                                    db.SaveChanges();
                                    continue;
                                }

                                try
                                {
                                    await textChannel.SendMessageAsync(embed: checkData.Embed);
                                }
                                catch (Discord.Net.HttpException ex)
                                {
                                    if (ex.DiscordCode == DiscordErrorCode.MissingPermissions || ex.DiscordCode == DiscordErrorCode.InsufficientPermissions)
                                    {
                                        Log.Warn($"無法傳送訊息至: {guild.Name} / {textChannel.Name}");
                                        db.NoticeYoutubeStreamChannel.RemoveRange(db.NoticeYoutubeStreamChannel.Where((x) => x.GuildId == item.Key));
                                        db.SaveChanges();
                                    }
                                    else
                                        Log.Error(ex.ToString());
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"MSG: {guild.Name} / {textChannel.Name}");
                                    Log.Error(ex.Message);
                                }
                                finally
                                {
                                    Log.Info($"({i}/{num}) {item.Key}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"{ex.Message}\n{ex.StackTrace}");
                        }

                        await button.SendConfirmAsync("已發送完成", true);
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
