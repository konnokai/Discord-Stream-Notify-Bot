using System.ComponentModel.DataAnnotations;

namespace DiscordStreamNotifyBot.DataBase.Table
{
    public class YoutubeMemberAccessToken
    {
        [Key]
        public ulong DiscordUserId { get; set; }
        public string EncryptedAccessToken { get; set; }
        public DateTime? DateAdded { get; set; } = DateTime.Now;
    }
}
