namespace Discord_Stream_Notify_Bot.Interaction.Utility.Service
{
    public class UtilityService : IInteractionService
    {
        public UtilityService(DiscordSocketClient client) 
        {
            client.ModalSubmitted += async modal =>
            {
                if (modal.Data.CustomId != "send-message-to-bot-owner")
                    return;

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

                await Program.ApplicatonOwner.SendMessageAsync(embed: embedBuilder.Build());

                embedBuilder
                    .WithTitle("")
                    .WithDescription("已收到訊息，請確保你填寫的聯絡資訊可讓 Bot 擁有者聯繫");

                await modal.FollowupAsync(embed: embedBuilder.Build(), ephemeral: true);
            };
        }
    }
}
