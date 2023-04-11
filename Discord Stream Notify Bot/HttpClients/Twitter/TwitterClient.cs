using Discord_Stream_Notify_Bot.HttpClients.Twitter;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Discord_Stream_Notify_Bot.HttpClients
{
    // https://blog.ailand.date/2021/05/12/how-to-crawl-twitter-with-graphql/
    public class TwitterClient
    {
        private readonly Dictionary<string, (string QueryId, string FeatureSwitches)> _apiQueryData = new();
        private readonly HttpClient _httpClient;
        private readonly HttpClientHandler _handler;

        public TwitterClient(HttpClient httpClient, BotConfig botConfig)
        {
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs=1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA");
            httpClient.DefaultRequestHeaders.Add("UserAgent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");
            httpClient.DefaultRequestHeaders.Add("Referer", "https://twitter.com/");
            httpClient.DefaultRequestHeaders.Add("ContentType", "application/json");

            _httpClient = httpClient;
            _handler = new HttpClientHandler();
            _handler.CookieContainer = new CookieContainer();
            _handler.CookieContainer.Add(new Cookie("auth_token", botConfig.TwitterAuthToken, "/", ".twitter.com"));
        }

        public async Task<string> GetGusetTokenAsync()
        {
            try
            {
                var data = await _httpClient.PostAsync("https://api.twitter.com/1.1/guest/activate.json", null);
                Regex regex = new Regex(@"""(\d{19})""");
                var guestToken = regex.Match(await data.Content.ReadAsStringAsync()).Groups[1].Value;
                return guestToken;
            }
            catch
            {
                throw new WebException("GetGusetToken");
            }
        }

        public async Task GetQueryIdAndFeatureSwitchesAsync()
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("UserAgent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36");
                httpClient.DefaultRequestHeaders.Add("Referer", "https://twitter.com/");
                var web = await httpClient.GetStringAsync("https://twitter.com");

                Regex regex = new Regex(@"api:""([^""]+)""");
                var match = regex.Match(web);
                if (!match.Success)
                    throw new Exception("GetQueryIdAndFeatureSwitchesAsync-Get API version error");

                string type = "client-web";
                if (web.Contains("-legacy"))
                    type += "-legacy";

                _apiQueryData.Clear();
                regex = new Regex("{queryId:\"([^\"]+)\",operationName:\"([^\"]+)\",operationType:\"([^\"]+)\",metadata:{featureSwitches:\\[([^\\]]+)", RegexOptions.None);

                string mainJsText = await httpClient.GetStringAsync($"https://abs.twimg.com/responsive-web/{type}/api.{match.Groups[1].Value}a.js");
                var queryList = regex.Matches(mainJsText);
                foreach (Match item in queryList)
                {
                    string queryId = item.Groups[1].Value;
                    string featureSwitches = "{" + string.Join(',', item.Groups[4].Value.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select((x) => $"{x}:false")) + "}";
                    featureSwitches = WebUtility.UrlEncode(featureSwitches);
                    _apiQueryData.Add(item.Groups[2].Value, new(queryId, featureSwitches));
                }  

                _httpClient.DefaultRequestHeaders.Remove("x-guest-token");
                string guestToken = await GetGusetTokenAsync();
                _httpClient.DefaultRequestHeaders.Add("x-guest-token", guestToken);

                Log.Info("NewTwitterAPIQueryData Found!");
                Log.Info($"Total QueryData: {_apiQueryData.Count}");
                Log.Info($"AudioSpaceById QueryId: {_apiQueryData["AudioSpaceById"].QueryId}");
                Log.Info($"UserByScreenName QueryId: {_apiQueryData["UserByScreenName"].QueryId}");
                Log.Info($"GuestToken: {guestToken}");
            }
        }

        public async Task<TwitterUserJson> GetUserDataByScreenNameAsync(string screenName, bool isRefresh = false)
        {
            if (!_apiQueryData.ContainsKey("UserByScreenName"))
                await GetQueryIdAndFeatureSwitchesAsync();

            if (string.IsNullOrEmpty(_apiQueryData["UserByScreenName"].QueryId))
                await GetQueryIdAndFeatureSwitchesAsync();

            string variables = WebUtility.UrlEncode(JsonConvert.SerializeObject(new
            {
                screen_name = screenName,
                withSafetyModeUserFields = true,
                withSuperFollowsUserFields = true
            }));

            try
            {
                string url = $"https://twitter.com/i/api/graphql/{_apiQueryData["UserByScreenName"].QueryId}/UserByScreenName?variables={variables}&features={_apiQueryData["UserByScreenName"].FeatureSwitches}";
                return JsonConvert.DeserializeObject<TwitterUserJson>(await _httpClient.GetStringAsync(url));
            }
            catch (HttpRequestException httpEx) when (httpEx.Message.Contains("40") && !isRefresh)
            {
                await GetQueryIdAndFeatureSwitchesAsync();
                return await GetUserDataByScreenNameAsync(screenName, true);
            }
            catch (Exception)
            {
                throw;
            }
        }

        // https://github.com/HitomaruKonpaku/twspace-crawler/blob/abaac096bf6bf33a13454c68c22720d2665bd062/src/apis/TwitterApi.ts#L47-L56
        public async Task<List<TwitterSpacesData>> GetTwitterSpaceByUsersIdAsync(params string[] usersId)
        {
            var resultList = new List<TwitterSpacesData>();
            using (var httpClient = new HttpClient(_handler, false))
            {
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs=1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA");
                httpClient.DefaultRequestHeaders.Add("UserAgent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/109.0.0.0 Safari/537.36");
                httpClient.DefaultRequestHeaders.Add("Referer", "https://twitter.com/");
                httpClient.DefaultRequestHeaders.Add("ContentType", "application/json");
                // https://blog.nest.moe/posts/how-to-crawl-twitter-with-graphql/#csrf-token
                httpClient.DefaultRequestHeaders.Add("x-csrf-token", DateTimeOffset.UtcNow.ToString().ToMD5());

                try
                {
                    // user_ids可以放多個，使用','來分隔，應該也是以100人為限
                    // 如果沒Spaces的話會回傳空的資料，所以不用特別判定現在是否正在開
                    var result = await httpClient.GetStringAsync($"https://twitter.com/i/api/fleets/v1/avatar_content?user_ids={string.Join(',', usersId)}&only_spaces=true");
                    if (result.Contains("{\"users\":{}")) // 空的代表查詢的Id都沒有開Space;
                        return resultList;

                    var json = JObject.Parse(result);

                    foreach (var item in usersId)
                    {
                        if (json["users"][item] != null)
                        {
                            var spaceData = json["users"][item]["spaces"]["live_content"]["audiospace"];
                            resultList.Add(new TwitterSpacesData() { UserId = item, SpaceId = spaceData["broadcast_id"].ToString(), SpaceTitle = spaceData["title"].ToString(), StartAt = (DateTime?)spaceData["start"] });
                        }
                    }

                    return resultList;
                }
                catch (JsonReaderException jsonEx)
                {
                    Log.Error(jsonEx, "GetTwitterSpaceByUsersIdAsync-Json");
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "GetTwitterSpaceByUsersIdAsync");
                    throw;
                }
            }
        }

        public async Task<JToken> GetTwitterSpaceMetadataAsync(string spaceId, bool isRefresh = false)
        {
            if (!_apiQueryData.ContainsKey("AudioSpaceById"))
                await GetQueryIdAndFeatureSwitchesAsync();

            if (string.IsNullOrEmpty(_apiQueryData["AudioSpaceById"].QueryId) || string.IsNullOrEmpty(_apiQueryData["AudioSpaceById"].FeatureSwitches))
                await GetQueryIdAndFeatureSwitchesAsync();

            string variables = WebUtility.UrlEncode(JsonConvert.SerializeObject(new
            {
                id = spaceId,
                isMetatagsQuery = false,
                withDownvotePerspective = false,
                withReactionsMetadata = false,
                withReactionsPerspective = false,
                withReplays = true
            }));

            try
            {
                string url = $"https://twitter.com/i/api/graphql/{_apiQueryData["AudioSpaceById"].QueryId}/AudioSpaceById?variables={variables}&features={_apiQueryData["AudioSpaceById"].FeatureSwitches}";
                return JObject.Parse(await _httpClient.GetStringAsync(url))["data"]["audioSpace"]["metadata"];
            }
            catch (HttpRequestException httpEx) when (httpEx.Message.Contains("40") && !isRefresh)
            {
                await GetQueryIdAndFeatureSwitchesAsync();
                return await GetTwitterSpaceMetadataAsync(spaceId, true);
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
                return JObject.Parse(await _httpClient.GetStringAsync(url))["source"]["location"].ToString();
            }
            catch (Exception)
            {
                throw;
            }
        }
    }

    // https://ithelp.ithome.com.tw/articles/10190215
    public static class MD5Extensions
    {
        public static string ToMD5(this string str)
        {
            using (var cryptoMD5 = System.Security.Cryptography.MD5.Create())
            {
                //將字串編碼成 UTF8 位元組陣列
                var bytes = Encoding.UTF8.GetBytes(str);

                //取得雜湊值位元組陣列
                var hash = cryptoMD5.ComputeHash(bytes);

                //取得 MD5
                var md5 = BitConverter.ToString(hash)
                  .Replace("-", String.Empty)
                  .ToUpper();

                return md5;
            }
        }
    }
}
