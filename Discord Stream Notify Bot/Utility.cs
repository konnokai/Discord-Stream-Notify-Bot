using Discord_Stream_Notify_Bot.Interaction;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Discord_Stream_Notify_Bot
{
    public static class Utility
    {
        static Regex videoIdRegex = new Regex(@"youtube_(?'ChannelId'[\w\-]{24})_(?'Date'[\d]{8})_(?'Time'[\d]{6})_(?'VideoId'[\w\-]{11}).mp4.part");

        public static Dictionary<string, int> GetNowRecordStreamList()
        {
            Dictionary<string, int> streamList = new Dictionary<string, int>();

            foreach (var item in Process.GetProcessesByName("ffmpeg"))
            {
                try
                {
                    string cmdLine = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? item.GetCommandLine() : File.ReadAllText($"/proc/{item.Id}/cmdline");
                    var match = videoIdRegex.Match(cmdLine);
                    if (match.Success)
                        streamList.Add(match.Groups["VideoId"].Value, item.Id);
                }
                catch (Exception ex) { Log.Error(ex.ToString()); }
            }

            return streamList;
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
