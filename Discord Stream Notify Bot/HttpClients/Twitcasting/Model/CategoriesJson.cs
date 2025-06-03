namespace Discord_Stream_Notify_Bot.HttpClients.Twitcasting.Model
{
    public class Category
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("sub_categories")]
        public List<SubCategory> SubCategories { get; set; }
    }

    public class CategoriesJson
    {
        [JsonProperty("categories")]
        public List<Category> Categories { get; set; }
    }

    public class SubCategory
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("count")]
        public int Count { get; set; }
    }
}
