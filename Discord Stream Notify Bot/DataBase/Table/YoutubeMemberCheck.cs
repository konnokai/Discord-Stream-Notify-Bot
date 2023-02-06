namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class YoutubeMemberCheck : DbEntity
    {
        public ulong GuildId { get; set; }
        public ulong UserId { get; set; }
        public string CheckYTChannelId { get; set; }
        public DateTime LastCheckTime { get; set; } = DateTime.Now;
        public bool IsChecked { get; set; } = false;
    }
}
