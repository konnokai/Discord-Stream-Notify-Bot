namespace Discord_Stream_Notify_Bot.HttpClients.TwitCasting
{
    public class TcFrontendStreamInfoData
    {
        [JsonProperty("id")]
        public int? Id { get; set; }

        [JsonProperty("started_at")]
        public int? StartedAt { get; set; }

        [JsonProperty("visibility")]
        public Visibility Visibility { get; set; }

        [JsonProperty("collabo")]
        public object Collabo { get; set; }

        [JsonProperty("is_tool")]
        public bool? IsTool { get; set; }

        [JsonProperty("is_games")]
        public object IsGames { get; set; }

        [JsonProperty("is_vtuber")]
        public object IsVtuber { get; set; }

        [JsonProperty("is_corporate_broadcasting")]
        public object IsCorporateBroadcasting { get; set; }

        [JsonProperty("is_portrait")]
        public object IsPortrait { get; set; }

        [JsonProperty("is_dvr_supported")]
        public object IsDvrSupported { get; set; }

        [JsonProperty("has_secrecy_of_communication")]
        public bool? HasSecrecyOfCommunication { get; set; }
    }

    public class Visibility
    {
        [JsonProperty("type")]
        public string Type { get; set; }
    }
}
