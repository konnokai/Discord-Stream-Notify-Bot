using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Discord_Stream_Notify_Bot.HttpClients
{
    // https://blog.ailand.date/2021/05/12/how-to-crawl-twitter-with-graphql/
    public class TwitterClient
    {
        private string queryId = "";
        private string featureSwitches = "";
        private readonly HttpClient _httpClient;

        public TwitterClient(HttpClient httpClient)
        {
#if DEBUG
            return;
#endif
            httpClient.DefaultRequestHeaders.Add("UserAgent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.82 Safari/537.36");
            httpClient.DefaultRequestHeaders.Add("Referer", "https://twitter.com/");
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs=1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA");
            httpClient.DefaultRequestHeaders.Add("ContentType", "application/json");

            _httpClient = httpClient;
        }

        public async Task GetQueryIdAndFeatureSwitchesAsync()
        {
            HtmlAgilityPack.HtmlWeb htmlWeb = new HtmlAgilityPack.HtmlWeb();
            htmlWeb.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.82 Safari/537.36";
            var web = await htmlWeb.LoadFromWebAsync("https://twitter.com/");

            Regex regex = new Regex(@"https:\/\/abs\.twimg\.com\/responsive-web\/client-web([^\/]+|)\/main\.[^.]+\.js");
            string mainJsUrl = regex.Match(web.Text).Value;

            if (mainJsUrl != "")
            {
                string mainJsText = await _httpClient.GetStringAsync(mainJsUrl);

                regex = new Regex("{queryId:\"([^\"]+)\",operationName:\"([^\"]+)\",operationType:\"([^\"]+)\",metadata:{featureSwitches:\\[([^\\]]+)", RegexOptions.None);
                var queryList = regex.Matches(mainJsText);

                var exports = queryList.FirstOrDefault((x) => x.Groups[2].Value == "AudioSpaceById");
                queryId = exports.Groups[1].Value;
                featureSwitches = "{" + string.Join(',', exports.Groups[4].Value.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select((x) => $"{x}:false")) + "}";
                featureSwitches = WebUtility.UrlEncode(featureSwitches);

                _httpClient.DefaultRequestHeaders.Remove("x-guest-token");
                regex = new Regex(@"gt=(\d{19});");
                var guestToken = regex.Match(web.Text).Groups[1].Value;
                _httpClient.DefaultRequestHeaders.Add("x-guest-token", guestToken);

                Log.Info("NewTwitterMetadata Found!");
                Log.Info($"QueryId: {queryId}");
                Log.Info($"FeatureSwitches: {featureSwitches}");
                Log.Info($"GuestToken: {guestToken}");
            }
        }

        public async Task<JToken> GetTwitterSpaceMetadataAsync(string spaceId, bool isRefresh = false)
        {
            if (string.IsNullOrEmpty(queryId) || string.IsNullOrEmpty(featureSwitches))
                await GetQueryIdAndFeatureSwitchesAsync();

            string variables = WebUtility.UrlEncode(JsonConvert.SerializeObject(new
            {
                id = spaceId,
                isMetatagsQuery = false,
                withSuperFollowsUserFields = true,
                withDownvotePerspective = false,
                withReactionsMetadata = false,
                withReactionsPerspective = false,
                withSuperFollowsTweetFields = true,
                withReplays = true
            }));

            try
            {
                string url = $"https://twitter.com/i/api/graphql/{queryId}/AudioSpaceById?variables={variables}&features={featureSwitches}";
                return JObject.Parse(await _httpClient.GetStringAsync(url))["data"]["audioSpace"]["metadata"];
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("40") && !isRefresh)
                {
                    await GetQueryIdAndFeatureSwitchesAsync();
                    return await GetTwitterSpaceMetadataAsync(spaceId, true);
                }
                else throw;
            }
        }

        public async Task<string> GetTwitterSpaceMasterUrlAsync(string mediaKey)
        {
            try
            {
                string url = $"https://twitter.com/i/api/1.1/live_video_stream/status/{mediaKey}";
                return JObject.Parse(await _httpClient.GetStringAsync(url))["source"]["location"].ToString();
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
