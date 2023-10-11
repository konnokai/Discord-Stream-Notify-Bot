namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class YoutubeChannelNameToId : DbEntity
    {
        public string ChannelName { get; set; }
        public string ChannelId { get; set; }
    }
}
