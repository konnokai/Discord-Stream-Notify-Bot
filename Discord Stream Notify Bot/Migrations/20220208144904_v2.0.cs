using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations
{
    public partial class v20 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecordTwitterSpaceChannel");

            migrationBuilder.AddColumn<bool>(
                name: "IsRecord",
                table: "TwitterSpaecSpider",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SpaecMasterPlaylistUrl",
                table: "TwitterSpace",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YoutubeChannelId",
                table: "MemberAccessToken",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRecord",
                table: "TwitterSpaecSpider");

            migrationBuilder.DropColumn(
                name: "SpaecMasterPlaylistUrl",
                table: "TwitterSpace");

            migrationBuilder.DropColumn(
                name: "YoutubeChannelId",
                table: "MemberAccessToken");

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
        }
    }
}
