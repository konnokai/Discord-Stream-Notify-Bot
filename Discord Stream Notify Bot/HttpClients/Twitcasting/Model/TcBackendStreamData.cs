namespace Discord_Stream_Notify_Bot.HttpClients.Twitcasting.Model
{
    public class TcBackendStreamData
    {
        [JsonProperty("movie")]
        public BackendMovie Movie { get; set; }

        [JsonProperty("hls")]
        public Hls Hls { get; set; }

        [JsonProperty("fmp4")]
        public Fmp4 Fmp4 { get; set; }

        [JsonProperty("llfmp4")]
        public Llfmp4 Llfmp4 { get; set; }

        [JsonProperty("webrtc")]
        public Webrtc Webrtc { get; set; }
    }

    public class Fmp4
    {
        [JsonProperty("host")]
        public string Host { get; set; }

        [JsonProperty("proto")]
        public string Proto { get; set; }

        [JsonProperty("source")]
        public bool Source { get; set; }

        [JsonProperty("mobilesource")]
        public bool Mobilesource { get; set; }
    }

    public class Hls
    {
        [JsonProperty("host")]
        public string Host { get; set; }

        [JsonProperty("proto")]
        public string Proto { get; set; }

        [JsonProperty("source")]
        public bool Source { get; set; }
    }

    public class Llfmp4
    {
        [JsonProperty("streams")]
        public Streams Streams { get; set; }
    }

    public class BackendMovie
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("live")]
        public bool Live { get; set; }
    }

    public class Streams
    {
        [JsonProperty("main")]
        public string Main { get; set; }

        [JsonProperty("base")]
        public string Base { get; set; }
    }

    public class Webrtc
    {
        [JsonProperty("streams")]
        public Streams Streams { get; set; }

        [JsonProperty("app")]
        public App App { get; set; }
    }

    public class App
    {
        [JsonProperty("mode")]
        public string Mode { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }
    }
}
