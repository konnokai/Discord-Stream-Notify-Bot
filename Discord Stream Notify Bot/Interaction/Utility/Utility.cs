using Discord.Interactions;
using Discord_Stream_Notify_Bot.Interaction.Utility.Service;

namespace Discord_Stream_Notify_Bot.Interaction.Utility
{
    [Group("utility", "工具")]
    public class Utility : TopLevelModule<UtilityService>
    {
        private readonly DiscordSocketClient _client;
        private readonly HttpClients.DiscordWebhookClient _discordWebhookClient;

        public Utility(DiscordSocketClient client, HttpClients.DiscordWebhookClient discordWebhookClient)
        {
            _client = client;
            _discordWebhookClient = discordWebhookClient;
        }

        [SlashCommand("ping", "延遲檢測")]
        public async Task PingAsync()
        {
            await Context.Interaction.SendConfirmAsync(":ping_pong: " + _client.Latency.ToString() + "ms");
        }


        [SlashCommand("invite", "取得邀請連結")]
        public async Task InviteAsync()
        {
#if RELEASE
            if (Context.User.Id != Program.ApplicatonOwner.Id)
            {
                _discordWebhookClient.SendMessageToDiscord($"[{Context.Guild.Name}-{Context.Channel.Name}] {Context.User.Username}:({Context.User.Id}) 使用了邀請指令");
            }
#endif     
            await Context.Interaction.SendConfirmAsync("<https://discordapp.com/api/oauth2/authorize?client_id=" + _client.CurrentUser.Id + "&permissions=2416143425&scope=bot%20applications.commands>", ephemeral: true);
        }

        [SlashCommand("status", "顯示機器人目前的狀態")]
        public async Task StatusAsync()
        {
            EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor();
            embedBuilder.WithTitle("直播小幫手");

#if DEBUG || DEBUG_DONTREGISTERCOMMAND
            embedBuilder.Title += " (測試版)";
#endif

            embedBuilder.WithDescription($"建置版本 {Program.VERSION}");
            embedBuilder.AddField("作者", "孤之界 (konnokai)", true);
            embedBuilder.AddField("擁有者", $"{Program.ApplicatonOwner}", true);
            embedBuilder.AddField("狀態", $"伺服器 {_client.Guilds.Count}\n服務成員數 {_client.Guilds.Sum((x) => x.MemberCount)}", false);
            embedBuilder.AddField("看過的直播數量", Discord_Stream_Notify_Bot.Utility.GetDbStreamCount(), true);
            embedBuilder.AddField("上線時間", $"{Program.stopWatch.Elapsed:d\\天\\ hh\\:mm\\:ss}", false);

            await RespondAsync(embed: embedBuilder.Build());
        }

        [SlashCommand("send-message-to-bot-owner", "聯繫 Bot 擁有者")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task SendMessageToBotOwner()
        {
            var modalBuilder = new ModalBuilder().WithTitle("聯繫 Bot 擁有者")
                .WithCustomId("send-message-to-bot-owner")
                .AddTextInput("訊息", "message", TextInputStyle.Paragraph, "請輸入你要發送的訊息", 10, null, true)
                .AddTextInput("聯繫方式", "contact-method", TextInputStyle.Short, "請輸入可與你聯繫的方式及相關資訊 (推特、Discord、Facebook等)", 3, null, true);

            await RespondWithModalAsync(modalBuilder.Build());
        }
    }
}