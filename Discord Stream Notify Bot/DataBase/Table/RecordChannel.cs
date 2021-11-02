using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class RecordChannel 
    {
        [Key]
        public string ChannelId { get; set; }
    }
}