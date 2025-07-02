namespace DiscordStreamNotifyBot.DataBase.Table
{
    public class NoticeTwitterSpaceChannel : DbEntity
    {
        public ulong GuildId { get; set; }
        public ulong DiscordChannelId { get; set; }
        public string NoticeTwitterSpaceUserId { get; set; }
        public string NoticeTwitterSpaceUserScreenName { get; set; }
        public string StratTwitterSpaceMessage { get; set; } = "";
    }
}
