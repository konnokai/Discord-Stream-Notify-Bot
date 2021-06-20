using Discord_Stream_Notify_Bot.Command.Stream.Service;
using System;
using System.ComponentModel.DataAnnotations;

namespace Discord_Stream_Notify_Bot.DataBase.Table
{
    public class StreamVideo
    {
        public string ChannelId { get; set; }
        public string ChannelTitle { get; set; }
        [Key]
        public string VideoId { get; set; }
        public string VideoTitle { get; set; }
        public DateTime ScheduledStartTime { get; set; }
        public StreamService.ChannelType ChannelType { get; set; }

        public HoloStreamVideo ConvertToHoloStreamVideo() =>
            new HoloStreamVideo() { ChannelId = ChannelId, ChannelTitle = ChannelTitle, VideoId = VideoId, VideoTitle = VideoTitle, ChannelType = ChannelType, ScheduledStartTime = ScheduledStartTime };
        public NijisanjiStreamVideo ConvertToNijisanjiStreamVideo() =>
            new NijisanjiStreamVideo() { ChannelId = ChannelId, ChannelTitle = ChannelTitle, VideoId = VideoId, VideoTitle = VideoTitle, ChannelType = ChannelType, ScheduledStartTime = ScheduledStartTime };
        public OtherStreamVideo ConvertToOtherStreamVideo() =>
            new OtherStreamVideo() { ChannelId = ChannelId, ChannelTitle = ChannelTitle, VideoId = VideoId, VideoTitle = VideoTitle, ChannelType = ChannelType, ScheduledStartTime = ScheduledStartTime };

    }
}
