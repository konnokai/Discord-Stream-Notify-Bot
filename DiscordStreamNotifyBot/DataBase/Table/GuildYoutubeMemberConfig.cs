namespace DiscordStreamNotifyBot.DataBase.Table
{
    public class GuildYoutubeMemberConfig : DbEntity
    {
        public ulong GuildId { get; set; }
        public string MemberCheckChannelId { get; set; } = "";
        public string MemberCheckChannelTitle { get; set; } = "";
        public string MemberCheckVideoId { get; set; } = "-";
        public ulong MemberCheckGrantRoleId { get; set; } = 0;
    }
}
