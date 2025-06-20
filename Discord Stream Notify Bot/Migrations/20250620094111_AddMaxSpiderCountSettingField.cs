using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxSpiderCountSettingField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "max_twitcasting_spider_count",
                table: "guild_config",
                type: "int unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "max_twitch_spider_count",
                table: "guild_config",
                type: "int unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "max_twitter_space_spider_count",
                table: "guild_config",
                type: "int unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "max_you_tube_member_check_count",
                table: "guild_config",
                type: "int unsigned",
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "max_you_tube_spider_count",
                table: "guild_config",
                type: "int unsigned",
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_twitcasting_spider_count",
                table: "guild_config");

            migrationBuilder.DropColumn(
                name: "max_twitch_spider_count",
                table: "guild_config");

            migrationBuilder.DropColumn(
                name: "max_twitter_space_spider_count",
                table: "guild_config");

            migrationBuilder.DropColumn(
                name: "max_you_tube_member_check_count",
                table: "guild_config");

            migrationBuilder.DropColumn(
                name: "max_you_tube_spider_count",
                table: "guild_config");
        }
    }
}
