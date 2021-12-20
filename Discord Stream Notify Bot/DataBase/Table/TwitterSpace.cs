using System;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class TwitterSpace : DbEntity
    {
        public string UserId { get; set; }
        public string UserScreenName { get; set; }
        public string UserName { get; set; }
        public string SpaecId { get; set; }
        public string SpaecTitle { get; set; }
        public DateTime SpaecActualStartTime { get; set; }
        public string SpaecMasterPlaylistUrl { get; set; }
    }
}
