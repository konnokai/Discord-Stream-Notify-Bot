using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class TwitchSpider
    {
        [Key]
        public string UserId { get; set; }
        public string UserLogin { get; set; }
        public string UserName { get; set; }
        public string ProfileImageUrl { get; set; } = "";
        public string OfflineImageUrl { get; set; } = "";
        public ulong GuildId { get; set; }
        public bool IsWarningUser { get; set; } = false;
        public bool IsRecord { get; set; } = false;
        public DateTime? DateAdded { get; set; } = DateTime.UtcNow;
    }
}
