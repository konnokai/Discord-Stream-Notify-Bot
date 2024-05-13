public static class UptimeKumaClient
{
    static Timer timerUptimeKumaPush;
    static DiscordSocketClient discordSocketClient;
    static HttpClient httpClient;
    static string uptimeKumaPushUrl;
    static bool isInit = false;

    public static bool Init(string uptimeKumaPushUrl, DiscordSocketClient discordSocketClient = null)
    {
        if (isInit)
            return false;

        if (string.IsNullOrEmpty(uptimeKumaPushUrl))
        {
            Log.Warn($"未設定 {nameof(uptimeKumaPushUrl)} 的網址，略過檢測");
            return false;
        }

        httpClient = new HttpClient();
        UptimeKumaClient.uptimeKumaPushUrl = uptimeKumaPushUrl.Split('?')[0];
        UptimeKumaClient.discordSocketClient = discordSocketClient;

        try
        {
            timerUptimeKumaPush = new Timer(async (state) => { await UptimeKumaTimerHandler(state); });
            timerUptimeKumaPush.Change(0, 30 * 1000);

            Log.Info("已註冊 Uptime Kuma 狀態檢測");
        }
        catch (Exception ex)
        {
            Log.Error($"UptimeKumaClient: {ex}");
        }

        isInit = true;
        return true;
    }

    private static async Task UptimeKumaTimerHandler(object state)
    {
        try
        {
            string latency = discordSocketClient.Latency.ToString() ?? "";
            var result = await httpClient.GetStringAsync($"{uptimeKumaPushUrl}?status=up&msg=OK&ping={latency}");
            if (result != "{\"ok\":true}")
            {
                Log.Error("Uptime Kuma 回傳錯誤");
                Log.Error(result);
            }
        }
        catch (TaskCanceledException timeout)
        {
            Log.Warn($"UptimeKumaTimerHandler-Timeout: {timeout.Message}");
        }
        catch (HttpRequestException requestEx)
        {
            if (requestEx.Message.Contains("500") || requestEx.Message.Contains("530"))
                return;

            Log.Error($"UptimeKumaTimerHandler-RequestError: {requestEx.Message}");
        }
        catch (Exception ex)
        {
            Log.Error($"UptimeKumaTimerHandler: {ex}");
        }
    }
}