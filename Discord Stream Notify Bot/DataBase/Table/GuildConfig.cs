namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class GuildConfig : DbEntity
    {
        public ulong GuildId { get; set; }
        public ulong NoticeChannelId { get; set; } = 0;
    }
}
