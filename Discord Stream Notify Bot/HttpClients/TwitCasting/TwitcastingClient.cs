using Discord_Stream_Notify_Bot.HttpClients.TwitCasting;
using System.Text;

#nullable enable

namespace Discord_Stream_Notify_Bot.HttpClients
{
    public class TwitCastingClient
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClient? _apiHttpClient;

        private readonly string? _twitCastingAccessToken;

        public TwitCastingClient(HttpClient httpClient, BotConfig botConfig)
        {
            _httpClient = httpClient;

            // https://apiv2-doc.twitcasting.tv/#access-token
            if (!string.IsNullOrEmpty(botConfig.TwitCastingClientId) && !string.IsNullOrEmpty(botConfig.TwitCastingClientSecret))
            {
                _twitCastingAccessToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{botConfig.TwitCastingClientId}:{botConfig.TwitCastingClientSecret}"));

                _apiHttpClient = new HttpClient();
                _apiHttpClient.BaseAddress = new Uri("https://apiv2.twitcasting.tv/");
                _apiHttpClient.DefaultRequestHeaders.Add("Accept", $"application/json");
                _apiHttpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {_twitCastingAccessToken}");
                _apiHttpClient.DefaultRequestHeaders.Add("X-Api-Version", $"2.0");
            }
        }

        /// <summary>
        /// 取得頻道正在直播的資料
        /// </summary>
        /// <param name="channelId"></param>
        /// <returns>如果正在直播，則回傳 (<see langword="true"/>, <see cref="int">影片 Id</see>)，否則為 (<see langword="false"/>, <see cref="int">0</see>)</returns>
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
                Log.Error($"TwitCastingClient.GetCategoriesAsync: {ex}");
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
                Log.Error($"TwitCastingClient.GetBackendStreamDataAsync: {ex}");
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
                Log.Error($"TwitCastingClient.GetMovieInfoAsync: {ex}");
                return null;
            }
        }
    }
}
