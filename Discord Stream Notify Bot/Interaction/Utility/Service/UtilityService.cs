namespace Discord_Stream_Notify_Bot.Interaction.Utility.Service
{
    public class UtilityService : IInteractionService
    {
        public UtilityService(DiscordSocketClient client)
        {
            client.ModalSubmitted += async modal =>
            {
                switch (modal.Data.CustomId)
                {
                    case "send-message-to-bot-owner":
                        {
                            await modal.DeferAsync(true);

                            List<SocketMessageComponentData> components = modal.Data.Components.ToList();
                            string message = components.First(x => x.CustomId == "message").Value;
                            string contactMethod = components.First(x => x.CustomId == "contact-method").Value;

                            var embedBuilder = new EmbedBuilder()
                                .WithOkColor()
                                .WithTitle("新的使用者訊息")
                                .WithAuthor(modal.User)
                                .AddField("訊息", message)
                                .AddField("聯繫方式", contactMethod);

                            var componentBuilder = new ComponentBuilder()
                                .WithButton("發送回覆", $"send-reply-to-user:{modal.User.Id}", ButtonStyle.Success);

                            await Program.ApplicatonOwner.SendMessageAsync(embed: embedBuilder.Build(), components: componentBuilder.Build());

                            embedBuilder
                                .WithTitle("")
                                .WithDescription($"已收到訊息，請確保你填寫的聯絡資訊可讓 Bot 擁有者聯繫\n" +
                                    $"注意: Bot 擁有者會優先透過 Bot 來回應你的訊息，請確保你已開啟與本 Bot 共通伺服器的 `私人訊息` 隱私設定");

                            await modal.FollowupAsync(embed: embedBuilder.Build(), ephemeral: true);
                        }
                        break;
                    case "send-reply-to-user":
                        {
                            await modal.DeferAsync(true);

                            List<SocketMessageComponentData> components = modal.Data.Components.ToList();
                            ulong userId = ulong.Parse(components.First(x => x.CustomId == "userId").Value);
                            string message = components.First(x => x.CustomId == "message").Value;

                            try
                            {
                                await (await client.Rest.GetUserAsync(userId))
                                    .SendMessageAsync(embed: new EmbedBuilder()
                                        .WithOkColor()
                                        .WithTitle("來自擁有者的回覆")
                                        .WithDescription(message)
                                        .Build());

                                await modal.SendConfirmAsync($"發送成功\n" +
                                    $"回覆訊息: {message}", true, true);
                            }
                            catch (Discord.Net.HttpException httpEx) when (httpEx.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                            {
                                await modal.SendErrorAsync("無法發送訊息，該使用者未開放私人訊息", true, true);
                                return;
                            }
                            catch (Exception ex)
                            {
                                await modal.SendErrorAsync($"無法發送訊息: {ex}", true, true);
                                return;
                            }
                        }
                        break;
                    default:
                        break;
                }
            };

            client.ButtonExecuted += async button =>
            {
                if (button.HasResponded)
                    return;

                if (!button.Data.CustomId.StartsWith("send-reply-to-user"))
                    return;

                string userId = button.Data.CustomId.Split(':')[1];
                var modalBuilder = new ModalBuilder().WithTitle("回覆訊息給使用者")
                   .WithCustomId("send-reply-to-user")
                   .AddTextInput("UserId", "userId", TextInputStyle.Short, "", null, null, true, userId)
                   .AddTextInput("訊息", "message", TextInputStyle.Paragraph, "請輸入你要發送的訊息", null, null, true);

                await button.RespondWithModalAsync(modalBuilder.Build());
            };
        }
    }
}
