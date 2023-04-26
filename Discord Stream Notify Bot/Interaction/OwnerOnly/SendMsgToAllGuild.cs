using Discord.Interactions;
using Discord_Stream_Notify_Bot.Interaction.Attribute;

namespace Discord_Stream_Notify_Bot.Interaction.OwnerOnly
{
    [DontAutoRegister]
    [RequireGuild(506083124015398932)]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public class SendMsgToAllGuild : TopLevelModule<Service.SendMsgToAllGuildService>
    {
        [SlashCommand("send-message", "傳送訊息到所有伺服器")]
        [RequireOwner]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task SendMessageToAllGuildAsync(Attachment attachment = null)
        {
            var mb = new ModalBuilder()
            .WithTitle("傳送全球訊息")
            .WithCustomId("send_message")
            .AddTextInput("圖片網址", "image_url", placeholder: "https://...", value: attachment?.Url, required: false)
            .AddTextInput("訊息", "message", TextInputStyle.Paragraph, "內容...", required: true);

            await Context.Interaction.RespondWithModalAsync(mb.Build());
        }
    }
}
