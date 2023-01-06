using Discord.WebSocket;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public static class UptimeKumaClient
{
    static Timer timerUptimeKumaPush;
    static DiscordSocketClient _client;
    static HttpClient httpClient;
    static string uptimeKumaPushUrl;
    static bool isInit = false;

    public static bool Init(string uptimeKumaPushUrl, DiscordSocketClient discordSocketClient = null)
    {
        if (isInit)
            return false;
        isInit = true;

        if (string.IsNullOrEmpty(uptimeKumaPushUrl))
        {
            Log.Warn($"未設定{nameof(uptimeKumaPushUrl)}的網址，略過檢測");
            return false;
        }

        httpClient = new HttpClient();
        _client = discordSocketClient;
        UptimeKumaClient.uptimeKumaPushUrl = uptimeKumaPushUrl.Split('?')[0];

        try
        {
            timerUptimeKumaPush = new Timer(async (state) => { await UptimeKumaTimerHandler(state); });
            timerUptimeKumaPush.Change(0, 30 * 1000);

            Log.Info("已註冊Uptime Kuma狀態檢測");
        }
        catch (Exception ex)
        {
            Log.Error($"UptimeKumaClient: {ex}");
        }

        return true;
    }

    private static async Task UptimeKumaTimerHandler(object state)
    {
        try
        {
            string latency = _client.Latency.ToString() ?? "";
            var result = await httpClient.GetStringAsync($"{uptimeKumaPushUrl}?status=up&msg=OK&ping={latency}");
            if (result != "{\"ok\":true}")
            {
                Log.Error("Uptime Kuma回傳錯誤");
                Log.Error(result);
            }
        }
        catch (TaskCanceledException timeout)
        {
            Log.Error($"UptimeKumaTimerHandler-Timeout: {timeout.Message}");
        }
        catch (HttpRequestException requestEx)
        {
            Log.Error($"UptimeKumaTimerHandler-RequestError: {requestEx.Message}");
        }
        catch (Exception ex)
        {
            Log.Error($"UptimeKumaTimerHandler: {ex}");
        }
    }
}