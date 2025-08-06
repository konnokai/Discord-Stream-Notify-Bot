using DiscordStreamNotifyBot.Interaction;
using TableVideo = DiscordStreamNotifyBot.DataBase.Table.Video;

namespace DiscordStreamNotifyBot.SharedService.Youtube
{
    public static class EmbedBuilderFactory
    {
        public static EmbedBuilder CreateStreamDeleted(TableVideo video)
        {
            return new EmbedBuilder()
                .WithErrorColor()
                .WithTitle(video.VideoTitle)
                .WithDescription(Format.Url(video.ChannelTitle, $"https://www.youtube.com/channel/{video.ChannelId}"))
                .WithUrl($"https://www.youtube.com/watch?v={video.VideoId}")
                .AddField("直播狀態", "已刪除直播")
                .AddField("排定開台時間", video.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());
        }

        public static EmbedBuilder CreateStreamStarted(TableVideo video)
        {
            return new EmbedBuilder()
                .WithTitle(video.VideoTitle)
                .WithOkColor()
                .WithDescription(Format.Url(video.ChannelTitle, $"https://www.youtube.com/channel/{video.ChannelId}"))
                .WithImageUrl($"https://i.ytimg.com/vi/{video.VideoId}/maxresdefault.jpg")
                .WithUrl($"https://www.youtube.com/watch?v={video.VideoId}")
                .AddField("直播狀態", "開台中")
                .AddField("排定開台時間", video.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());
        }

        public static EmbedBuilder CreateStreamTimeChanged(TableVideo video, DateTime newStartTime)
        {
            return new EmbedBuilder()
                .WithErrorColor()
                .WithTitle(video.VideoTitle)
                .WithDescription(Format.Url(video.ChannelTitle, $"https://www.youtube.com/channel/{video.ChannelId}"))
                .WithImageUrl($"https://i.ytimg.com/vi/{video.VideoId}/maxresdefault.jpg")
                .WithUrl($"https://www.youtube.com/watch?v={video.VideoId}")
                .AddField("直播狀態", "尚未開台(已更改時間)")
                .AddField("排定開台時間", video.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown())
                .AddField("更改開台時間", newStartTime.ConvertDateTimeToDiscordMarkdown());
        }
    }
}
