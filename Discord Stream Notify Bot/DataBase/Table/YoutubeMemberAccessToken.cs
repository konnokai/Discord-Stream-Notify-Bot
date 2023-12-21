using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class YoutubeMemberAccessToken
    {
        [Key]
        public ulong DiscordUserId { get; set; }
        public string EncryptedAccessToken { get; set; }
        public DateTime? DateAdded { get; set; } = DateTime.Now;
    }
}
