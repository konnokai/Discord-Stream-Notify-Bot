using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations
{
    public partial class AddedDateTime : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateAdded",
                table: "YoutubeMemberCheck",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAdded",
                table: "YoutubeChannelSpider",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAdded",
                table: "YoutubeChannelOwnedType",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAdded",
                table: "TwitterSpaecSpider",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAdded",
                table: "TwitterSpace",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAdded",
                table: "TwitCastingSpider",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAdded",
                table: "RecordYoutubeChannel",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAdded",
                table: "NoticeYoutubeStreamChannel",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAdded",
                table: "NoticeTwitterSpaceChannel",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAdded",
                table: "NoticeTwitCastingStreamChannels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAdded",
                table: "GuildYoutubeMemberConfig",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAdded",
                table: "GuildConfig",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateAdded",
                table: "BannerChange",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateAdded",
                table: "YoutubeMemberCheck");

            migrationBuilder.DropColumn(
                name: "DateAdded",
                table: "YoutubeChannelSpider");

            migrationBuilder.DropColumn(
                name: "DateAdded",
                table: "YoutubeChannelOwnedType");

            migrationBuilder.DropColumn(
                name: "DateAdded",
                table: "TwitterSpaecSpider");

            migrationBuilder.DropColumn(
                name: "DateAdded",
                table: "TwitterSpace");

            migrationBuilder.DropColumn(
                name: "DateAdded",
                table: "TwitCastingSpider");

            migrationBuilder.DropColumn(
                name: "DateAdded",
                table: "RecordYoutubeChannel");

            migrationBuilder.DropColumn(
                name: "DateAdded",
                table: "NoticeYoutubeStreamChannel");

            migrationBuilder.DropColumn(
                name: "DateAdded",
                table: "NoticeTwitterSpaceChannel");

            migrationBuilder.DropColumn(
                name: "DateAdded",
                table: "NoticeTwitCastingStreamChannels");

            migrationBuilder.DropColumn(
                name: "DateAdded",
                table: "GuildYoutubeMemberConfig");

            migrationBuilder.DropColumn(
                name: "DateAdded",
                table: "GuildConfig");

            migrationBuilder.DropColumn(
                name: "DateAdded",
                table: "BannerChange");
        }
    }
}
