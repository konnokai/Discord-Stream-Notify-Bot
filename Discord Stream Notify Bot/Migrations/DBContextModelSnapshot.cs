﻿// <auto-generated />
using System;
using Discord_Stream_Notify_Bot.DataBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations
{
    [DbContext(typeof(MainDbContext))]
    partial class DBContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "7.0.14");

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.BannerChange", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("ChannelId")
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("LastChangeStreamId")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("BannerChange");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.GuildConfig", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("LogMemberStatusChannelId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("GuildConfig");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.GuildYoutubeMemberConfig", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("MemberCheckChannelId")
                        .HasColumnType("TEXT");

                    b.Property<string>("MemberCheckChannelTitle")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("MemberCheckGrantRoleId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("MemberCheckVideoId")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("GuildYoutubeMemberConfig");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.NoticeTwitcastingStreamChannel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("ChannelId")
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("DiscordChannelId")
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("StartStreamMessage")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("NoticeTwitcastingStreamChannels");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.NoticeTwitchStreamChannel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("DiscordChannelId")
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("NoticeTwitchUserId")
                        .HasColumnType("TEXT");

                    b.Property<string>("StartStreamMessage")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("NoticeTwitchStreamChannels");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.NoticeTwitterSpaceChannel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("DiscordChannelId")
                        .HasColumnType("INTEGER");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("NoticeTwitterSpaceUserId")
                        .HasColumnType("TEXT");

                    b.Property<string>("NoticeTwitterSpaceUserScreenName")
                        .HasColumnType("TEXT");

                    b.Property<string>("StratTwitterSpaceMessage")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("NoticeTwitterSpaceChannel");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.NoticeYoutubeStreamChannel", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("ChangeTimeMessage")
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.Property<string>("DeleteMessage")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("DiscordChannelId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("EndMessage")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("NewStreamMessage")
                        .HasColumnType("TEXT");

                    b.Property<string>("NewVideoMessage")
                        .HasColumnType("TEXT");

                    b.Property<string>("NoticeStreamChannelId")
                        .HasColumnType("TEXT");

                    b.Property<string>("StratMessage")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("NoticeYoutubeStreamChannel");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.RecordYoutubeChannel", b =>
                {
                    b.Property<string>("YoutubeChannelId")
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.HasKey("YoutubeChannelId");

                    b.ToTable("RecordYoutubeChannel");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.TwitcastingSpider", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("ChannelId")
                        .HasColumnType("TEXT");

                    b.Property<string>("ChannelTitle")
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsRecord")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsWarningUser")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("TwitcastingSpider");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.TwitchSpider", b =>
                {
                    b.Property<string>("UserId")
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsRecord")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsWarningUser")
                        .HasColumnType("INTEGER");

                    b.Property<string>("OfflineImageUrl")
                        .HasColumnType("TEXT");

                    b.Property<string>("ProfileImageUrl")
                        .HasColumnType("TEXT");

                    b.Property<string>("UserLogin")
                        .HasColumnType("TEXT");

                    b.Property<string>("UserName")
                        .HasColumnType("TEXT");

                    b.HasKey("UserId");

                    b.ToTable("TwitchSpider");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.TwitterSpace", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("SpaecActualStartTime")
                        .HasColumnType("TEXT");

                    b.Property<string>("SpaecId")
                        .HasColumnType("TEXT");

                    b.Property<string>("SpaecMasterPlaylistUrl")
                        .HasColumnType("TEXT");

                    b.Property<string>("SpaecTitle")
                        .HasColumnType("TEXT");

                    b.Property<string>("UserId")
                        .HasColumnType("TEXT");

                    b.Property<string>("UserName")
                        .HasColumnType("TEXT");

                    b.Property<string>("UserScreenName")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("TwitterSpace");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.TwitterSpaecSpider", b =>
                {
                    b.Property<string>("UserId")
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsRecord")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsWarningUser")
                        .HasColumnType("INTEGER");

                    b.Property<string>("UserName")
                        .HasColumnType("TEXT");

                    b.Property<string>("UserScreenName")
                        .HasColumnType("TEXT");

                    b.HasKey("UserId");

                    b.ToTable("TwitterSpaecSpider");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.YoutubeChannelNameToId", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("ChannelId")
                        .HasColumnType("TEXT");

                    b.Property<string>("ChannelName")
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("YoutubeChannelNameToId");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.YoutubeChannelOwnedType", b =>
                {
                    b.Property<string>("ChannelId")
                        .HasColumnType("TEXT");

                    b.Property<string>("ChannelTitle")
                        .HasColumnType("TEXT");

                    b.Property<int>("ChannelType")
                        .HasColumnType("INTEGER");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.HasKey("ChannelId");

                    b.ToTable("YoutubeChannelOwnedType");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.YoutubeChannelSpider", b =>
                {
                    b.Property<string>("ChannelId")
                        .HasColumnType("TEXT");

                    b.Property<string>("ChannelTitle")
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsTrustedChannel")
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("LastSubscribeTime")
                        .HasColumnType("TEXT");

                    b.HasKey("ChannelId");

                    b.ToTable("YoutubeChannelSpider");
                });

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.YoutubeMemberCheck", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("CheckYTChannelId")
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("GuildId")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsChecked")
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("LastCheckTime")
                        .HasColumnType("TEXT");

                    b.Property<ulong>("UserId")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.ToTable("YoutubeMemberCheck");
                });
#pragma warning restore 612, 618
        }
    }
}
