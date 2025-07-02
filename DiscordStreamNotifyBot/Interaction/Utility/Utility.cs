using Discord.Interactions;
using DiscordStreamNotifyBot.DataBase;
using DiscordStreamNotifyBot.Interaction.Utility.Service;

namespace DiscordStreamNotifyBot.Interaction.Utility
{
    [Group("utility", "工具")]
    public class Utility : TopLevelModule<UtilityService>
    {
        private readonly DiscordSocketClient _client;
        private readonly HttpClients.DiscordWebhookClient _discordWebhookClient;
        private readonly MainDbService _dbService;

        public Utility(DiscordSocketClient client, HttpClients.DiscordWebhookClient discordWebhookClient, MainDbService dbService)
        {
            _client = client;
            _discordWebhookClient = discordWebhookClient;
            _dbService = dbService;
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
            if (Context.User.Id != Bot.ApplicatonOwner.Id)
            {
                _discordWebhookClient.SendMessageToDiscord($"[{Context.Guild.Name}-{Context.Channel.Name}] {Context.User.Username}:({Context.User.Id}) 使用了邀請指令");
            }
#endif     
            await Context.Interaction.SendConfirmAsync("<https://discordapp.com/api/oauth2/authorize?client_id=" + _client.CurrentUser.Id + "&permissions=11006299201&scope=bot+applications.commands>", ephemeral: true);
        }

        [SlashCommand("status", "顯示機器人目前的狀態")]
        public async Task StatusAsync()
        {
            EmbedBuilder embedBuilder = new EmbedBuilder().WithOkColor();
            embedBuilder.WithTitle("直播小幫手");

#if DEBUG || DEBUG_DONTREGISTERCOMMAND
            embedBuilder.Title += " (測試版)";
#endif

            embedBuilder.WithDescription($"建置版本 {Program.Version}");
            embedBuilder.AddField("作者", "孤之界 (konnokai)", true);
            embedBuilder.AddField("擁有者", $"{Bot.ApplicatonOwner}", true);
            embedBuilder.AddField("狀態", $"伺服器 {_client.Guilds.Count}\n服務成員數 {_client.Guilds.Sum((x) => x.MemberCount)}", false);
            embedBuilder.AddField("看過的直播數量", DiscordStreamNotifyBot.Utility.GetDbStreamCount(), true);
            embedBuilder.AddField("上線時間", $"{Bot.StopWatch.Elapsed:d\\天\\ hh\\:mm\\:ss}", false);

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

        [SlashCommand("set-global-notice-channel", "設定要接收 Bot 擁有者發送的訊息頻道")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task SetGlobalNoticeChannel([Summary("接收通知的頻道"), ChannelTypes(ChannelType.Text, ChannelType.News)] IChannel channel)
        {
            try
            {
                var textChannel = channel as IGuildChannel;
                var permissions = Context.Guild.GetUser(_client.CurrentUser.Id).GetPermissions(textChannel);
                if (!permissions.ViewChannel || !permissions.SendMessages)
                {
                    await Context.Interaction.SendErrorAsync($"我在 `{textChannel}` 沒有 `讀取&編輯頻道` 的權限，請給予權限後再次執行本指令");
                    return;
                }

                if (!permissions.EmbedLinks)
                {
                    await Context.Interaction.SendErrorAsync($"我在 `{textChannel}` 沒有 `嵌入連結` 的權限，請給予權限後再次執行本指令");
                    return;
                }

                using var db = _dbService.GetDbContext();
                var guildConfig = db.GuildConfig.FirstOrDefault((x) => x.GuildId == Context.Guild.Id) ?? new DataBase.Table.GuildConfig();
                guildConfig.NoticeChannelId = channel.Id;
                db.SaveChanges();

                await Context.Interaction.SendConfirmAsync($"已設定全球通知頻道為: {channel}", ephemeral: true);
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "Set Notice Channel Error");
                await Context.Interaction.SendErrorAsync($"設定全球通知失敗，請向 Bot 擁有者詢問");
            }
        }
    }
}