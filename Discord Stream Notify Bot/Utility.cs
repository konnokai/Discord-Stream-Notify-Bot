using Discord_Stream_Notify_Bot.Command;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    }
}
