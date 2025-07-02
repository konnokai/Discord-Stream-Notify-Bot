using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DiscordStreamNotifyBot.Migrations
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
                defaultValue: 3u);

            migrationBuilder.AddColumn<uint>(
                name: "max_twitch_spider_count",
                table: "guild_config",
                type: "int unsigned",
                nullable: false,
                defaultValue: 3u);

            migrationBuilder.AddColumn<uint>(
                name: "max_twitter_space_spider_count",
                table: "guild_config",
                type: "int unsigned",
                nullable: false,
                defaultValue: 3u);

            migrationBuilder.AddColumn<uint>(
                name: "max_you_tube_member_check_count",
                table: "guild_config",
                type: "int unsigned",
                nullable: false,
                defaultValue: 5u);

            migrationBuilder.AddColumn<uint>(
                name: "max_you_tube_spider_count",
                table: "guild_config",
                type: "int unsigned",
                nullable: false,
                defaultValue: 3u);
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
