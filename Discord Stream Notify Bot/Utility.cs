using System.Diagnostics;

namespace Discord_Stream_Notify_Bot
{
    public static class Utility
    {
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
