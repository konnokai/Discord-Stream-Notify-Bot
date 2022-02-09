using Discord_Stream_Notify_Bot.Interaction;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Discord_Stream_Notify_Bot
{
    public static class Utility
    {
        public static Dictionary<string,int> GetNowRecordStreamList()
        {
            Dictionary<string, int> streamList = new Dictionary<string, int>();

            foreach (var item in Process.GetProcessesByName("streamlink"))
            {
                try
                {
                    string cmdLine = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? item.GetCommandLine() : File.ReadAllText($"/proc/{item.Id}/cmdline");
                    string temp = cmdLine.Split(".ts", StringSplitOptions.RemoveEmptyEntries)[0];
                    streamList.Add(temp.Substring(temp.Length - 11, 11), item.Id);
                }
                catch { }
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
                Log.Error($"{ex.Message}\r\n{ex.StackTrace}");
                return 0;
            }
        }
    }
}
