using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations
{
    public partial class AddMultiMemberCheck : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_YoutubeMemberCheck_GuildConfig_GuildConfigId",
                table: "YoutubeMemberCheck");

            migrationBuilder.DropTable(
                name: "MemberAccessToken");

            migrationBuilder.DropIndex(
                name: "IX_YoutubeMemberCheck_GuildConfigId",
                table: "YoutubeMemberCheck");

            migrationBuilder.DropColumn(
                name: "GuildConfigId",
                table: "YoutubeMemberCheck");

            migrationBuilder.DropColumn(
                name: "MemberCheckChannelId",
                table: "GuildConfig");

            migrationBuilder.DropColumn(
                name: "MemberCheckGrantRoleId",
                table: "GuildConfig");

            migrationBuilder.DropColumn(
                name: "MemberCheckVideoId",
                table: "GuildConfig");

            migrationBuilder.AddColumn<string>(
                name: "CheckYTChannelId",
                table: "YoutubeMemberCheck",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<ulong>(
                name: "GuildId",
                table: "YoutubeMemberCheck",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0ul);

            migrationBuilder.CreateTable(
                name: "GuildYoutubeMemberConfig",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    MemberCheckChannelId = table.Column<string>(type: "TEXT", nullable: true),
                    MemberCheckChannelTitle = table.Column<string>(type: "TEXT", nullable: true),
                    MemberCheckVideoId = table.Column<string>(type: "TEXT", nullable: true),
                    MemberCheckGrantRoleId = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildYoutubeMemberConfig", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuildYoutubeMemberConfig");

            migrationBuilder.DropColumn(
                name: "CheckYTChannelId",
                table: "YoutubeMemberCheck");

            migrationBuilder.DropColumn(
                name: "GuildId",
                table: "YoutubeMemberCheck");

            migrationBuilder.AddColumn<int>(
                name: "GuildConfigId",
                table: "YoutubeMemberCheck",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MemberCheckChannelId",
                table: "GuildConfig",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<ulong>(
                name: "MemberCheckGrantRoleId",
                table: "GuildConfig",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0ul);

            migrationBuilder.AddColumn<string>(
                name: "MemberCheckVideoId",
                table: "GuildConfig",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MemberAccessToken",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DiscordUserId = table.Column<string>(type: "TEXT", nullable: true),
                    GoogleAccessToken = table.Column<string>(type: "TEXT", nullable: true),
                    GoogleExpiresIn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GoogleRefrechToken = table.Column<string>(type: "TEXT", nullable: true),
                    YoutubeChannelId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberAccessToken", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_YoutubeMemberCheck_GuildConfigId",
                table: "YoutubeMemberCheck",
                column: "GuildConfigId");

            migrationBuilder.AddForeignKey(
                name: "FK_YoutubeMemberCheck_GuildConfig_GuildConfigId",
                table: "YoutubeMemberCheck",
                column: "GuildConfigId",
                principalTable: "GuildConfig",
                principalColumn: "Id");
        }
    }
}
