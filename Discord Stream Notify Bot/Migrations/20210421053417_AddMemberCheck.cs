using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Discord_Stream_Notify_Bot.Migrations
{
    public partial class AddMemberCheck : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangeNowStreamerEmojiToNoticeChannel",
                table: "GuildConfig");

            migrationBuilder.DropColumn(
                name: "NoticeGuildChannelId",
                table: "GuildConfig");

            migrationBuilder.AddColumn<string>(
                name: "MemberCheckChannelId",
                table: "GuildConfig",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MemberCheckGrantRoleId",
                table: "GuildConfig",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MemberCheckVideoId",
                table: "GuildConfig",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MemberAccessToken",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DiscordUserId = table.Column<string>(nullable: true),
                    GoogleAccessToken = table.Column<string>(nullable: true),
                    GoogleRefrechToken = table.Column<string>(nullable: true),
                    GoogleExpiresIn = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberAccessToken", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "YoutubeMemberCheck",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<ulong>(nullable: false),
                    LastCheckTime = table.Column<DateTime>(nullable: false),
                    LastCheckStatus = table.Column<int>(nullable: false),
                    GuildConfigId = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YoutubeMemberCheck", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YoutubeMemberCheck_GuildConfig_GuildConfigId",
                        column: x => x.GuildConfigId,
                        principalTable: "GuildConfig",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_YoutubeMemberCheck_GuildConfigId",
                table: "YoutubeMemberCheck",
                column: "GuildConfigId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberAccessToken");

            migrationBuilder.DropTable(
                name: "YoutubeMemberCheck");

            migrationBuilder.DropColumn(
                name: "MemberCheckChannelId",
                table: "GuildConfig");

            migrationBuilder.DropColumn(
                name: "MemberCheckGrantRoleId",
                table: "GuildConfig");

            migrationBuilder.DropColumn(
                name: "MemberCheckVideoId",
                table: "GuildConfig");

            migrationBuilder.AddColumn<bool>(
                name: "ChangeNowStreamerEmojiToNoticeChannel",
                table: "GuildConfig",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<ulong>(
                name: "NoticeGuildChannelId",
                table: "GuildConfig",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0ul);
        }
    }
}
