﻿using Discord_Stream_Notify_Bot.DataBase.Table;
using Microsoft.EntityFrameworkCore;

namespace Discord_Stream_Notify_Bot.DataBase
{
    public class MainDbContext : DbContext
    {
        public DbSet<BannerChange> BannerChange { get; set; }
        public DbSet<GuildConfig> GuildConfig { get; set; }
        public DbSet<GuildYoutubeMemberConfig> GuildYoutubeMemberConfig { get; set; }
        public DbSet<NoticeTwitCastingStreamChannel> NoticeTwitCastingStreamChannels { get; set; }
        public DbSet<NoticeTwitchStreamChannel> NoticeTwitchStreamChannels { get; set; }
        public DbSet<NoticeTwitterSpaceChannel> NoticeTwitterSpaceChannel { get; set; }
        public DbSet<NoticeYoutubeStreamChannel> NoticeYoutubeStreamChannel { get; set; }
        public DbSet<RecordYoutubeChannel> RecordYoutubeChannel { get; set; }
        public DbSet<TwitCastingSpider> TwitCastingSpider { get; set; }
        public DbSet<TwitchSpider> TwitchSpider { get; set; }
        public DbSet<TwitterSpace> TwitterSpace { get; set; }
        public DbSet<TwitterSpaecSpider> TwitterSpaecSpider { get; set; }
        public DbSet<YoutubeChannelNameToId> YoutubeChannelNameToId { get; set; }
        public DbSet<YoutubeChannelOwnedType> YoutubeChannelOwnedType { get; set; }
        public DbSet<YoutubeChannelSpider> YoutubeChannelSpider { get; set; }
        public DbSet<YoutubeMemberAccessToken> YoutubeMemberAccessToken { get; set; }
        public DbSet<YoutubeMemberCheck> YoutubeMemberCheck { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={Program.GetDataFilePath("Database.db")}")
#if DEBUG || DEBUG_DONTREGISTERCOMMAND
            //.LogTo((act) => System.IO.File.AppendAllText("DbTrackerLog.txt", act), Microsoft.Extensions.Logging.LogLevel.Information)
#endif
            .EnableSensitiveDataLogging();

        public static MainDbContext GetDbContext()
        {
            var context = new MainDbContext();
            context.Database.SetCommandTimeout(60);
            var conn = context.Database.GetDbConnection();
            conn.Open();
            using (var com = conn.CreateCommand())
            {
                com.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=OFF";
                com.ExecuteNonQuery();
            }
            return context;
        }
    }
}
