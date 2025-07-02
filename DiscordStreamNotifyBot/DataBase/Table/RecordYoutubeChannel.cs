using System.ComponentModel.DataAnnotations;

namespace DiscordStreamNotifyBot.DataBase.Table
{
    public class RecordYoutubeChannel
    {
        [Key]
        public string YoutubeChannelId { get; set; }
        public DateTime? DateAdded { get; set; } = DateTime.UtcNow;
    }
}