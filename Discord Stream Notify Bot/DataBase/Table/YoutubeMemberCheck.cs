using System;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class YoutubeMemberCheck : DbEntity
    {
        public enum CheckStatus { ExpiredOrNoMember = -1, NotYetStarted = 0, Success = 1 }

        public ulong UserId { get; set; }
        public DateTime LastCheckTime { get; set; } = DateTime.Now;
        public CheckStatus LastCheckStatus { get; set; } = CheckStatus.NotYetStarted;
    }
}
