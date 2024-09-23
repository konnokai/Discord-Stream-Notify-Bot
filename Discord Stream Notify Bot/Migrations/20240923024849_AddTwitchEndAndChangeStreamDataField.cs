using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations
{
    /// <inheritdoc />
    public partial class AddTwitchEndAndChangeStreamDataField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeStreamDataMessage",
                table: "NoticeTwitchStreamChannels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndStreamMessage",
                table: "NoticeTwitchStreamChannels",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangeStreamDataMessage",
                table: "NoticeTwitchStreamChannels");

            migrationBuilder.DropColumn(
                name: "EndStreamMessage",
                table: "NoticeTwitchStreamChannels");
        }
    }
}
