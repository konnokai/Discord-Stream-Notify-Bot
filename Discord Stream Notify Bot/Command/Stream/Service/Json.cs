using System;
using System.Collections.Generic;
using System.Text;

namespace Discord_Stream_Notify_Bot.Command.Stream.Service
{
    public class NijisanjiJson
    {
        public string status { get; set; }
        public data data { get; set; }
    }

    public class data
    {
        public List<events> events { get; set; }

    }

    public class events
    {
        public string url { get; set; }
        public DateTime start_date { get; set; }
    }
}
