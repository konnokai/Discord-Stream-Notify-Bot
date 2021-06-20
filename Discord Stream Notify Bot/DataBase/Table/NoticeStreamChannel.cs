namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class NoticeStreamChannel : DbEntity
    {
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
        public string NoticeStreamChannelId { get; set; }
        public string NewStreamMessage { get; set; } = "";
        public string NewVideoMessage { get; set; } = "";
        public string StratMessage { get; set; } = "";
        public string EndMessage { get; set; } = "";
        public string ChangeTimeMessage { get; set; } = "";
        public string DeleteMessage { get; set; } = "";
    }
}
