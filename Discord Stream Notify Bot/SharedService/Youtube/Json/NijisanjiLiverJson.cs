namespace Discord_Stream_Notify_Bot.SharedService.Youtube.Json
{
    public class NijisanjiLiverJson
    {
        public List<Content> contents { get; set; }
        public int? totalCount { get; set; }
        public int? offset { get; set; }
        public int? limit { get; set; }
    }

    public class Content
    {
        public string slug { get; set; }
        public bool? hidden { get; set; }
        public string name { get; set; }
        public string enName { get; set; }
        public Images images { get; set; }
        public SocialLinks socialLinks { get; set; }
        public SiteColor siteColor { get; set; }
        public string id { get; set; }
        public int? subscriberCount { get; set; }
    }

    public class Fullbody
    {
        public string url { get; set; }
        public int? height { get; set; }
        public int? width { get; set; }
    }

    public class Halfbody
    {
        public string url { get; set; }
        public int? height { get; set; }
        public int? width { get; set; }
    }

    public class Head
    {
        public string url { get; set; }
        public int? height { get; set; }
        public int? width { get; set; }
    }

    public class Images
    {
        public string fieldId { get; set; }
        public Fullbody fullbody { get; set; }
        public Halfbody halfbody { get; set; }
        public Head head { get; set; }
        public List<object> variation { get; set; }
    }

    public class SiteColor
    {
        public string id { get; set; }
        public DateTime? createdAt { get; set; }
        public DateTime? updatedAt { get; set; }
        public DateTime? publishedAt { get; set; }
        public DateTime? revisedAt { get; set; }
        public string name { get; set; }
        public string color1 { get; set; }
        public string color2 { get; set; }
    }

    public class SocialLinks
    {
        public string fieldId { get; set; }
        public string twitter { get; set; }
        public string youtube { get; set; }
        public string twitch { get; set; }
        public string reddit { get; set; }
    }
}
