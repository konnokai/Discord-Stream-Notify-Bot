using Discord.WebSocket;
using Newtonsoft.Json;
using System.Net.Http;
using System.Text;

namespace Discord_Stream_Notify_Bot.HttpClients
{
    public class DiscordWebhookClient
    {
        public HttpClient Client { get; private set; }

        DiscordSocketClient _client;
        BotConfig _botConfig;

        class Message
        {
            public string username { get; set; }
            public string content { get; set; }
            public string avatar_url { get; set; }
        }

        public DiscordWebhookClient(HttpClient httpClient, DiscordSocketClient client, BotConfig botConfig)
        {
            httpClient.DefaultRequestHeaders.Add("UserAgent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/96.0.4664.45 Safari/537.36");
            Client = httpClient;
            _client = client;
            _botConfig = botConfig;
        }

        public void SendMessageToDiscord(string content)
        {
            Message message = new Message();

            if (_client.CurrentUser != null)
            {
                message.username = _client.CurrentUser.Username;
                message.avatar_url = _client.CurrentUser.GetAvatarUrl();
            }
            else
            {
                message.username = "Bot";
                message.avatar_url = "";
            }

            message.content = content;
            var httpContent = new StringContent(JsonConvert.SerializeObject(message), Encoding.UTF8, "application/json");
            Client.PostAsync(_botConfig.WebHookUrl, httpContent);
        }
    }
}
