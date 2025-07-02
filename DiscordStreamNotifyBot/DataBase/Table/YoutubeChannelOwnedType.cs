using System.ComponentModel.DataAnnotations;

namespace DiscordStreamNotifyBot.DataBase.Table
{
    public class YoutubeChannelOwnedType
    {

        [Key]
        public string ChannelId { get; set; }
        public string ChannelTitle { get; set; } = null;
        public Video.YTChannelType ChannelType { get; set; } = Video.YTChannelType.Other;
        public DateTime? DateAdded { get; set; } = DateTime.UtcNow;
    }
}
