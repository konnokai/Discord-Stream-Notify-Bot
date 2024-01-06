namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class TwitCastingSpider : DbEntity
    {
        public ulong GuildId { get; set; }
        public string ChannelTitle { get; set; }
        public string ChannelId { get; set; }
        public bool IsWarningUser { get; set; } = false;
        public bool IsRecord { get; set; } = false;
    }
}