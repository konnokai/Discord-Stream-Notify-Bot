using DiscordStreamNotifyBot.HttpClients.Twitter;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using Polly;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DiscordStreamNotifyBot.HttpClients
{
    // https://blog.ailand.date/2021/05/12/how-to-crawl-twitter-with-graphql/
    public class TwitterClient
    {
        private readonly BotConfig _botConfig;
        private readonly HttpClient _graphQLClient;
        private readonly HttpClientHandler _handler;
        private readonly Dictionary<string, (string QueryId, string FeatureSwitches)> _apiQueryData = new();

        public TwitterClient(HttpClient _, BotConfig botConfig)
        {
            _botConfig = botConfig;

            _handler = new HttpClientHandler();
            _handler.CookieContainer = new CookieContainer();
            _handler.CookieContainer.Add(new Cookie("auth_token", _botConfig.TwitterAuthToken, "/", ".twitter.com"));
            _handler.CookieContainer.Add(new Cookie("ct0", _botConfig.TwitterCSRFToken, "/", ".twitter.com"));

            _graphQLClient = new HttpClient(_handler, false);
            _graphQLClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs=1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA");
            _graphQLClient.DefaultRequestHeaders.Add("UserAgent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36");
            _graphQLClient.DefaultRequestHeaders.Add("ContentType", "application/json");
            _graphQLClient.DefaultRequestHeaders.Add("X-Csrf-Token", _botConfig.TwitterCSRFToken); // ct0 跟 csrf token 是一樣的
        }

        public async Task<string> GetGusetTokenAsync()
        {
            try
            {
                using (var httpClient = new HttpClient(_handler, false))
                {
                    httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs=1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA");
                    httpClient.DefaultRequestHeaders.Add("UserAgent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36");
                    httpClient.DefaultRequestHeaders.Add("Referer", "https://twitter.com/");
                    httpClient.DefaultRequestHeaders.Add("ContentType", "application/json");
                    httpClient.DefaultRequestHeaders.Add("X-Csrf-Token", _botConfig.TwitterCSRFToken);

                    var data = await Policy.Handle<HttpRequestException>()
                        .Or<WebException>((ex) => ex.Message.Contains("unavailable")) // Resource temporarily unavailable
                        .Or<TaskCanceledException>((ex) => ex.Message.Contains("HttpClient.Timeout")) // The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing
                        .WaitAndRetryAsync(3, (retryAttempt) =>
                        {
                            var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                            Log.Warn($"Twitter GetGusetTokenAsync: POST 失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                            return timeSpan;
                        })
                        .ExecuteAsync(async () =>
                        {
                            return await httpClient.PostAsync("https://api.twitter.com/1.1/guest/activate.json", null);
                        });

                    Regex regex = new Regex(@"""(\d{19})""");
                    var guestToken = regex.Match(await data.Content.ReadAsStringAsync()).Groups[1].Value;
                    return guestToken;
                }
            }
            catch
            {
                throw new WebException("GetGusetToken");
            }
        }

        public async Task GetQueryIdAndFeatureSwitchesAsync()
        {
            _apiQueryData.Clear();

            CookieContainer cookies = new CookieContainer();
            HttpClientHandler handler = new HttpClientHandler();
            handler.CookieContainer = cookies;

            using (var httpClient = new HttpClient(handler))
            {
                httpClient.DefaultRequestHeaders.Add("UserAgent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                httpClient.DefaultRequestHeaders.Add("Referer", "https://twitter.com/");

                var webContext = await GetRealHomePageContextAsync(httpClient);

                foreach (var item in new string[] { "main", "modules.audio" })
                {
                    await AddApiQueryDataAsync(httpClient, webContext, item);
                }

                _graphQLClient.DefaultRequestHeaders.Remove("x-guest-token");
                string guestToken = await GetGusetTokenAsync();
                _graphQLClient.DefaultRequestHeaders.Add("x-guest-token", guestToken);

                Log.Info("NewTwitterAPIQueryData Found!");
                Log.Info($"Total QueryData: {_apiQueryData.Count}");
                Log.Info($"AudioSpaceById QueryId: {_apiQueryData["AudioSpaceById"].QueryId}");
                Log.Info($"UserByScreenName QueryId: {_apiQueryData["UserByScreenName"].QueryId}");
                Log.Info($"GuestToken: {guestToken}");
            }
        }

        private async Task<string> GetRealHomePageContextAsync(HttpClient httpClient)
        {
            return await Policy.Handle<HttpRequestException>()
                .Or<WebException>((ex) => ex.Message.Contains("unavailable")) // Resource temporarily unavailable
                .Or<TaskCanceledException>((ex) => ex.Message.Contains("HttpClient.Timeout")) // The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing
                .WaitAndRetryAsync(3, (retryAttempt) =>
                {
                    var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                    Log.Warn($"Twitter GetRealHomePageContextAsync: GET or POST 失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                    return timeSpan;
                })
                .ExecuteAsync(async () =>
                {
                    var httpResponse = await httpClient.GetAsync("https://twitter.com");
                    var firstWebContext = await httpResponse.Content.ReadAsStringAsync();

                    string redirectUrl = "";
                    Regex regex = new Regex(@"document\.location\s*=\s*""(?'url'.+?)""");
                    Match match = Regex.Match(firstWebContext, @"document\.location\s*=\s*""(?'url'.+?)""");
                    if (match.Success)
                        redirectUrl = match.Groups["url"].Value;

                    if (string.IsNullOrEmpty(redirectUrl))
                        throw new NullReferenceException(redirectUrl);

                    httpResponse = await httpClient.GetAsync(redirectUrl);
                    HtmlDocument htmlDocument = new HtmlDocument();
                    htmlDocument.LoadHtml(await httpResponse.Content.ReadAsStringAsync());
                    var nodes = htmlDocument.DocumentNode.Descendants().Where((x) => x.Name == "input");

                    var formContent = new Dictionary<string, string>();
                    foreach (var node in nodes)
                    {
                        if (node.GetAttributeValue("type", "") != "hidden")
                            continue;

                        string name = node.GetAttributeValue("name", "");
                        string value = node.GetAttributeValue("value", "");
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value))
                            continue;

                        formContent.Add(name, value);
                    }

                    httpResponse = await httpClient.PostAsync("https://x.com/x/migrate", new FormUrlEncodedContent(formContent));
                    var readWebContext = await httpResponse.Content.ReadAsStringAsync();

                    return readWebContext;
                });
        }

        private async Task AddApiQueryDataAsync(HttpClient httpClient, string webContext, string fileName)
        {
            try
            {
                if (fileName == "main")
                {
                    Regex mainRegex = new Regex(@"main.([^""]+).js");

                    var match = mainRegex.Match(webContext);
                    if (!match.Success)
                        throw new Exception($"AddApiQueryDataAsync - Get {fileName} version error");

                    fileName = match.ToString();
                }
                else
                {
                    Regex fileNameRegex = new Regex($@"""{fileName.Replace(".", "\\.")}"":""(\w+)""");

                    var match = fileNameRegex.Match(webContext);
                    if (!match.Success)
                        throw new Exception($"AddApiQueryDataAsync - Get {fileName} version error");

                    fileName = $"{fileName}.{match.Groups[1]}a.js";
                }

                string type = "client-web";
                if (webContext.Contains("-legacy"))
                    type += "-legacy";

                Regex apiQueryRegex = new Regex("{queryId:\"([^\"]+)\",operationName:\"([^\"]+)\",operationType:\"([^\"]+)\",metadata:{featureSwitches:\\[([^\\]]+)", RegexOptions.None);

                Log.Debug($"https://abs.twimg.com/responsive-web/{type}/{fileName}");

                var mainJsText = await Policy.Handle<HttpRequestException>()
                    .Or<WebException>((ex) => ex.Message.Contains("unavailable")) // Resource temporarily unavailable
                    .Or<TaskCanceledException>((ex) => ex.Message.Contains("HttpClient.Timeout")) // The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing
                    .WaitAndRetryAsync(3, (retryAttempt) =>
                    {
                        var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                        Log.Warn($"Twitter AddApiQueryDataAsync: GET 失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                        return timeSpan;
                    })
                    .ExecuteAsync(async () =>
                    {
                        return await httpClient.GetStringAsync($"https://abs.twimg.com/responsive-web/{type}/{fileName}");
                    });

                var queryList = apiQueryRegex.Matches(mainJsText);
                foreach (Match item in queryList)
                {
                    string queryId = item.Groups[1].Value;
                    string featureSwitches = "{" + string.Join(',', item.Groups[4].Value.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select((x) => $"{x}:false")) + "}";
                    featureSwitches = WebUtility.UrlEncode(featureSwitches);
                    _apiQueryData.Add(item.Groups[2].Value, new(queryId, featureSwitches));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), $"AddApiQueryData - {fileName}");
                throw;
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
                var json = await Policy.Handle<HttpRequestException>()
                    .Or<WebException>((ex) => ex.Message.Contains("unavailable")) // Resource temporarily unavailable
                    .Or<TaskCanceledException>((ex) => ex.Message.Contains("HttpClient.Timeout")) // The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing
                    .WaitAndRetryAsync(3, (retryAttempt) =>
                    {
                        var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                        Log.Warn($"Twitter GetUserDataByScreenNameAsync: GET 失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                        return timeSpan;
                    })
                    .ExecuteAsync(async () =>
                    {
                        return await _graphQLClient.GetStringAsync(url);
                    });

                return JsonConvert.DeserializeObject<TwitterUserJson>(json);
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
                httpClient.DefaultRequestHeaders.Add("X-Csrf-Token", DateTimeOffset.UtcNow.ToString().ToMD5());

                try
                {
                    // user_ids可以放多個，使用','來分隔，應該也是以100人為限
                    // 如果沒Spaces的話會回傳空的資料，所以不用特別判定現在是否正在開
                    var result = await Policy.Handle<HttpRequestException>()
                        .Or<WebException>((ex) => ex.Message.Contains("unavailable")) // Resource temporarily unavailable
                        .Or<TaskCanceledException>((ex) => ex.Message.Contains("HttpClient.Timeout")) // The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing
                        .WaitAndRetryAsync(3, (retryAttempt) =>
                        {
                            var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                            Log.Warn($"Twitter GetTwitterSpaceByUsersIdAsync: GET 失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                            return timeSpan;
                        })
                        .ExecuteAsync(async () =>
                        {
                            return await httpClient.GetStringAsync($"https://twitter.com/i/api/fleets/v1/avatar_content?user_ids={string.Join(',', usersId)}&only_spaces=true");
                        });

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
                catch (HttpRequestException httpEx) when (httpEx.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    Log.Warn($"GetTwitterSpaceByUsersIdAsync: 429錯誤");
                    return new List<TwitterSpacesData>();
                }
                catch (JsonReaderException jsonEx)
                {
                    Log.Error(jsonEx, "GetTwitterSpaceByUsersIdAsync-Json");
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Demystify(), "GetTwitterSpaceByUsersIdAsync");
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
                withReplays = true,
                withListeners = true,
            }));

            try
            {
                string url = $"https://twitter.com/i/api/graphql/{_apiQueryData["AudioSpaceById"].QueryId}/AudioSpaceById?variables={variables}&features={_apiQueryData["AudioSpaceById"].FeatureSwitches}";
                var json = await Policy.Handle<HttpRequestException>()
                    .Or<WebException>((ex) => ex.Message.Contains("unavailable")) // Resource temporarily unavailable
                    .Or<TaskCanceledException>((ex) => ex.Message.Contains("HttpClient.Timeout")) // The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing
                    .WaitAndRetryAsync(3, (retryAttempt) =>
                    {
                        var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                        Log.Warn($"Twitter GetTwitterSpaceMetadataAsync: GET 失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                        return timeSpan;
                    })
                    .ExecuteAsync(async () =>
                    {
                        return await _graphQLClient.GetStringAsync(url);
                    });

                return JObject.Parse(json)["data"]["audioSpace"]["metadata"];
            }
            catch (HttpRequestException httpEx) when (httpEx.Message.Contains("40") && !isRefresh)
            {
                await GetQueryIdAndFeatureSwitchesAsync();
                return await GetTwitterSpaceMetadataAsync(spaceId, true);
                throw;
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
                var json = await Policy.Handle<HttpRequestException>()
                    .Or<WebException>((ex) => ex.Message.Contains("unavailable")) // Resource temporarily unavailable
                    .Or<TaskCanceledException>((ex) => ex.Message.Contains("HttpClient.Timeout")) // The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing
                    .WaitAndRetryAsync(3, (retryAttempt) =>
                    {
                        var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                        Log.Warn($"Twitter GetTwitterSpaceMasterUrlAsync: GET 失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                        return timeSpan;
                    })
                    .ExecuteAsync(async () =>
                    {
                        return await _graphQLClient.GetStringAsync(url);
                    });

                return JObject.Parse(json)["source"]["location"].ToString();
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
