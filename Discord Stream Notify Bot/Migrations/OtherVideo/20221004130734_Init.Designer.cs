﻿// <auto-generated />
using System;
using Discord_Stream_Notify_Bot.DataBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations.OtherVideo
{
    [DbContext(typeof(OtherVideoContext))]
    [Migration("20221004130734_Init")]
    partial class Init
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "6.0.5");

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.Video", b =>
                {
                    b.Property<string>("VideoId")
                        .HasColumnType("TEXT");

                    b.Property<string>("ChannelId")
                        .HasColumnType("TEXT");

                    b.Property<string>("ChannelTitle")
                        .HasColumnType("TEXT");

                    b.Property<int>("ChannelType")
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("ScheduledStartTime")
                        .HasColumnType("TEXT");

                    b.Property<string>("VideoTitle")
                        .HasColumnType("TEXT");

                    b.HasKey("VideoId");

                    b.ToTable("Video");
                });
#pragma warning restore 612, 618
        }
    }
}
