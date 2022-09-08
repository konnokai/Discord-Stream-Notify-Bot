using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations
{
    public partial class RenameFidle : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsVTuberChannel",
                table: "YoutubeChannelSpider",
                newName: "IsTrustedChannel");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsTrustedChannel",
                table: "YoutubeChannelSpider",
                newName: "IsVTuberChannel");
        }
    }
}
