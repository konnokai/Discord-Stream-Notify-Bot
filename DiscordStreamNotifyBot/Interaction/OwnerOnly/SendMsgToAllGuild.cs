using Discord.Interactions;
using DiscordStreamNotifyBot.Interaction.Attribute;
using static DiscordStreamNotifyBot.Interaction.OwnerOnly.Service.SendMsgToAllGuildService;

namespace DiscordStreamNotifyBot.Interaction.OwnerOnly
{
    [DontAutoRegister]
    [RequireGuild(506083124015398932)]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public class SendMsgToAllGuild : TopLevelModule<Service.SendMsgToAllGuildService>
    {
        [SlashCommand("send-message", "傳送訊息到所有伺服器")]
        [RequireOwner]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task SendMessageToAllGuildAsync(NoticeType noticeType, Attachment attachment = null)
        {
            var mb = new ModalBuilder()
            .WithTitle("傳送全球訊息")
            .WithCustomId("send_message")
            .AddTextInput("發送類型", "notice_type", placeholder: "一般 or 工商", value: noticeType == NoticeType.Normal ? "一般" : "工商", minLength: 2, maxLength: 2, required: true)
            .AddTextInput("圖片網址", "image_url", placeholder: "https://...", value: attachment?.Url, required: false)
            .AddTextInput("訊息", "message", TextInputStyle.Paragraph, "內容...", required: true);

            await Context.Interaction.RespondWithModalAsync(mb.Build());
        }
    }
}