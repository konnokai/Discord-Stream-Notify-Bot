using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class TwitterSpaceSpider
    {
        [Key]
        public string UserId { get; set; }
        public string UserScreenName { get; set; } = null;
        public string UserName { get; set; } = null;
        public ulong GuildId { get; set; }
        public bool IsWarningUser { get; set; } = false;
        public bool IsRecord { get; set; } = true;
        public DateTime? DateAdded { get; set; } = DateTime.UtcNow;
    }
}
