using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations
{
    public partial class RemoveWarningChannelFidle : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsWarningChannel",
                table: "YoutubeChannelSpider");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsWarningChannel",
                table: "YoutubeChannelSpider",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
