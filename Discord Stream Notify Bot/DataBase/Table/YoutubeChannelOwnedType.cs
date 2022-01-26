using Discord_Stream_Notify_Bot.Command.Youtube.Service;
using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class YoutubeChannelOwnedType
    {

        [Key]
        public string ChannelId { get; set; }
        public string ChannelTitle { get; set; } = null;
        public YoutubeStreamService.ChannelType ChannelType { get; set; } = YoutubeStreamService.ChannelType.Other;
    }
}
