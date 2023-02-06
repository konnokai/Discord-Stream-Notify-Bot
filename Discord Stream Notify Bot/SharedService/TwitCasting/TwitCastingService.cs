using Discord_Stream_Notify_Bot.Interaction;

namespace Discord_Stream_Notify_Bot.SharedService.TwitCasting
{
    public class TwitCastingService : IInteractionService
    {
        private readonly DiscordSocketClient _client;

        public TwitCastingService(DiscordSocketClient client)
        {
            _client = client;
        }
    }
}
