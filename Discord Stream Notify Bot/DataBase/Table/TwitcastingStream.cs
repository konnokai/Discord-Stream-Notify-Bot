namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class TwitCastingStream : DbEntity
    {
        public string ChannelId { get; set; }
        public string ChannelTitle { get; set; }
        public int StreamId { get; set; }
        public string StreamTitle { get; set; } = "";
        public string StreamSubTitle { get; set; } = "";
        public string Category { get; set; } = "";
        public DateTime StreamStartAt { get; set; } = DateTime.Now;
    }
}
