﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations
{
    /// <inheritdoc />
    public partial class AddGuildNoticeChannelId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<ulong>(
                name: "NoticeChannelId",
                table: "GuildConfig",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0ul);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NoticeChannelId",
                table: "GuildConfig");
        }
    }
}
