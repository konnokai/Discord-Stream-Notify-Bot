using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations
{
    /// <inheritdoc />
    public partial class ModifyTwitCastingTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "channel_id",
                table: "notice_twitcasting_stream_channels",
                newName: "screen_id");

            migrationBuilder.AddColumn<string>(
                name: "screen_id",
                table: "twitcasting_spider",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "screen_id",
                table: "twitcasting_spider");

            migrationBuilder.RenameColumn(
                name: "screen_id",
                table: "notice_twitcasting_stream_channels",
                newName: "channel_id");
        }
    }
}
