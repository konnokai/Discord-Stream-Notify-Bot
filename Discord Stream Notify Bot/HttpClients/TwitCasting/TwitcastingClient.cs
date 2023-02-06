using Discord_Stream_Notify_Bot.HttpClients.TwitCasting;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;

namespace Discord_Stream_Notify_Bot.HttpClients
{
    public class TwitcastingClient
    {
        private readonly HttpClient _httpClient;

        private readonly string _frontendApiUrl = "https://frontendapi.twitcasting.tv/";
        private readonly string _streamServerUrl = "https://twitcasting.tv/streamserver.php";
        private readonly string _happyTokenUrl = "https://twitcasting.tv/happytoken.php";

        public TwitcastingClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// 取得直播後端相關資料
        /// </summary>
        /// <param name="channelId"></param>
        /// <returns>如果正在直播，則回傳 (<see langword="true"/>, <see cref="int">直播Id</see>)，否則為 (<see langword="false"/>, <see cref="int">0</see>)</returns>
        public async Task<(bool? IsLive, int? StreamId)> GetBackendStreamDataAsync(string channelId)
        {
            try
            {
                var json = await _httpClient.GetStringAsync($"{_streamServerUrl}?target={channelId}&mode=client");
                var data = JsonConvert.DeserializeObject<TcBackendStreamData>(json);
                return (data.Movie.Live, data.Movie.Id);
            }
            catch (Exception ex)
            {
                Log.Error($"TwitcastingClient.GetStreamData: {ex}");
                return (false, 0);
            }
        }

        /// <summary>
        /// 取得直播Token
        /// </summary>
        /// <param name="streamId">直播Id</param>
        /// <param name="cancellation"></param>
        /// <returns>回傳該直播的Token，如果直播為私人直播則回傳 <see cref="string.Empty"/></returns>
        private async Task<string> GetHappyTokenAsync(int streamId, CancellationToken cancellation = default)
        {
            int epochTimeStamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            using var content = new MultipartFormDataContent("------WebKitFormBoundary")
            {
                { new StringContent(streamId.ToString()), "movie_id" }
            };

            try
            {
                var response = await _httpClient.PostAsync($@"{_happyTokenUrl}?__n={epochTimeStamp}", content, cancellation);
                response.EnsureSuccessStatusCode();
                var data = await response.Content.ReadFromJsonAsync<TcHappyTokenData>();

                return data != null && !string.IsNullOrEmpty(data.Token) ? data.Token : string.Empty;
            }
            catch (Exception ex)
            {
                Log.Error($"TwitcastingClient.GetTwitcastingTokenAsync: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 取得直播前端相關資料
        /// </summary>
        /// <param name="channelId"></param>
        /// <returns>(直播標題, 直播副標題, 分類)</returns>
        public async Task<(string Title, string SubTitle, string Category)> GetFrontendStreamDataAsync(int streamId, string happyToken)
        {
            if (streamId <= 0)
                throw new FormatException(nameof(streamId));

            if (string.IsNullOrEmpty(happyToken))
                throw new ArgumentNullException(nameof(happyToken));

            try
            {
                var json = await _httpClient.GetStringAsync($"{_frontendApiUrl}/movies/{streamId}/status/viewer?token={happyToken}");
                var data = JsonConvert.DeserializeObject<TcFrontendStreamData>(json);
                return (data.Movie.Title, data.Movie.Telop, data.Movie.Category.Name);
            }
            catch (Exception ex)
            {
                Log.Error($"TwitcastingClient.GetStreamData: {ex}");
                return (string.Empty, string.Empty, string.Empty);
            }
        }
    }
}
