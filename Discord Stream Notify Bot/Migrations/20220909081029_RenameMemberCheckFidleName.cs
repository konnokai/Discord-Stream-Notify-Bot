using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations
{
    public partial class RenameMemberCheckFidleName : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LastCheckStatus",
                table: "YoutubeMemberCheck",
                newName: "IsChecked");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsChecked",
                table: "YoutubeMemberCheck",
                newName: "LastCheckStatus");
        }
    }
}
