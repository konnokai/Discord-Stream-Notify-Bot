namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class NoticeTwitcastingStreamChannel : DbEntity
    {
        public ulong GuildId { get; set; }
        public ulong DiscordChannelId { get; set; }
        public string ChannelId { get; set; }
        public string StartStreamMessage { get; set; } = "";
    }
}