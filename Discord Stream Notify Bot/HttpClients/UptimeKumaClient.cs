using Discord.WebSocket;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.HttpClients
{
    public class UptimeKumaClient
    {
        Timer timerUptimeKumaPush;
        DiscordSocketClient _client;
        HttpClient httpClient;
        string uptimeKumaPushUrl;
        bool isInit = false;

        public UptimeKumaClient(HttpClient httpClient, DiscordSocketClient discordSocketClient, BotConfig botConfig)
        {
            if (string.IsNullOrEmpty(botConfig.UptimeKumaPushUrl))
            {
                Log.Warn($"{nameof(botConfig.UptimeKumaPushUrl)} 未設定，略過狀態檢測");
                return;
            }

            this.httpClient = httpClient;
            _client = discordSocketClient;
            uptimeKumaPushUrl = botConfig.UptimeKumaPushUrl.Split('?')[0];

            try
            {
                timerUptimeKumaPush = new Timer(async (state) => { await UptimeKumaTimerHandler(state); });
                timerUptimeKumaPush.Change(0, 60 * 1000);

                Log.Info("已註冊Uptime Kuma狀態檢測");
            }
            catch (System.Exception ex)
            {
                Log.Error($"UptimeKumaClient: {ex}");
            }
        }

        public void Init()
        {
            if (isInit) 
                return;

            isInit = true;
        }

        private async Task UptimeKumaTimerHandler(object state)
        {
            try
            {
                var result = await httpClient.GetStringAsync($"{uptimeKumaPushUrl}?status=up&msg=OK&ping={_client.Latency}");
                if (result != "{\"ok\":true}")
                {
                    Log.Error("Uptime Kuma回傳錯誤");
                    Log.Error(result);
                }                
            }
            catch (System.Exception ex)
            {
                Log.Error($"UptimeKumaTimerHandler: {ex}");
            }
        }
    }
}
