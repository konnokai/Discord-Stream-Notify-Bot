namespace DiscordStreamNotifyBot.DataBase.Table
{
    public class TwitcastingSpider : DbEntity
    {
        public ulong GuildId { get; set; }
        public string ChannelTitle { get; set; }
        public string ScreenId { get; set; }
        public string ChannelId { get; set; }
        public bool IsWarningUser { get; set; } = false;
        public bool IsRecord { get; set; } = false;
    }
}