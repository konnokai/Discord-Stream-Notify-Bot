using Discord.Interactions;
using DiscordStreamNotifyBot.DataBase;

namespace DiscordStreamNotifyBot.Interaction
{
    public abstract class TopLevelModule : InteractionModuleBase<SocketInteractionContext>
    {
        public async Task<bool> PromptUserConfirmAsync(string context)
        {
            string guid = Guid.NewGuid().ToString().Replace("-", "");

            EmbedBuilder embed = new EmbedBuilder()
                .WithOkColor()
                .WithDescription(context)
                .WithFooter("10 秒後按鈕會無效化，請快速選擇或重新觸發");

            ComponentBuilder component = new ComponentBuilder()
                .WithButton("是", $"{guid}-yes", ButtonStyle.Success)
                .WithButton("否", $"{guid}-no", ButtonStyle.Danger);

            await FollowupAsync(embed: embed.Build(), components: component.Build(), ephemeral: true).ConfigureAwait(false);

            try
            {
                var input = await GetUserClickAsync(Context.User.Id, Context.Channel.Id, guid).ConfigureAwait(false);
                return input;
            }
            finally
            {
            }
        }

        public async Task<bool> GetUserClickAsync(ulong userId, ulong channelId, string guid)
        {
            var userInputTask = new TaskCompletionSource<bool>();

            try
            {
                Context.Client.ButtonExecuted += ButtonExecuted;

                if ((await Task.WhenAny(userInputTask.Task, Task.Delay(5000)).ConfigureAwait(false)) != userInputTask.Task)
                {
                    return false;
                }

                return await userInputTask.Task.ConfigureAwait(false);
            }
            finally
            {
                Context.Client.ButtonExecuted -= ButtonExecuted;
            }

            Task ButtonExecuted(SocketMessageComponent component)
            {
                var _ = Task.Run(async () =>
                {
                    if (!component.Data.CustomId.StartsWith(guid))
                        return Task.CompletedTask;

                    if (!(component is SocketMessageComponent userMsg) ||
                        userMsg.User.Id != userId ||
                        userMsg.Channel.Id != channelId)
                    {
                        await component.SendErrorAsync("你無法使用本功能", true).ConfigureAwait(false);
                        return Task.CompletedTask;
                    }

                    userInputTask.TrySetResult(component.Data.CustomId.EndsWith("yes"));

                    await component.UpdateAsync((x) => x.Components = new ComponentBuilder()
                        .WithButton("是", $"{guid}-yes", ButtonStyle.Success, disabled: true)
                        .WithButton("否", $"{guid}-no", ButtonStyle.Danger, disabled: true).Build())
                    .ConfigureAwait(false);
                    return Task.CompletedTask;
                });
                return Task.CompletedTask;
            }
        }

        public async Task CheckIsFirstSetNoticeAndSendWarningMessageAsync(MainDbContext dbContext)
        {
            bool firstCheck = !dbContext.NoticeYoutubeStreamChannel.AsNoTracking().Any((x) => x.GuildId == Context.Guild.Id);
            bool secondCheck = !dbContext.NoticeTwitchStreamChannels.AsNoTracking().Any((x) => x.GuildId == Context.Guild.Id);
            bool thirdCheck = !dbContext.GuildConfig.AsNoTracking().Any((x) => x.GuildId == Context.Guild.Id && x.LogMemberStatusChannelId != 0);
            if (firstCheck && secondCheck && thirdCheck)
            {
                await Context.Interaction.SendConfirmAsync("看來是第一次設定通知呢\n" +
                       "請注意 Bot 擁有者會透過通知頻道發送工商或是小幫手相關的通知 (功能更新之類的)\n" +
                       "你可以透過 `/utility set-global-notice-channel` 來設定由哪個頻道來接收小幫手相關的通知\n" +
                       "而工商相關通知則會直接發送到此頻道上\n" +
                       "(已認可的官方群組不會收到工商通知，如需添加認可或確認請向 Bot 擁有者詢問)\n" +
                       "(你可使用 `/utility send-message-to-bot-owner` 對 Bot 擁有者發送訊息)", true, true);
            }
        }
    }

    public abstract class TopLevelModule<TService> : TopLevelModule where TService : IInteractionService
    {
        protected TopLevelModule()
        {
        }

        public TService _service { get; set; }
    }
}
