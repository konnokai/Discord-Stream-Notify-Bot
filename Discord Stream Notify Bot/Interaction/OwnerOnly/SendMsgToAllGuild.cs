using Discord;
using Discord.Interactions;
using Discord_Stream_Notify_Bot.Interaction.Attribute;
using Imgur.API.Authentication;
using Imgur.API.Endpoints;
using System.Net.Http;
using System.Threading.Tasks;

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
            string imageUrl = null;
            if (!string.IsNullOrEmpty(_service.ImgurClientId) && attachment != null &&
                (attachment.Filename.EndsWith(".png") || attachment.Filename.EndsWith(".jpg")))
            {
                var apiClient = new ApiClient(_service.ImgurClientId);
                var httpClient = new HttpClient();
                var imageEndpoint = new ImageEndpoint(apiClient, httpClient);
                var imageUpload = await imageEndpoint.UploadImageAsync(attachment.Url, name: attachment.Filename);
                imageUrl = imageUpload.Link;
            }

            var mb = new ModalBuilder()
            .WithTitle("傳送全球訊息")
            .WithCustomId("send_message")
            .AddTextInput("圖片網址", "image_url", placeholder: "https://...", value: imageUrl, required: false)
            .AddTextInput("訊息", "message", TextInputStyle.Paragraph, "內容...", required: true);

            await Context.Interaction.RespondWithModalAsync(mb.Build());
        }
    }
}
