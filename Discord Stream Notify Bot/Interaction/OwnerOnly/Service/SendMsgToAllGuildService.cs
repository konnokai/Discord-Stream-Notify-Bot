using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;

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
                            }

                            var textChannel = guild.GetTextChannel(item.Value);
                            if (textChannel == null)
                            {
                                Log.Warn($"頻到不存在: {guild.Name} / {item.Value}");
                                db.NoticeYoutubeStreamChannel.RemoveRange(db.NoticeYoutubeStreamChannel.Where((x) => x.GuildId == item.Key));
                                db.SaveChanges();
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
            };
        }
    }
}
