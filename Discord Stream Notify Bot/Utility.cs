using System;
using System.Collections.Generic;
using System.Linq;

namespace Discord_Stream_Notify_Bot
{
    public static class Utility
    {
        //static Regex videoIdRegex = new Regex(@"youtube_(?'ChannelId'[\w\-]{24})_(?'Date'[\d]{8})_(?'Time'[\d]{6})_(?'VideoId'[\w\-]{11}).mp4.part");

        public static List<string> GetNowRecordStreamList()
        {
            try
            {
                var set = Program.RedisDb.SetMembers("youtube.nowRecord");
                return set.Select((x) => x.ToString()).ToList();
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
                using (var db = DataBase.DBContext.GetDbContext())
                {
                    return db.HoloStreamVideo.Count() + db.NijisanjiStreamVideo.Count() + db.OtherStreamVideo.Count();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{ex.Message}\n{ex.StackTrace}");
                return 0;
            }
        }
    }
}
