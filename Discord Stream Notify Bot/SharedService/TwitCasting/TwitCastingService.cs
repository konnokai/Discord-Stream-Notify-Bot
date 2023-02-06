using Discord_Stream_Notify_Bot.HttpClients;
using Discord_Stream_Notify_Bot.Interaction;

namespace Discord_Stream_Notify_Bot.SharedService.Twitcasting
{
    public class TwitcastingService : IInteractionService
    {
        private readonly DiscordSocketClient _client;
        private readonly TwitcastingClient _twitcastingClient;

        public TwitcastingService(DiscordSocketClient client, TwitcastingClient twitcastingClient)
        {
            _client = client;
            _twitcastingClient = twitcastingClient;
        }
    }
}
