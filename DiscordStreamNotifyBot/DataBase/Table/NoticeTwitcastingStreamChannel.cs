namespace DiscordStreamNotifyBot.DataBase.Table
{
    public class NoticeTwitcastingStreamChannel : DbEntity
    {
        public ulong GuildId { get; set; }
        public ulong DiscordChannelId { get; set; }
        public string ScreenId { get; set; }
        public string StartStreamMessage { get; set; } = "";
    }
}