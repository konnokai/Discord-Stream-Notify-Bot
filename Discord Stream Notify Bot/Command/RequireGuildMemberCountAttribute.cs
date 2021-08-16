﻿using Discord.Commands;
using System;
using System.Threading.Tasks;
using Discord.WebSocket;

namespace Discord_Stream_Notify_Bot.Command
{
    public class RequireGuildMemberCountAttribute : PreconditionAttribute
    {
        public RequireGuildMemberCountAttribute(uint gCount)
        {
            GuildMemberCount = gCount;
        }

        public uint? GuildMemberCount { get; }
        public override string ErrorMessage { get; set; } = "此伺服器不可使用本指令";

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (((SocketGuild)context.Guild).MemberCount >= GuildMemberCount) return Task.FromResult(PreconditionResult.FromSuccess());
            else return Task.FromResult(PreconditionResult.FromError($"伺服器人數小於 {GuildMemberCount} 人，不可使用本指令\r\n" +
                $"此指令要求伺服器人數須大於等於 {GuildMemberCount} 人"));
        }
    }
}