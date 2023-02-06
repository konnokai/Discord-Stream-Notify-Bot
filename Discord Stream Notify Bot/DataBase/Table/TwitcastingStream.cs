namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class TwitcastingStream : DbEntity
    {
        public string ChannelId { get; set; }
        public string ChannelName { get; set; }
        public int StreamId { get; set; }
        public string StreamTitle { get; set; }
        public DateTime StreamDateTime { get; set; } = DateTime.Now;
    }
}
