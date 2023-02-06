using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations
{
    public partial class SplitYTVideoContext : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HoloStreamVideo");

            migrationBuilder.DropTable(
                name: "NijisanjiStreamVideo");

            migrationBuilder.DropTable(
                name: "NotVTuberStreamVideo");

            migrationBuilder.DropTable(
                name: "OtherStreamVideo");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HoloStreamVideo",
                columns: table => new
                {
                    VideoId = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<string>(type: "TEXT", nullable: true),
                    ChannelTitle = table.Column<string>(type: "TEXT", nullable: true),
                    ChannelType = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledStartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VideoTitle = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HoloStreamVideo", x => x.VideoId);
                });

            migrationBuilder.CreateTable(
                name: "NijisanjiStreamVideo",
                columns: table => new
                {
                    VideoId = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<string>(type: "TEXT", nullable: true),
                    ChannelTitle = table.Column<string>(type: "TEXT", nullable: true),
                    ChannelType = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledStartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VideoTitle = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NijisanjiStreamVideo", x => x.VideoId);
                });

            migrationBuilder.CreateTable(
                name: "NotVTuberStreamVideo",
                columns: table => new
                {
                    VideoId = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<string>(type: "TEXT", nullable: true),
                    ChannelTitle = table.Column<string>(type: "TEXT", nullable: true),
                    ChannelType = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledStartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VideoTitle = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotVTuberStreamVideo", x => x.VideoId);
                });

            migrationBuilder.CreateTable(
                name: "OtherStreamVideo",
                columns: table => new
                {
                    VideoId = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<string>(type: "TEXT", nullable: true),
                    ChannelTitle = table.Column<string>(type: "TEXT", nullable: true),
                    ChannelType = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledStartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VideoTitle = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtherStreamVideo", x => x.VideoId);
                });
        }
    }
}
