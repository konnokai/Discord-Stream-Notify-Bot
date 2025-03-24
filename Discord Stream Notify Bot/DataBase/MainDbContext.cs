using Discord_Stream_Notify_Bot.DataBase.Table;

namespace Discord_Stream_Notify_Bot.DataBase
{
    public class MainDbContext : DbContext
    {
        private readonly string _connectionString;

        public MainDbContext(string connectionString
            // 要新增 Migration 的時候再把下面的連線字串註解拿掉
            //= "Server=localhost;Port=3306;User Id=stream_bot;Password=Ch@nge_Me;Database=discord_stream_bot"
            )
        {
            _connectionString = connectionString;
        }

        public DbSet<BannerChange> BannerChange { get; set; }
        public DbSet<GuildConfig> GuildConfig { get; set; }
        public DbSet<GuildYoutubeMemberConfig> GuildYoutubeMemberConfig { get; set; }
        public DbSet<NoticeTwitcastingStreamChannel> NoticeTwitcastingStreamChannels { get; set; }
        public DbSet<NoticeTwitchStreamChannel> NoticeTwitchStreamChannels { get; set; }
        public DbSet<NoticeTwitterSpaceChannel> NoticeTwitterSpaceChannel { get; set; }
        public DbSet<NoticeYoutubeStreamChannel> NoticeYoutubeStreamChannel { get; set; }
        public DbSet<RecordYoutubeChannel> RecordYoutubeChannel { get; set; }
        public DbSet<TwitcastingSpider> TwitcastingSpider { get; set; }
        public DbSet<TwitchSpider> TwitchSpider { get; set; }
        public DbSet<TwitterSpace> TwitterSpace { get; set; }
        public DbSet<TwitterSpaceSpider> TwitterSpaceSpider { get; set; }
        public DbSet<YoutubeChannelNameToId> YoutubeChannelNameToId { get; set; }
        public DbSet<YoutubeChannelOwnedType> YoutubeChannelOwnedType { get; set; }
        public DbSet<YoutubeChannelSpider> YoutubeChannelSpider { get; set; }
        public DbSet<YoutubeMemberAccessToken> YoutubeMemberAccessToken { get; set; }
        public DbSet<YoutubeMemberCheck> YoutubeMemberCheck { get; set; }

        #region Video
        public DbSet<HoloVideos> HoloVideos { get; set; }
        public DbSet<NijisanjiVideos> NijisanjiVideos { get; set; }
        public DbSet<OtherVideos> OtherVideos { get; set; }
        public DbSet<NonApprovedVideos> NonApprovedVideos { get; set; }
        public DbSet<TwitcastingStream> TwitcastingStreams { get; set; }
        public DbSet<TwitchStream> TwitchStreams { get; set; }
        #endregion

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            optionsBuilder
                .UseMySql(_connectionString, ServerVersion.AutoDetect(_connectionString))
                .UseSnakeCaseNamingConvention();
        }

        public bool UpdateAndSave(Table.Video video)
        {
            Table.Video updatedVideo = video switch
            {
                { ChannelType: Table.Video.YTChannelType.Holo } => video as HoloVideos,
                { ChannelType: Table.Video.YTChannelType.Nijisanji } => video as NijisanjiVideos,
                { ChannelType: Table.Video.YTChannelType.Other } => video as OtherVideos,
                { ChannelType: Table.Video.YTChannelType.NonApproved } => video as NonApprovedVideos,
                _ => null
            };

            if (updatedVideo == null)
            {
                return false;
            }

            Update(updatedVideo);
            var saveTime = DateTime.Now;
            bool saveFailed;
            int retryCount = 0;
            const int maxRetryCount = 5;

            do
            {
                saveFailed = false;
                try
                {
                    SaveChanges();
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    saveFailed = true;
                    retryCount++;
                    foreach (var item in ex.Entries)
                    {
                        try
                        {
                            item.Reload();
                        }
                        catch (Exception ex2)
                        {
                            Log.Error($"VideoContext-SaveChanges-Reload");
                            Log.Error(item.DebugView.ToString());
                            Log.Error(ex2.ToString());
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"VideoContext-SaveChanges: {ex}");
                    Log.Error(ChangeTracker.DebugView.LongView);
                }
            } while (saveFailed && retryCount < maxRetryCount && DateTime.Now.Subtract(saveTime) <= TimeSpan.FromMinutes(1));

            return retryCount >= maxRetryCount || DateTime.Now.Subtract(saveTime) >= TimeSpan.FromMinutes(1);
        }
    }
}
