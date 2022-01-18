using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Command.Normal
{
    public class Normal : TopLevelModule
    {
        DiscordSocketClient _client;
        public Normal(DiscordSocketClient client)
        {
            _client = client;
        }

        [Command("Ping")]
        [Summary("延遲檢測")]
        public async Task PingAsync()
        {
            await Context.Channel.SendConfirmAsync(":ping_pong: " + _client.Latency.ToString() + "ms");
        }


        [Command("Invite")]
        [Summary("取得邀請連結")]
        public async Task InviteAsync()
        {
#if RELEASE
            if (Context.User.Id != Program.ApplicatonOwner.Id)
            {
                Program.SendMessageToDiscord(string.Format("[{0}-{1}] {2}:({3}) 使用了邀請指令",
                    Context.Guild.Name, Context.Channel.Name, Context.Message.Author.Username, Context.Message.Author.Id));
            }
#endif

            try
            {
                await (await Context.Message.Author.GetOrCreateDMChannelAsync())
                    .SendConfirmAsync("<https://discordapp.com/api/oauth2/authorize?client_id=" + _client.CurrentUser.Id + "&permissions=268569697&scope=bot>\n");
            }
            catch (Exception) { await Context.Channel.SendConfirmAsync("無法私訊，請確認已開啟伺服器內成員私訊許可"); }
        }

        [Command("Status")]
        [Summary("顯示機器人目前的狀態")]
        [Alias("Stats")]
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
            embedBuilder.AddField("看過的直播數量", Utility.GetDbStreamCount(), true);
            embedBuilder.AddField("上線時間", $"{Program.stopWatch.Elapsed:d\\天\\ hh\\:mm\\:ss}", false);

            await ReplyAsync(null, false, embedBuilder.Build());
        }
    }
}
