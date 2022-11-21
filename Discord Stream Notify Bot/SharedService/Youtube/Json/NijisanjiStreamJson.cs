using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Discord_Stream_Notify_Bot.SharedService.Youtube.Json
{
    public class NijisanjiStreamJson
    {
        public Props props { get; set; }
        public string page { get; set; }
        public string buildId { get; set; }
        public bool isFallback { get; set; }
        public bool gssp { get; set; }
        public string locale { get; set; }
        public List<string> locales { get; set; }
        public string defaultLocale { get; set; }
    }

    public class PageProps
    {
        public List<Stream> streams { get; set; }
    }

    public class Props
    {
        public PageProps pageProps { get; set; }
    }

    public class Liver
    {
        [JsonProperty("external-id")]
        public string externalid { get; set; }
        public string id { get; set; }
    }

    public class Stream
    {
        public string title { get; set; }
        public string description { get; set; }
        public string url { get; set; }

        [JsonProperty("thumbnail-url")]
        public string thumbnailurl { get; set; }

        [JsonProperty("start-at")]
        public DateTime? startat { get; set; }

        [JsonProperty("end-at")]
        public DateTime? endat { get; set; }
        public string status { get; set; }
        public string id { get; set; }

        [JsonProperty("youtube-channel")]
        public YoutubeChannel youtubechannel { get; set; }

        [JsonProperty("youtube-events-livers")]
        public List<YoutubeEventsLiver> youtubeeventslivers { get; set; }
    }

    public class YoutubeChannel
    {
        public string name { get; set; }

        [JsonProperty("thumbnail-url")]
        public string thumbnailurl { get; set; }
        public string id { get; set; }
        public Liver liver { get; set; }
    }

    public class YoutubeEventsLiver
    {
        [JsonProperty("external-id")]
        public string externalid { get; set; }
        public string id { get; set; }
    }
}
