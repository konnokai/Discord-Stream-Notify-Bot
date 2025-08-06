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
                .AddField("�������A", "�w�R������")
                .AddField("�Ʃw�}�x�ɶ�", video.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());
        }

        public static EmbedBuilder CreateStreamStarted(TableVideo video)
        {
            return new EmbedBuilder()
                .WithTitle(video.VideoTitle)
                .WithOkColor()
                .WithDescription(Format.Url(video.ChannelTitle, $"https://www.youtube.com/channel/{video.ChannelId}"))
                .WithImageUrl($"https://i.ytimg.com/vi/{video.VideoId}/maxresdefault.jpg")
                .WithUrl($"https://www.youtube.com/watch?v={video.VideoId}")
                .AddField("�������A", "�}�x��")
                .AddField("�Ʃw�}�x�ɶ�", video.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown());
        }

        public static EmbedBuilder CreateStreamTimeChanged(TableVideo video, DateTime newStartTime)
        {
            return new EmbedBuilder()
                .WithErrorColor()
                .WithTitle(video.VideoTitle)
                .WithDescription(Format.Url(video.ChannelTitle, $"https://www.youtube.com/channel/{video.ChannelId}"))
                .WithImageUrl($"https://i.ytimg.com/vi/{video.VideoId}/maxresdefault.jpg")
                .WithUrl($"https://www.youtube.com/watch?v={video.VideoId}")
                .AddField("�������A", "�|���}�x(�w���ɶ�)")
                .AddField("�Ʃw�}�x�ɶ�", video.ScheduledStartTime.ConvertDateTimeToDiscordMarkdown())
                .AddField("���}�x�ɶ�", newStartTime.ConvertDateTimeToDiscordMarkdown());
        }
    }
}
