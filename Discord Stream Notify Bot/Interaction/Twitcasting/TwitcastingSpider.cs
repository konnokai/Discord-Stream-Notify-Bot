using Discord.Interactions;

namespace Discord_Stream_Notify_Bot.Interaction.Twitcasting
{
    [Group("twitcasting-spider", "Tc台爬蟲")]
    public class TwitcastingSpider : TopLevelModule<SharedService.Twitcasting.TwitcastingService>
    {
    }
}
