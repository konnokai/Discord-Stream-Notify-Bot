using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;

namespace Discord_Stream_Notify_Bot.HttpClients
{
    // https://blog.ailand.date/2021/05/12/how-to-crawl-twitter-with-graphql/
    public class TwitterClient
    {
        private string queryId = "";
        public HttpClient Client { get; private set; }

        public TwitterClient(HttpClient httpClient)
        {
            httpClient.DefaultRequestHeaders.Add("UserAgent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.82 Safari/537.36");
            httpClient.DefaultRequestHeaders.Add("Referer", "https://twitter.com/");
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs=1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA");
            httpClient.DefaultRequestHeaders.Add("ContentType", "application/json");

            Client = httpClient;
            GetQueryIdAsync().Wait();
        }

        public async Task GetQueryIdAsync()
        {
            HtmlAgilityPack.HtmlWeb htmlWeb = new HtmlAgilityPack.HtmlWeb();
            htmlWeb.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.82 Safari/537.36";
            var web = await htmlWeb.LoadFromWebAsync("https://twitter.com/");

            Regex regex = new Regex(@"https:\/\/abs\.twimg\.com\/responsive-web\/client-web([^\/]+|)\/main\.[^.]+\.js");
            string mainJsUrl = regex.Match(web.Text).Value;

            if (mainJsUrl != "")
            {
                string mainJsText = await Client.GetStringAsync(mainJsUrl);

                regex = new Regex("{queryId:\"([^\"]+)\",operationName:\"([^\"]+)\",operationType:\"([^\"]+)\"", RegexOptions.None);
                var queryList = regex.Matches(mainJsText);
                queryId = queryList.FirstOrDefault((x) => x.Groups[2].Value == "AudioSpaceById").Groups[1].Value;

                Client.DefaultRequestHeaders.Remove("x-guest-token");
                regex = new Regex(@"gt=(\d{19});");
                Client.DefaultRequestHeaders.Add("x-guest-token", regex.Match(web.Text).Groups[1].Value);
            }
        }

        public async Task<JToken> GetTwitterSpaceMetadataAsync(string spaceId)
        {
            if (string.IsNullOrEmpty(queryId))
                await GetQueryIdAsync();

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
                string url = $"https://twitter.com/i/api/graphql/{queryId}/AudioSpaceById?variables=" + query;
                return JObject.Parse(await Client.GetStringAsync(url))["data"]["audioSpace"]["metadata"];
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("40"))
                {
                    await GetQueryIdAsync();
                    return await GetTwitterSpaceMetadataAsync(spaceId);
                }
                else throw;
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

        [Obsolete]
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
