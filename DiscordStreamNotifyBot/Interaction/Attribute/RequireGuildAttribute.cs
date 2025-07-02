using Discord.Interactions;

namespace DiscordStreamNotifyBot.Interaction.Attribute
{
    public class RequireGuildAttribute : PreconditionAttribute
    {
        public RequireGuildAttribute(ulong gId)
        {
            GuildId = gId;
        }

        public ulong? GuildId { get; }

        public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
        {
            if (context.Guild.Id == GuildId) return Task.FromResult(PreconditionResult.FromSuccess());
            else return Task.FromResult(PreconditionResult.FromError("此伺服器不可使用本指令"));
        }
    }
}