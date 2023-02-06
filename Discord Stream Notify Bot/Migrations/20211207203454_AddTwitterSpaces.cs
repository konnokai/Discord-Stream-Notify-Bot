using Microsoft.EntityFrameworkCore.Migrations;

namespace Discord_Stream_Notify_Bot.Migrations
{
    public partial class AddTwitterSpaces : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelOwnedType");

            migrationBuilder.DropTable(
                name: "ChannelSpider");

            migrationBuilder.DropTable(
                name: "NoticeStreamChannel");

            migrationBuilder.DropTable(
                name: "RecordChannel");

            migrationBuilder.AlterColumn<ulong>(
                name: "MemberCheckGrantRoleId",
                table: "GuildConfig",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0ul,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "NoticeTwitterSpaceChannel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    DiscordChannelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    NoticeTwitterSpaceUserId = table.Column<string>(type: "TEXT", nullable: true),
                    NoticeTwitterSpaceUserScreenName = table.Column<string>(type: "TEXT", nullable: true),
                    StratTwitterSpaceMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeTwitterSpaceChannel", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NoticeYoutubeStreamChannel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    DiscordChannelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    NoticeStreamChannelId = table.Column<string>(type: "TEXT", nullable: true),
                    NewStreamMessage = table.Column<string>(type: "TEXT", nullable: true),
                    NewVideoMessage = table.Column<string>(type: "TEXT", nullable: true),
                    StratMessage = table.Column<string>(type: "TEXT", nullable: true),
                    EndMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ChangeTimeMessage = table.Column<string>(type: "TEXT", nullable: true),
                    DeleteMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeYoutubeStreamChannel", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecordTwitterSpaceChannel",
                columns: table => new
                {
                    TwitterUserId = table.Column<string>(type: "TEXT", nullable: false),
                    TwitterScreenName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordTwitterSpaceChannel", x => x.TwitterUserId);
                });

            migrationBuilder.CreateTable(
                name: "RecordYoutubeChannel",
                columns: table => new
                {
                    YoutubeChannelId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordYoutubeChannel", x => x.YoutubeChannelId);
                });

            migrationBuilder.CreateTable(
                name: "TwitterSpace",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    UserScreenName = table.Column<string>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", nullable: true),
                    SpaecId = table.Column<string>(type: "TEXT", nullable: true),
                    SpaecTitle = table.Column<string>(type: "TEXT", nullable: true),
                    SpaecActualStartTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitterSpace", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TwitterSpaecSpider",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    UserScreenName = table.Column<string>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", nullable: true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    IsWarningUser = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwitterSpaecSpider", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "YoutubeChannelOwnedType",
                columns: table => new
                {
                    ChannelId = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelTitle = table.Column<string>(type: "TEXT", nullable: true),
                    ChannelType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YoutubeChannelOwnedType", x => x.ChannelId);
                });

            migrationBuilder.CreateTable(
                name: "YoutubeChannelSpider",
                columns: table => new
                {
                    ChannelId = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelTitle = table.Column<string>(type: "TEXT", nullable: true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    IsWarningChannel = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YoutubeChannelSpider", x => x.ChannelId);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoticeTwitterSpaceChannel");

            migrationBuilder.DropTable(
                name: "NoticeYoutubeStreamChannel");

            migrationBuilder.DropTable(
                name: "RecordTwitterSpaceChannel");

            migrationBuilder.DropTable(
                name: "RecordYoutubeChannel");

            migrationBuilder.DropTable(
                name: "TwitterSpace");

            migrationBuilder.DropTable(
                name: "TwitterSpaecSpider");

            migrationBuilder.DropTable(
                name: "YoutubeChannelOwnedType");

            migrationBuilder.DropTable(
                name: "YoutubeChannelSpider");

            migrationBuilder.AlterColumn<string>(
                name: "MemberCheckGrantRoleId",
                table: "GuildConfig",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(ulong),
                oldType: "INTEGER");

            migrationBuilder.CreateTable(
                name: "ChannelOwnedType",
                columns: table => new
                {
                    ChannelId = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelTitle = table.Column<string>(type: "TEXT", nullable: true),
                    ChannelType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelOwnedType", x => x.ChannelId);
                });

            migrationBuilder.CreateTable(
                name: "ChannelSpider",
                columns: table => new
                {
                    ChannelId = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelTitle = table.Column<string>(type: "TEXT", nullable: true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    IsWarningChannel = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelSpider", x => x.ChannelId);
                });

            migrationBuilder.CreateTable(
                name: "NoticeStreamChannel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChangeTimeMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ChannelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    DeleteMessage = table.Column<string>(type: "TEXT", nullable: true),
                    EndMessage = table.Column<string>(type: "TEXT", nullable: true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    NewStreamMessage = table.Column<string>(type: "TEXT", nullable: true),
                    NewVideoMessage = table.Column<string>(type: "TEXT", nullable: true),
                    NoticeStreamChannelId = table.Column<string>(type: "TEXT", nullable: true),
                    StratMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeStreamChannel", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecordChannel",
                columns: table => new
                {
                    ChannelId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordChannel", x => x.ChannelId);
                });
        }
    }
}
