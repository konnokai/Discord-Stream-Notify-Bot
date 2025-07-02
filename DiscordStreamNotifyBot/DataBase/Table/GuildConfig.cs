namespace DiscordStreamNotifyBot.DataBase.Table
{
    public class GuildConfig : DbEntity
    {
        public ulong GuildId { get; set; }
        public ulong LogMemberStatusChannelId { get; set; } = 0;
        public ulong NoticeChannelId { get; set; } = 0;
        public uint MaxYouTubeSpiderCount { get; set; } = 3;
        public uint MaxYouTubeMemberCheckCount { get; set; } = 5;
        public uint MaxTwitcastingSpiderCount { get; set; } = 3;
        public uint MaxTwitterSpaceSpiderCount { get; set; } = 3;
        public uint MaxTwitchSpiderCount { get; set; } = 3;
    }
}
