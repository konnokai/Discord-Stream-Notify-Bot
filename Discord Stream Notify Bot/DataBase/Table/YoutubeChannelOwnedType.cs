using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class YoutubeChannelOwnedType
    {

        [Key]
        public string ChannelId { get; set; }
        public string ChannelTitle { get; set; } = null;
        public StreamVideo.YTChannelType ChannelType { get; set; } = StreamVideo.YTChannelType.Other;
    }
}
