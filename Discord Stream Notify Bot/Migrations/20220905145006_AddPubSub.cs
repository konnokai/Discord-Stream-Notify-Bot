using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations
{
    public partial class AddPubSub : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVTuberChannel",
                table: "YoutubeChannelSpider",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSubscribeTime",
                table: "YoutubeChannelSpider",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "NotVTuberStreamVideo",
                columns: table => new
                {
                    VideoId = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<string>(type: "TEXT", nullable: true),
                    ChannelTitle = table.Column<string>(type: "TEXT", nullable: true),
                    VideoTitle = table.Column<string>(type: "TEXT", nullable: true),
                    ScheduledStartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChannelType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotVTuberStreamVideo", x => x.VideoId);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotVTuberStreamVideo");

            migrationBuilder.DropColumn(
                name: "IsVTuberChannel",
                table: "YoutubeChannelSpider");

            migrationBuilder.DropColumn(
                name: "LastSubscribeTime",
                table: "YoutubeChannelSpider");
        }
    }
}
