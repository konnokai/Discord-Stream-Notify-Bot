using System.ComponentModel.DataAnnotations;
using System;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class YoutubeChannelSpider
    {
        [Key]
        public string ChannelId { get; set; }
        public string ChannelTitle { get; set; } = null;
        public ulong GuildId { get; set; }
        public bool IsVTuberChannel { get; set; } = false;
        public bool IsWarningChannel { get; set; } = false;
        public DateTime LastSubscribeTime { get; set; } = DateTime.MinValue;
    }
}
