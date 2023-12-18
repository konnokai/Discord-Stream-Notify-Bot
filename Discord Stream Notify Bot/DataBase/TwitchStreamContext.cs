using Discord_Stream_Notify_Bot.DataBase.Table;
using Microsoft.EntityFrameworkCore;

namespace Discord_Stream_Notify_Bot.DataBase
{
    public class TwitchStreamContext : DbContext
    {
        public DbSet<TwitchStream> TwitchStreams { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={Program.GetDataFilePath("TwitchStreamDb.db")}")
#if DEBUG || DEBUG_DONTREGISTERCOMMAND
            //.LogTo((act) => System.IO.File.AppendAllText("TwitchVideoDbTrackerLog.txt", act), Microsoft.Extensions.Logging.LogLevel.Information)
#endif
            .EnableSensitiveDataLogging();

        public static TwitchStreamContext GetDbContext()
        {
            var context = new TwitchStreamContext();
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
