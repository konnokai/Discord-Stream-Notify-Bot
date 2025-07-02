namespace DiscordStreamNotifyBot.HttpClients.Twitter
{
    public class TwitterUserJson
    {
        [JsonProperty("data")]
        public Data Data { get; set; }
    }

    public class Data
    {
        [JsonProperty("user")]
        public User User { get; set; }
    }

    public class User
    {
        [JsonProperty("result")]
        public Result Result { get; set; }
    }

    public class Result
    {
        [JsonProperty("rest_id")]
        public string RestId { get; set; }

        [JsonProperty("legacy")]
        public Legacy Legacy { get; set; }
    }

    public class Legacy
    {
        [JsonProperty("protected")]
        public bool? Protected { get; set; }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }

        [JsonProperty("default_profile")]
        public bool? DefaultProfile { get; set; }

        [JsonProperty("default_profile_image")]
        public bool? DefaultProfileImage { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("favourites_count")]
        public int? FavouritesCount { get; set; }

        [JsonProperty("followers_count")]
        public int? FollowersCount { get; set; }

        [JsonProperty("friends_count")]
        public int? FriendsCount { get; set; }

        [JsonProperty("has_custom_timelines")]
        public bool? HasCustomTimelines { get; set; }

        [JsonProperty("is_translator")]
        public bool? IsTranslator { get; set; }

        [JsonProperty("listed_count")]
        public int? ListedCount { get; set; }

        [JsonProperty("location")]
        public string Location { get; set; }

        [JsonProperty("media_count")]
        public int? MediaCount { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("normal_followers_count")]
        public int? NormalFollowersCount { get; set; }

        [JsonProperty("possibly_sensitive")]
        public bool? PossiblySensitive { get; set; }

        [JsonProperty("screen_name")]
        public string ScreenName { get; set; }

        [JsonProperty("profile_banner_url")]
        public string ProfileBannerUrl { get; set; }

        [JsonProperty("profile_image_url_https")]
        public string ProfileImageUrlHttps { get; set; }

        [JsonProperty("statuses_count")]
        public int? StatusesCount { get; set; }

        [JsonProperty("translator_type")]
        public string TranslatorType { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("verified")]
        public bool? Verified { get; set; }
    }
}
