using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Discord_Stream_Notify_Bot
{
    public static class Utility
    {
        public const string PatreonUrl = "https://patreon.com/konnokai";
        public const string PaypalUrl = "https://paypal.me/jun112561";

        //static Regex videoIdRegex = new Regex(@"youtube_(?'ChannelId'[\w\-]{24})_(?'Date'[\d]{8})_(?'Time'[\d]{6})_(?'VideoId'[\w\-]{11}).mp4.part");
        public static string RedisKey { get; set; } = "";
        public static HashSet<ulong> OfficialGuildList { get; set; } = new HashSet<ulong>();

        public static List<string> GetNowRecordStreamList()
        {
            try
            {
                return Bot.RedisDb.SetMembers("youtube.nowRecord").Select((x) => x.ToString()).ToList();
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
                return new List<string>();
            }
        }

        public static int GetDbStreamCount()
        {
            try
            {
                int total = 0;

                using var db = Bot.DbService.GetDbContext();
                total += db.HoloVideo.AsNoTracking().Count();
                total += db.NijisanjiVideo.AsNoTracking().Count();
                total += db.OtherVideo.AsNoTracking().Count();

                return total;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "Utility-GetDbStreamCount");
                return 0;
            }
        }

        public static bool OfficialGuildContains(ulong guildId) =>
            OfficialGuildList.Contains(guildId);

        public static string GetDataFilePath(string fileName)
            => $"{AppDomain.CurrentDomain.BaseDirectory}Data{GetPlatformSlash()}{fileName}";

        public static string GetPlatformSlash()
            => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\\" : "/";
    }
}
