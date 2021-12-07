using Microsoft.EntityFrameworkCore.Migrations;

namespace Discord_Stream_Notify_Bot.Migrations
{
    public partial class AddScreenNameFidle : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserScreenName",
                table: "TwitterSpaecSpider",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserScreenName",
                table: "TwitterSpace",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UserScreenName",
                table: "TwitterSpaecSpider");

            migrationBuilder.DropColumn(
                name: "UserScreenName",
                table: "TwitterSpace");
        }
    }
}
