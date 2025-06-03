namespace Discord_Stream_Notify_Bot.SharedService.Twitcasting
{
    public class TwitCastingWebHookJson
    {
        [JsonProperty("signature")]
        public string Signature { get; set; }

        [JsonProperty("movie")]
        public Movie Movie { get; set; }

        [JsonProperty("broadcaster")]
        public Broadcaster Broadcaster { get; set; }
    }

    public class Broadcaster
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("screen_id")]
        public string ScreenId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("image")]
        public string Image { get; set; }

        [JsonProperty("profile")]
        public string Profile { get; set; }

        [JsonProperty("level")]
        public int Level { get; set; }

        [JsonProperty("last_movie_id")]
        public string LastMovieId { get; set; }

        [JsonProperty("is_live")]
        public bool IsLive { get; set; }

        [JsonProperty("supporter_count")]
        public int SupporterCount { get; set; }

        [JsonProperty("supporting_count")]
        public int SupportingCount { get; set; }

        [JsonProperty("created")]
        public int Created { get; set; }
    }

    public class Movie
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("subtitle")]
        public string Subtitle { get; set; }

        [JsonProperty("last_owner_comment")]
        public string LastOwnerComment { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("link")]
        public string Link { get; set; }

        [JsonProperty("is_live")]
        public bool IsLive { get; set; }

        [JsonProperty("is_recorded")]
        public bool IsRecorded { get; set; }

        [JsonProperty("comment_count")]
        public int CommentCount { get; set; }

        [JsonProperty("large_thumbnail")]
        public string LargeThumbnail { get; set; }

        [JsonProperty("small_thumbnail")]
        public string SmallThumbnail { get; set; }

        [JsonProperty("country")]
        public string Country { get; set; }

        [JsonProperty("duration")]
        public int Duration { get; set; }

        [JsonProperty("created")]
        public int Created { get; set; }

        [JsonProperty("is_collabo")]
        public bool IsCollabo { get; set; }

        [JsonProperty("is_protected")]
        public bool IsProtected { get; set; }

        [JsonProperty("max_view_count")]
        public int MaxViewCount { get; set; }

        [JsonProperty("current_view_count")]
        public int CurrentViewCount { get; set; }

        [JsonProperty("total_view_count")]
        public int TotalViewCount { get; set; }

        [JsonProperty("hls_url")]
        public string HlsUrl { get; set; }
    }
}
