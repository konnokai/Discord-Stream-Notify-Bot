namespace DiscordStreamNotifyBot.DataBase.Table
{
    public class YoutubeChannelNameToId : DbEntity
    {
        public string ChannelName { get; set; }
        public string ChannelId { get; set; }
    }
}
