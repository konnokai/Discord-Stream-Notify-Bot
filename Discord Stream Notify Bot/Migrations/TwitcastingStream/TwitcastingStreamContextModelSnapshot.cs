﻿// <auto-generated />
using System;
using Discord_Stream_Notify_Bot.DataBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Discord_Stream_Notify_Bot.Migrations.TwitCastingStream
{
    [DbContext(typeof(TwitCastingStreamContext))]
    partial class TwitCastingStreamContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "6.0.5");

            modelBuilder.Entity("Discord_Stream_Notify_Bot.DataBase.Table.TwitCastingStream", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Category")
                        .HasColumnType("TEXT");

                    b.Property<string>("ChannelId")
                        .HasColumnType("TEXT");

                    b.Property<string>("ChannelTitle")
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("DateAdded")
                        .HasColumnType("TEXT");

                    b.Property<int>("StreamId")
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("StreamStartAt")
                        .HasColumnType("TEXT");

                    b.Property<string>("StreamSubTitle")
                        .HasColumnType("TEXT");

                    b.Property<string>("StreamTitle")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("TwitCastingStreams");
                });
#pragma warning restore 612, 618
        }
    }
}
