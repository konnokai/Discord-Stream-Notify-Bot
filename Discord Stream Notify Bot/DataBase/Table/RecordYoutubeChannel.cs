using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class RecordYoutubeChannel 
    {
        [Key]
        public string YoutubeChannelId { get; set; }
    }
}