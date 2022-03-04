using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Interaction
{
    public abstract class TopLevelModule : InteractionModuleBase<SocketInteractionContext>
    {
        public async Task<bool> PromptUserConfirmAsync(string context)
        {
            string guid = Guid.NewGuid().ToString().Replace("-", "");

            EmbedBuilder embed = new EmbedBuilder()
                .WithOkColor()
                .WithDescription(context);

            ComponentBuilder component = new ComponentBuilder()
                .WithButton("是", $"{guid}-yes", ButtonStyle.Success)
                .WithButton("否", $"{guid}-no", ButtonStyle.Danger);

            var msg = await FollowupAsync(embed: embed.Build(), components: component.Build()).ConfigureAwait(false);

            try
            {
                var input = await GetUserClickAsync(Context.User.Id, Context.Channel.Id, guid).ConfigureAwait(false);
                if (!input) await msg.DeleteAsync().ConfigureAwait(false);
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
                        !(userMsg.Channel is ITextChannel chan) ||
                        userMsg.User.Id != userId ||
                        userMsg.Channel.Id != channelId)
                    {
                        await component.SendErrorAsync("你無法使用本功能").ConfigureAwait(false);
                        return Task.CompletedTask;
                    }

                    if (userInputTask.TrySetResult(component.Data.CustomId.EndsWith("yes")))
                    {
                        await component.UpdateAsync((x) => x.Components = new ComponentBuilder().Build())
                        .ConfigureAwait(false);
                    }
                    return Task.CompletedTask;
                });
                return Task.CompletedTask;
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
