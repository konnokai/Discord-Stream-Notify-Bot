using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class YoutubeChannelSpider
    {
        [Key]
        public string ChannelId { get; set; }
        public string ChannelTitle { get; set; } = null;
        public ulong GuildId { get; set; }
        public bool IsTrustedChannel { get; set; } = false;
        public DateTime LastSubscribeTime { get; set; } = DateTime.MinValue;
        public DateTime? DateAdded { get; set; } = DateTime.UtcNow;
    }
}
