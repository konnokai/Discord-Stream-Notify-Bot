using Discord.Commands;

namespace DiscordStreamNotifyBot.Command.Attribute
{
    public class RequireGuildOwnerAttribute : PreconditionAttribute
    {
        public RequireGuildOwnerAttribute()
        {
        }

        public override string ErrorMessage { get; set; } = "非伺服器擁有者不可使用本指令";

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.Message.Author.Id == Bot.ApplicatonOwner.Id) return Task.FromResult(PreconditionResult.FromSuccess());

            if (context.Message.Author.Id == context.Guild.OwnerId) return Task.FromResult(PreconditionResult.FromSuccess());
            else return Task.FromResult(PreconditionResult.FromError(ErrorMessage));
        }
    }
}