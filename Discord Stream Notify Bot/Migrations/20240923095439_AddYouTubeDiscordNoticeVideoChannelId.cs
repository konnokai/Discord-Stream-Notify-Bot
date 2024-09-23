﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations
{
    /// <inheritdoc />
    public partial class AddYouTubeDiscordNoticeVideoChannelId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "NoticeStreamChannelId",
                table: "NoticeYoutubeStreamChannel",
                newName: "YouTubeChannelId");

            migrationBuilder.RenameColumn(
                name: "DiscordChannelId",
                table: "NoticeYoutubeStreamChannel",
                newName: "DiscordNoticeVideoChannelId");

            migrationBuilder.AddColumn<ulong>(
                name: "DiscordNoticeStreamChannelId",
                table: "NoticeYoutubeStreamChannel",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0ul);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscordNoticeStreamChannelId",
                table: "NoticeYoutubeStreamChannel");

            migrationBuilder.RenameColumn(
                name: "YouTubeChannelId",
                table: "NoticeYoutubeStreamChannel",
                newName: "NoticeStreamChannelId");

            migrationBuilder.RenameColumn(
                name: "DiscordNoticeVideoChannelId",
                table: "NoticeYoutubeStreamChannel",
                newName: "DiscordChannelId");
        }
    }
}
