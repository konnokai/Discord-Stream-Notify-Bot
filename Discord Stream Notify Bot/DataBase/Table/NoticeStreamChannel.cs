namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class NoticeStreamChannel : DbEntity
    {
        public ulong GuildID { get; set; }
        public ulong ChannelID { get; set; }
        public string NoticeStreamChannelID { get; set; }
    }
}
