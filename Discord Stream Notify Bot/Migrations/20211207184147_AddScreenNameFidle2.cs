using Microsoft.EntityFrameworkCore.Migrations;

namespace Discord_Stream_Notify_Bot.Migrations
{
    public partial class AddScreenNameFidle2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TwitterScreenName",
                table: "RecordTwitterSpaceChannel",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TwitterScreenName",
                table: "RecordTwitterSpaceChannel");
        }
    }
}
