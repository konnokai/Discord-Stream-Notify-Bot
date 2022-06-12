using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Interaction.OwnerOnly.Service
{
    public class SendMsgToAllGuildService : IInteractionService
    {
        private readonly DiscordSocketClient _client;
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

                string guid = Guid.NewGuid().ToString().Replace("-", "");
                ComponentBuilder component = new ComponentBuilder()
                    .WithButton("是", $"{guid}-yes", ButtonStyle.Success)
                    .WithButton("否", $"{guid}-no", ButtonStyle.Danger);

                await modal.FollowupAsync(embed: embed, components: component.Build(), ephemeral: true).ConfigureAwait(false);
                if (await GetUserClickAsync(Program.ApplicatonOwner.Id, modal.ChannelId.GetValueOrDefault(), guid)) //Todo: 修正無法透過按鈕回傳確認的問題
                {
                    using (var db = DataBase.DBContext.GetDbContext())
                    {
                        try
                        {
                            var list = db.NoticeYoutubeStreamChannel.Distinct((x) => x.GuildId).Select((x) => new KeyValuePair<ulong, ulong>(x.GuildId, x.DiscordChannelId));
                            int i = 1, num = list.Count();
                            foreach (var item in list)
                            {
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
                                    await textChannel.SendMessageAsync(embed: embed);
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
                                    Log.Info($"({i++}/{num}) {item.Key}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"{ex.Message}\n{ex.StackTrace}");
                        }

                        await modal.SendConfirmAsync("已發送完成", true);
                    }
                }
                else
                    return;
            };
        }

        public async Task<bool> GetUserClickAsync(ulong userId, ulong channelId, string guid)
        {
            var userInputTask = new TaskCompletionSource<bool>();

            try
            {
                _client.ButtonExecuted += ButtonExecuted;

                if ((await Task.WhenAny(userInputTask.Task, Task.Delay(5000)).ConfigureAwait(false)) != userInputTask.Task)
                {
                    return false;
                }

                return await userInputTask.Task.ConfigureAwait(false);
            }
            finally
            {
                _client.ButtonExecuted -= ButtonExecuted;
            }

            Task ButtonExecuted(SocketMessageComponent component)
            {
                var _ = Task.Run(async () =>
                {
                    if (!component.Data.CustomId.StartsWith(guid))
                        return Task.CompletedTask;

                    if (!(component is SocketMessageComponent userMsg) ||
                        !(userMsg.Channel is ITextChannel chan) ||
                        userMsg.User.Id != userId ||
                        userMsg.Channel.Id != channelId)
                    {
                        await component.SendErrorAsync("你無法使用本功能", true).ConfigureAwait(false);
                        return Task.CompletedTask;
                    }

                    if (userInputTask.TrySetResult(component.Data.CustomId.EndsWith("yes")))
                    {
                        await component.ModifyOriginalResponseAsync((x) => x.Components = new ComponentBuilder()
                            .WithButton("是", $"{guid}-yes", ButtonStyle.Success, disabled: true)
                            .WithButton("否", $"{guid}-no", ButtonStyle.Danger, disabled: true).Build())
                        .ConfigureAwait(false);
                    }
                    return Task.CompletedTask;
                });
                return Task.CompletedTask;
            }
        }
    }
}
