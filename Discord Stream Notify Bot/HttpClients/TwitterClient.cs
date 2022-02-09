using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.HttpClients
{
    public class TwitterClient
    {
        public HttpClient Client { get; private set; }

        public TwitterClient(HttpClient httpClient)
        {
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs=1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA");
            httpClient.DefaultRequestHeaders.Add("ContentType", "application/json");
            httpClient.DefaultRequestHeaders.Add("UserAgent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/96.0.4664.45 Safari/537.36");
            httpClient.DefaultRequestHeaders.Add("Referer", "https://twitter.com/");

            Client = httpClient;
            var token = GetTwitterGuestTokenAsync().Result;
            Client.DefaultRequestHeaders.Add("x-guest-token", token);
        }

        public async Task<JToken> GetTwitterSpaceMetadataAsync(string spaceId)
        {
            string query = WebUtility.UrlEncode(JsonConvert.SerializeObject(new
            {
                id = spaceId,
                isMetatagsQuery = false,
                withSuperFollowsUserFields = false,
                withBirdwatchPivots = false,
                withDownvotePerspective = false,
                withReactionsMetadata = false,
                withReactionsPerspective = false,
                withSuperFollowsTweetFields = false,
                withReplays = false,
                withScheduledSpaces = false
            }));

            try
            {
                string url = "https://twitter.com/i/api/graphql/Uv5R_-Chxbn1FEkyUkSW2w/AudioSpaceById?variables=" + query;
                return JObject.Parse(await Client.GetStringAsync(url))["data"]["audioSpace"]["metadata"];
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task<string> GetTwitterSpaceMasterUrlAsync(string mediaKey)
        {
            try
            {
                string url = $"https://twitter.com/i/api/1.1/live_video_stream/status/{mediaKey}";
                return JObject.Parse(await Client.GetStringAsync(url))["source"]["location"].ToString();
            }
            catch (Exception)
            {
                throw;
            }
        }

        private async Task<string> GetTwitterGuestTokenAsync()
        {
            try
            {
                var respone = await Client.PostAsync("https://api.twitter.com/1.1/guest/activate.json", new StringContent(""));
                respone.EnsureSuccessStatusCode();
                return JObject.Parse(await respone.Content.ReadAsStringAsync())["guest_token"].ToString();
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
