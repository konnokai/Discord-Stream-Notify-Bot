namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class NoticeTwitchStreamChannel : DbEntity
    {
        public ulong GuildId { get; set; }
        public ulong DiscordChannelId { get; set; }
        public string NoticeTwitchUserId { get; set; }
        public string StartStreamMessage { get; set; } = "";
    }
}
