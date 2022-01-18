using System;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class MemberAccessToken : DbEntity
    {
        public string DiscordUserId { get; set; }
        public string GoogleAccessToken { get; set; }
        public string GoogleRefrechToken { get; set; }
        public DateTime GoogleExpiresIn { get; set; }
        public string YoutubeChannelId { get; set; }
    }
}
