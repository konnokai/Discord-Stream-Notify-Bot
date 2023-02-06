using Discord.Interactions;

namespace Discord_Stream_Notify_Bot.Interaction.Utility
{
    [Group("utility", "工具")]
    public class Utility : TopLevelModule
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
#if DEBUG
            embedBuilder.Title += " (測試版)";
#endif

            embedBuilder.WithDescription($"建置版本 {Program.VERSION}");
            embedBuilder.AddField("作者", "孤之界#1121", true);
            embedBuilder.AddField("擁有者", $"{Program.ApplicatonOwner.Username}#{Program.ApplicatonOwner.Discriminator}", true);
            embedBuilder.AddField("狀態", $"伺服器 {_client.Guilds.Count}\n服務成員數 {_client.Guilds.Sum((x) => x.MemberCount)}", false);
            embedBuilder.AddField("看過的直播數量", Discord_Stream_Notify_Bot.Utility.GetDbStreamCount(), true);
            embedBuilder.AddField("上線時間", $"{Program.stopWatch.Elapsed:d\\天\\ hh\\:mm\\:ss}", false);

            await RespondAsync(embed: embedBuilder.Build());
        }
    }
}
