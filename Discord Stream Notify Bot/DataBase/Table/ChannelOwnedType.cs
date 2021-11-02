using Discord_Stream_Notify_Bot.Command.Stream.Service;
using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class ChannelOwnedType
    {

        [Key]
        public string ChannelId { get; set; }
        public string ChannelTitle { get; set; } = null;
        public StreamService.ChannelType ChannelType { get; set; } = StreamService.ChannelType.Other;
    }
}
