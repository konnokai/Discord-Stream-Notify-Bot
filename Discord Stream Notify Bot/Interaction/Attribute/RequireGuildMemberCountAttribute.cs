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

            var memberCount = ((SocketGuild)context.Guild).MemberCount;
            if (memberCount >= GuildMemberCount) return Task.FromResult(PreconditionResult.FromSuccess());
            else return Task.FromResult(PreconditionResult.FromError($"此伺服器不可使用本指令\n" +
                $"指令要求伺服器人數: `{GuildMemberCount}` 人\n" +
                $"目前 Bot 所取得的伺服器人數: `{memberCount}` 人\n" +
                $"由於快取的關係，可能會遇到伺服器人數錯誤的問題\n" +
                $"如有任何需要請聯繫 Bot 擁有者處理 (你可使用 `/utility send-message-to-bot-owner` 對擁有者發送訊息)"));
        }
    }
}