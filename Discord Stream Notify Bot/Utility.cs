using System.Diagnostics;

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
                return Program.RedisDb.SetMembers("youtube.nowRecord").Select((x) => x.ToString()).ToList();
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify().ToString());
                return new List<string>();
            }
        }

        public static int GetDbStreamCount()
        {
            try
            {
                int total = 0;

                using (var db = DataBase.HoloVideoContext.GetDbContext())
                    total += db.Video.Count();
                using (var db = DataBase.NijisanjiVideoContext.GetDbContext())
                    total += db.Video.Count();
                using (var db = DataBase.OtherVideoContext.GetDbContext())
                    total += db.Video.Count();

                return total;
            }
            catch (Exception ex)
            {
                Log.Error($"{ex}");
                return 0;
            }
        }

        public static bool OfficialGuildContains(ulong guildId) =>
            OfficialGuildList.Contains(guildId);
    }
}
