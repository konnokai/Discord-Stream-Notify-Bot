using DiscordStreamNotifyBot.HttpClients.Twitcasting.Model;
using System.Text;

#nullable enable

namespace DiscordStreamNotifyBot.HttpClients
{
    public class TwitcastingClient
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClient? _apiHttpClient;

        private readonly string? _twitcastingAccessToken;
        private static readonly string[] _events = ["livestart"];

        public TwitcastingClient(HttpClient httpClient, BotConfig botConfig)
        {
            _httpClient = httpClient;

            // https://apiv2-doc.twitcasting.tv/#access-token
            if (!string.IsNullOrEmpty(botConfig.TwitCastingClientId) && !string.IsNullOrEmpty(botConfig.TwitCastingClientSecret))
            {
                _twitcastingAccessToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{botConfig.TwitCastingClientId}:{botConfig.TwitCastingClientSecret}"));

                _apiHttpClient = new HttpClient();
                _apiHttpClient.BaseAddress = new Uri("https://apiv2.twitcasting.tv/");
                _apiHttpClient.DefaultRequestHeaders.Add("Accept", $"application/json");
                _apiHttpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {_twitcastingAccessToken}");
                _apiHttpClient.DefaultRequestHeaders.Add("X-Api-Version", $"2.0");
            }
        }

        /// <summary>
        /// 取得直播分類資料
        /// </summary>
        /// <param name="channelId"></param>
        /// <returns></returns>
        public async Task<List<Category>?> GetCategoriesAsync()
        {
            if (_apiHttpClient == null)
                throw new NullReferenceException(nameof(_apiHttpClient));

            try
            {
                var json = await _apiHttpClient.GetStringAsync($"categories?lang=ja");
                var data = JsonConvert.DeserializeObject<CategoriesJson>(json);
                return data?.Categories;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "TwitCastingClient.GetCategoriesAsync");
                return null;
            }
        }

        /// <summary>
        /// 取得頻道正在直播的資料
        /// </summary>
        /// <param name="channelId"></param>
        /// <returns>如果正在直播，則回傳 (<see langword="true"/>, <see cref="int">影片 Id</see>)，否則為 (<see langword="false"/>, <see cref="int">0</see>)</returns>
        public async Task<TcBackendStreamData?> GetNewStreamDataAsync(string channelId)
        {
            try
            {
                var json = await _httpClient.GetStringAsync($"https://twitcasting.tv/streamserver.php?target={channelId}&mode=client");
                if (json == "{}")
                    return null;

                var data = JsonConvert.DeserializeObject<TcBackendStreamData>(json);
                return data;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "TwitCastingClient.GetNewStreamDataAsync");
                return null;
            }
        }

        // https://apiv2-doc.twitcasting.tv/#get-movie-info
        /// <summary>
        /// 取得影片資料
        /// </summary>
        /// <param name="movieId">影片 Id</param>
        /// <returns><see cref="GetMovieInfoResponse"/></returns>
        public async Task<GetMovieInfoResponse?> GetMovieInfoAsync(int movieId)
        {
            if (_apiHttpClient == null)
                throw new NullReferenceException(nameof(_apiHttpClient));

            if (movieId <= 0)
                throw new FormatException(nameof(movieId));

            try
            {
                var json = await _apiHttpClient.GetStringAsync($"movies/{movieId}");
                var data = JsonConvert.DeserializeObject<GetMovieInfoResponse>(json);
                return data;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "TwitCastingClient.GetMovieInfoAsync");
                return null;
            }
        }

        /// <summary>
        /// 取得使用者資訊
        /// </summary>
        /// <param name="userIdOrScreenId">使用者 id 或 screen_id</param>
        /// <returns><see cref="GetUserInfoResponse"/></returns>
        public async Task<GetUserInfoResponse?> GetUserInfoAsync(string userIdOrScreenId)
        {
            if (_apiHttpClient == null)
                throw new NullReferenceException(nameof(_apiHttpClient));

            if (string.IsNullOrEmpty(userIdOrScreenId))
                throw new ArgumentNullException(nameof(userIdOrScreenId));

            try
            {
                var json = await _apiHttpClient.GetStringAsync($"users/{userIdOrScreenId}");
                var data = JsonConvert.DeserializeObject<GetUserInfoResponse>(json);
                return data;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Not Found
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "TwitCastingClient.GetUserInfoAsync");
                return null;
            }
        }

        #region WebHook
        /// <summary>
        /// 取得所有已註冊的 WebHook
        /// </summary>
        /// <returns>返回 WebHook 列表</returns>
        /// <exception cref="NullReferenceException"></exception>
        public async Task<List<Webhook>?> GetAllRegistedWebHookAsync()
        {
            if (_apiHttpClient == null)
                throw new NullReferenceException(nameof(_apiHttpClient));

            const int pageSize = 50;
            int offset = 0;
            int allCount = int.MaxValue;
            var result = new List<Webhook>();

            try
            {
                while (result.Count < allCount)
                {
                    var url = $"webhooks?limit={pageSize}&offset={offset}";
                    var jsonResponse = await _apiHttpClient.GetStringAsync(url);
                    var data = JsonConvert.DeserializeObject<GetAllRegistedWebHookJson>(jsonResponse);

                    if (data?.Webhooks == null || data.Webhooks.Count == 0)
                        break;

                    if (allCount == int.MaxValue)
                        allCount = data.AllCount;

                    result.AddRange(data.Webhooks);
                    offset += pageSize;
                }

                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "TwitCastingClient.GetAllRegistedWebHookAsync");
                return null;
            }
        }

        /// <summary>
        /// 註冊 WebHook
        /// </summary>
        /// <param name="channelId"></param>
        /// <returns></returns>
        /// <exception cref="NullReferenceException"></exception>
        public async Task<bool?> RegisterWebHookAsync(string channelId)
        {
            if (_apiHttpClient == null)
                throw new NullReferenceException(nameof(_apiHttpClient));

            if (string.IsNullOrEmpty(channelId))
                throw new NullReferenceException(nameof(channelId));

            try
            {
                var responseMessage = await _apiHttpClient.PostAsync("webhooks", new StringContent(JsonConvert.SerializeObject(new
                {
                    user_id = channelId,
                    events = _events
                })));

                responseMessage.EnsureSuccessStatusCode();

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "TwitCastingClient.RegisterWebHookAsync");
                return null;
            }
        }

        /// <summary>
        /// 取消註冊 WebHook
        /// </summary>
        /// <param name="channelId"></param>
        /// <returns></returns>
        /// <exception cref="NullReferenceException"></exception>
        public async Task<bool?> RemoveWebHookAsync(string channelId)
        {
            if (_apiHttpClient == null)
                throw new NullReferenceException(nameof(_apiHttpClient));

            if (string.IsNullOrEmpty(channelId))
                throw new NullReferenceException(nameof(channelId));

            try
            {
                var responseMessage = await _apiHttpClient.DeleteAsync($"webhooks?user_id={channelId}&events[]=livestart");

                responseMessage.EnsureSuccessStatusCode();

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "TwitCastingClient.RemoveWebHookAsync");
                return null;
            }
        }
        #endregion
    }
}
