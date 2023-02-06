using Microsoft.EntityFrameworkCore.Migrations;

namespace Discord_Stream_Notify_Bot.Migrations
{
    public partial class InitDb : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BannerChange",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(nullable: false),
                    ChannelId = table.Column<string>(nullable: true),
                    LastChangeStreamId = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BannerChange", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelOwnedType",
                columns: table => new
                {
                    ChannelId = table.Column<string>(nullable: false),
                    ChannelTitle = table.Column<string>(nullable: true),
                    ChannelType = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelOwnedType", x => x.ChannelId);
                });

            migrationBuilder.CreateTable(
                name: "ChannelSpider",
                columns: table => new
                {
                    ChannelId = table.Column<string>(nullable: false),
                    ChannelTitle = table.Column<string>(nullable: true),
                    GuildId = table.Column<ulong>(nullable: false),
                    IsWarningChannel = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelSpider", x => x.ChannelId);
                });

            migrationBuilder.CreateTable(
                name: "GuildConfig",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(nullable: false),
                    NoticeGuildChannelId = table.Column<ulong>(nullable: false),
                    ChangeNowStreamerEmojiToNoticeChannel = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuildConfig", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HoloStreamVideo",
                columns: table => new
                {
                    VideoId = table.Column<string>(nullable: false),
                    ChannelId = table.Column<string>(nullable: true),
                    ChannelTitle = table.Column<string>(nullable: true),
                    VideoTitle = table.Column<string>(nullable: true),
                    ScheduledStartTime = table.Column<DateTime>(nullable: false),
                    ChannelType = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HoloStreamVideo", x => x.VideoId);
                });

            migrationBuilder.CreateTable(
                name: "NijisanjiStreamVideo",
                columns: table => new
                {
                    VideoId = table.Column<string>(nullable: false),
                    ChannelId = table.Column<string>(nullable: true),
                    ChannelTitle = table.Column<string>(nullable: true),
                    VideoTitle = table.Column<string>(nullable: true),
                    ScheduledStartTime = table.Column<DateTime>(nullable: false),
                    ChannelType = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NijisanjiStreamVideo", x => x.VideoId);
                });

            migrationBuilder.CreateTable(
                name: "NoticeStreamChannel",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(nullable: false),
                    ChannelId = table.Column<ulong>(nullable: false),
                    NoticeStreamChannelId = table.Column<string>(nullable: true),
                    NewStreamMessage = table.Column<string>(nullable: true),
                    NewVideoMessage = table.Column<string>(nullable: true),
                    StratMessage = table.Column<string>(nullable: true),
                    EndMessage = table.Column<string>(nullable: true),
                    ChangeTimeMessage = table.Column<string>(nullable: true),
                    DeleteMessage = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeStreamChannel", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OtherStreamVideo",
                columns: table => new
                {
                    VideoId = table.Column<string>(nullable: false),
                    ChannelId = table.Column<string>(nullable: true),
                    ChannelTitle = table.Column<string>(nullable: true),
                    VideoTitle = table.Column<string>(nullable: true),
                    ScheduledStartTime = table.Column<DateTime>(nullable: false),
                    ChannelType = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtherStreamVideo", x => x.VideoId);
                });

            migrationBuilder.CreateTable(
                name: "RecordChannel",
                columns: table => new
                {
                    ChannelId = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordChannel", x => x.ChannelId);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BannerChange");

            migrationBuilder.DropTable(
                name: "ChannelOwnedType");

            migrationBuilder.DropTable(
                name: "ChannelSpider");

            migrationBuilder.DropTable(
                name: "GuildConfig");

            migrationBuilder.DropTable(
                name: "HoloStreamVideo");

            migrationBuilder.DropTable(
                name: "NijisanjiStreamVideo");

            migrationBuilder.DropTable(
                name: "NoticeStreamChannel");

            migrationBuilder.DropTable(
                name: "OtherStreamVideo");

            migrationBuilder.DropTable(
                name: "RecordChannel");
        }
    }
}
