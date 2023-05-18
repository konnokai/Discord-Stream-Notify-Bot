namespace Discord_Stream_Notify_Bot.SharedService.Youtube.Json
{
    public class NijisanjiStreamJson
    {
        [JsonProperty("data")]
        public List<Data> Data { get; set; }

        [JsonProperty("included")]
        public List<Data> Included { get; set; }
    }

    public class Attributes
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("thumbnail_url")]
        public string ThumbnailUrl { get; set; }

        [JsonProperty("start_at")]
        public DateTime? StartAt { get; set; }

        [JsonProperty("end_at")]
        public DateTime? EndAt { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("external_id")]
        public string ExternalId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("main")]
        public bool? Main { get; set; }
    }

    public class Data
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("attributes")]
        public Attributes Attributes { get; set; }

        [JsonProperty("relationships")]
        public Relationships Relationships { get; set; }
    }

    public class Liver
    {
        [JsonProperty("data")]
        public Data Data { get; set; }
    }

    public class Relationships
    {
        [JsonProperty("youtube_channel")]
        public YoutubeChannel YoutubeChannel { get; set; }

        [JsonProperty("youtube_events_livers")]
        public YoutubeEventsLivers YoutubeEventsLivers { get; set; }

        [JsonProperty("youtube_channels")]
        public YoutubeChannels YoutubeChannels { get; set; }

        [JsonProperty("liver")]
        public Liver Liver { get; set; }

        [JsonProperty("youtube_events")]
        public YoutubeEvents YoutubeEvents { get; set; }
    }

    public class YoutubeChannel
    {
        [JsonProperty("data")]
        public Data Data { get; set; }
    }

    public class YoutubeChannels
    {
    }

    public class YoutubeEvents
    {
    }

    public class YoutubeEventsLivers
    {
        [JsonProperty("data")]
        public List<object> Data { get; set; }
    }
}
