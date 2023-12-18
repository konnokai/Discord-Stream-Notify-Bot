namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class TwitchStream : DbEntity
    {
        public string StreamId { get; set; }
        public string StreamTitle { get; set; }
        public DateTime StreamStartAt { get; set; }
        public string UserLogin { get; set; }
        public string UserName { get; set; }
        public string GameName { get; set; } = "";
        public string ThumbnailUrl { get; set; } = "";
    }
}
