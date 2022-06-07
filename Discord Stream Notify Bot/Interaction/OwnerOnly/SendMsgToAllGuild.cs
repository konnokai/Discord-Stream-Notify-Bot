using Discord;
using Discord.Interactions;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Interaction.OwnerOnly
{
    [DontAutoRegister]
    public class SendMsgToAllGuild : TopLevelModule<Service.SendMsgToAllGuildService>
    {
        [SlashCommand("send-message", "傳送訊息到所有伺服器")]
        [RequireOwner]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task SendMessageToAllGuildAsync()
        {
            var mb = new ModalBuilder()
            .WithTitle("傳送全球訊息訊息")
            .WithCustomId("send_message")
            .AddTextInput("圖片網址", "image_url")
            .AddTextInput("訊息", "message", TextInputStyle.Paragraph);

            await Context.Interaction.RespondWithModalAsync(mb.Build());
        }
    }
}
