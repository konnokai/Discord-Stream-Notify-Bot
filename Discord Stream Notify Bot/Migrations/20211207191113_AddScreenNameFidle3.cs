using Microsoft.EntityFrameworkCore.Migrations;

namespace Discord_Stream_Notify_Bot.Migrations
{
    public partial class AddScreenNameFidle3 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NoticeTwitterSpaceUserScreenName",
                table: "NoticeTwitterSpaceChannel",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NoticeTwitterSpaceUserScreenName",
                table: "NoticeTwitterSpaceChannel");
        }
    }
}
