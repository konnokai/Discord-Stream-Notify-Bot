namespace Discord_Stream_Notify_Bot.HttpClients.TwitCasting
{
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class Category
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class FrontendMovie
    {
        [JsonProperty("id")]
        public int? Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("telop")]
        public string Telop { get; set; }

        [JsonProperty("category")]
        public Category Category { get; set; }

        [JsonProperty("viewers")]
        public Viewers Viewers { get; set; }
    }

    public class TcFrontendStreamStatusData
    {
        [JsonProperty("update_interval_sec")]
        public int? UpdateIntervalSec { get; set; }

        [JsonProperty("movie")]
        public FrontendMovie Movie { get; set; }
    }

    public class Viewers
    {
        [JsonProperty("current")]
        public int? Current { get; set; }

        [JsonProperty("total")]
        public int? Total { get; set; }
    }


}
