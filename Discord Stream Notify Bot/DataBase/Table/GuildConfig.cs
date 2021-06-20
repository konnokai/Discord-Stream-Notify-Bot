using System.Collections.Generic;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class GuildConfig : DbEntity
    {
        public ulong GuildId { get; set; }
        public string MemberCheckChannelId { get; set; } = null;
        public string MemberCheckVideoId { get; set; } = null;
        public ulong MemberCheckGrantRoleId { get; set; } = 0;
        public List<YoutubeMemberCheck> MemberCheck { get; set; } = new();
    }
}
