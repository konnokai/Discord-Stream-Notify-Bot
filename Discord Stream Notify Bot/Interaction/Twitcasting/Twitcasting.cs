using Discord.Interactions;

namespace Discord_Stream_Notify_Bot.Interaction.Twitcasting
{
    [Group("twitcasting", "Tc台通知")]
    public class Twitcasting : TopLevelModule<SharedService.Twitcasting.TwitcastingService>
    {
    }
}