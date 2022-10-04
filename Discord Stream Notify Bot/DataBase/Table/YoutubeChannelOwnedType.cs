﻿using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class YoutubeChannelOwnedType
    {

        [Key]
        public string ChannelId { get; set; }
        public string ChannelTitle { get; set; } = null;
        public Video.YTChannelType ChannelType { get; set; } = Video.YTChannelType.Other;
    }
}
