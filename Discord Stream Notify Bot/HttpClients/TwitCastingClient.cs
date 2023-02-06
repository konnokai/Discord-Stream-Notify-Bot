using System.Net.Http;

namespace Discord_Stream_Notify_Bot.HttpClients
{
    public class TwitCastingClient
    {
        private readonly HttpClient _httpClient;
        private readonly BotConfig _botConfig;

        public TwitCastingClient(HttpClient httpClient, BotConfig botConfig)
        {
            _httpClient = httpClient;
            _botConfig = botConfig;
        }
    }
}
