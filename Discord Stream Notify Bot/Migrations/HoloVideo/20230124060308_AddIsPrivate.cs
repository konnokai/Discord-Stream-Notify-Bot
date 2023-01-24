using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations.HoloVideo
{
    public partial class AddIsPrivate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrivate",
                table: "Video",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPrivate",
                table: "Video");
        }
    }
}
