using Newtonsoft.Json;

namespace DiscordStreamNotifyBot.HttpClients.Twitcasting.Model
{
    public class GetUserInfoResponse
    {
        [JsonProperty("user")]
        public Broadcaster User { get; set; }

        [JsonProperty("supporter_count")]
        public int SupporterCount { get; set; }

        [JsonProperty("supporting_count")]
        public int SupportingCount { get; set; }
    }
}