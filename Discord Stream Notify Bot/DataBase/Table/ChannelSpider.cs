using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class ChannelSpider
    {
        [Key]
        public string ChannelId { get; set; }
        public string ChannelTitle { get; set; } = null;
        public ulong GuildId { get; set; }
        public bool IsWarningChannel { get; set; } = false;
    }
}
