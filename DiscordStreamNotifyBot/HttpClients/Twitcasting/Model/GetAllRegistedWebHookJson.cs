namespace DiscordStreamNotifyBot.HttpClients.Twitcasting.Model
{
    public class GetAllRegistedWebHookJson
    {
        [JsonProperty("all_count")]
        public int AllCount { get; set; }

        [JsonProperty("webhooks")]
        public List<Webhook> Webhooks { get; set; }
    }

    public class Webhook
    {
        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("event")]
        public string Event { get; set; }
    }
}
