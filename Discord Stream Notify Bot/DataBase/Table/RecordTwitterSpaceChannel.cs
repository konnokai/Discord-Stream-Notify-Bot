using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class RecordTwitterSpaceChannel
    {
        [Key]
        public string TwitterUserId { get; set; }
        public string TwitterScreenName { get; set; }
    }
}