namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class BannerChange : DbEntity
    {
        public ulong GuildId { get; set; }
        public string ChannelId { get; set; }
        public string LastChangeStreamId { get; set; } = null;
    }
}
