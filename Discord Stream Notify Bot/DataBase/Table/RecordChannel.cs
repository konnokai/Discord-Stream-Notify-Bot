using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    class RecordChannel 
    {
        [Key]
        public string ChannelId { get; set; }
    }
}