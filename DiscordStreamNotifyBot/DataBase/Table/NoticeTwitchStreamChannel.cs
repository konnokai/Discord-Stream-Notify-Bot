namespace DiscordStreamNotifyBot.DataBase.Table
{
    public class NoticeTwitchStreamChannel : DbEntity
    {
        public ulong GuildId { get; set; }
        public ulong DiscordChannelId { get; set; }
        public string NoticeTwitchUserId { get; set; }
        public string StartStreamMessage { get; set; } = "";
        public string EndStreamMessage { get; set; } = "";
        public string ChangeStreamDataMessage { get; set; } = "";
    }
}
