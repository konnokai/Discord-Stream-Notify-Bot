using Discord_Stream_Notify_Bot.HttpClients.TwitCasting;
using System.Net.Http.Json;

namespace Discord_Stream_Notify_Bot.HttpClients
{
    public class TwitcastingClient
    {
        private readonly HttpClient _httpClient;

        private readonly string _frontendApiUrl = "https://frontendapi.twitcasting.tv";
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
        public async Task<TcBackendStreamData> GetNewStreamDataAsync(string channelId)
        {
            try
            {
                var json = await _httpClient.GetStringAsync($"{_streamServerUrl}?target={channelId}&mode=client");
                if (json == "{}")
                    return null;

                var data = JsonConvert.DeserializeObject<TcBackendStreamData>(json);
                return data;
            }
            catch (Exception ex)
            {
                Log.Error($"TwitcastingClient.GetBackendStreamDataAsync: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 取得直播Token
        /// </summary>
        /// <param name="streamId">直播Id</param>
        /// <param name="cancellation"></param>
        /// <returns>回傳該直播的Token，如果直播為私人直播則回傳 <see cref="string.Empty"/></returns>
        public async Task<string> GetHappyTokenAsync(int streamId, CancellationToken cancellation = default)
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
            catch (HttpRequestException httpEx) when (httpEx.Message.ToLower().Contains("forbidden"))
            {
                return "403";
            }
            catch (Exception ex)
            {
                Log.Error($"TwitcastingClient.GetHappyTokenAsync: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 取得直播前端狀態相關資料
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="happyToken"></param>
        /// <returns>(直播標題, 直播副標題, 分類)</returns>
        public async Task<TcFrontendStreamStatusData> GetStreamStatusDataAsync(int streamId, string happyToken)
        {
            if (streamId <= 0)
                throw new FormatException(nameof(streamId));

            if (string.IsNullOrEmpty(happyToken))
                throw new ArgumentNullException(nameof(happyToken));

            try
            {
                int epochTimeStamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                var json = await _httpClient.GetStringAsync($"{_frontendApiUrl}/movies/{streamId}/status/viewer?token={happyToken}&__n={epochTimeStamp}");
                var data = JsonConvert.DeserializeObject<TcFrontendStreamStatusData>(json);
                return data;
            }
            catch (Exception ex)
            {
                Log.Error($"TwitcastingClient.GetFrontendStreamStatusDataAsync: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 取得直播開始時間
        /// </summary>
        /// <param name="streamId"></param>
        /// <param name="happyToken"></param>
        /// <returns>開始時間，如遇到錯誤則回傳現在時間</returns>
        public async Task<DateTime> GetStreamStartAtAsync(int streamId, string happyToken)
        {
            if (streamId <= 0)
                throw new FormatException(nameof(streamId));

            if (string.IsNullOrEmpty(happyToken))
                throw new ArgumentNullException(nameof(happyToken));

            try
            {
                int epochTimeStamp = (int)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                var json = await _httpClient.GetStringAsync($"{_frontendApiUrl}/movies/{streamId}/info?token={happyToken}&__n={epochTimeStamp}");
                var data = JsonConvert.DeserializeObject<TcFrontendStreamInfoData>(json);
                return UnixTimeStampToDateTime((double)data.StartedAt);
            }
            catch (Exception ex)
            {
                Log.Error($"TwitcastingClient.GetStreamStartAtAsync: {ex}");
                return DateTime.Now;
            }
        }

        // https://stackoverflow.com/questions/249760/how-can-i-convert-a-unix-timestamp-to-datetime-and-vice-versa
        private static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            // Unix timestamp is seconds past epoch
            DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dateTime = dateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dateTime;
        }
    }
}
