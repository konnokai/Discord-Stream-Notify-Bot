using Discord.Interactions;

namespace Discord_Stream_Notify_Bot.Interaction.Attribute
{
    public class RequireGuildMemberCountAttribute : PreconditionAttribute
    {
        public RequireGuildMemberCountAttribute(uint gCount)
        {
            GuildMemberCount = gCount;
        }

        public uint? GuildMemberCount { get; }
        public override string ErrorMessage { get; } = "此伺服器不可使用本指令";

        public override Task<PreconditionResult> CheckRequirementsAsync(IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
        {
            if (context.Interaction.User.Id == Program.ApplicatonOwner.Id) return Task.FromResult(PreconditionResult.FromSuccess());

            if (((SocketGuild)context.Guild).MemberCount >= GuildMemberCount) return Task.FromResult(PreconditionResult.FromSuccess());
            else return Task.FromResult(PreconditionResult.FromError($"伺服器人數小於 {GuildMemberCount} 人，不可使用本指令\n" +
                $"此指令要求伺服器人數須大於等於 {GuildMemberCount} 人\n" +
                $"如有需要請聯繫 Bot 擁有者處理 (你可使用 `/utility send-message-to-bot-owner` 對擁有者發送訊息)"));
        }
    }
}