using Discord_Stream_Notify_Bot;

public class BotConfig
{
    public string RedisOption { get; set; } = "127.0.0.1,syncTimeout=3000";
    public string RedisTokenKey { get; set; } = "";
    public string UptimeKumaPushUrl { get; set; } = "";

    public ulong DetectGuildId { get; set; } = 0;
    public ulong DetectCategoryId { get; set; } = 0;
    public string DiscordToken { get; set; } = "";
    public ulong TestSlashCommandGuildId { get; set; } = 0;
    public string WebHookUrl { get; set; } = "";

    public string GoogleApiKey { get; set; } = "";
    public string GoogleClientId { get; set; } = "";
    public string GoogleClientSecret { get; set; } = "";
    public string PubSubCallbackUrl { get; set; } = "";

    public string TwitCastingRecordPath { get; set; } = "";

    // https://streamlink.github.io/cli/plugins/twitch.html#authentication
    // 先放著，未來可能會用到
    public string TwitchCookieAuthToken { get; set; } = "";
    public string TwitchClientId { get; set; } = "";
    public string TwitchClientSecret { get; set; } = "";
    public string TwitchRecordPath { get; set; } = "";

    public string TwitterAuthToken { get; set; } = "";
    public string TwitterCSRFToken { get; set; } = "";
    public string TwitterSpaceRecordPath { get; set; } = "";

    public void InitBotConfig()
    {
        try { File.WriteAllText("bot_config_example.json", JsonConvert.SerializeObject(new BotConfig(), Formatting.Indented)); } catch { }
        if (!File.Exists("bot_config.json"))
        {
            Log.Error($"bot_config.json遺失，請依照 {Path.GetFullPath("bot_config_example.json")} 內的格式填入正確的數值");
            if (!Console.IsInputRedirected)
                Console.ReadKey();
            Environment.Exit(3);
        }

        var config = JsonConvert.DeserializeObject<BotConfig>(File.ReadAllText("bot_config.json"));

        try
        {
            if (string.IsNullOrWhiteSpace(config.DiscordToken))
            {
                Log.Error("DiscordToken遺失，請輸入至bot_config.json後重開Bot");
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
                Environment.Exit(3);
            }

            if (string.IsNullOrWhiteSpace(config.WebHookUrl))
            {
                Log.Error("WebHookUrl遺失，請輸入至bot_config.json後重開Bot");
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
                Environment.Exit(3);
            }

            if (string.IsNullOrWhiteSpace(config.GoogleApiKey))
            {
                Log.Error("GoogleApiKey遺失，請輸入至bot_config.json後重開Bot");
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
                Environment.Exit(3);
            }

            if (string.IsNullOrWhiteSpace(config.PubSubCallbackUrl))
            {
                Log.Error("PubSubCallbackUrl遺失，請輸入至bot_config.json後重開Bot");
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
                Environment.Exit(3);
            }

            DiscordToken = config.DiscordToken;
            WebHookUrl = config.WebHookUrl;
            PubSubCallbackUrl = config.PubSubCallbackUrl;
            GoogleApiKey = config.GoogleApiKey;
            TestSlashCommandGuildId = config.TestSlashCommandGuildId;
            TwitCastingRecordPath = config.TwitCastingRecordPath;
            TwitchCookieAuthToken = config.TwitchCookieAuthToken;
            TwitchClientId = config.TwitchClientId;
            TwitchClientSecret = config.TwitchClientSecret;
            TwitchRecordPath = config.TwitchRecordPath;
            TwitterAuthToken = config.TwitterAuthToken;
            TwitterCSRFToken = config.TwitterCSRFToken;
            TwitterSpaceRecordPath = config.TwitterSpaceRecordPath;
            GoogleClientId = config.GoogleClientId;
            GoogleClientSecret = config.GoogleClientSecret;
            RedisOption = config.RedisOption;
            RedisTokenKey = config.RedisTokenKey;
            UptimeKumaPushUrl = config.UptimeKumaPushUrl;
            DetectGuildId = config.DetectGuildId;
            DetectCategoryId = config.DetectCategoryId;

            if (string.IsNullOrWhiteSpace(config.RedisTokenKey) || string.IsNullOrWhiteSpace(RedisTokenKey))
            {
                Log.Error($"{nameof(RedisTokenKey)}遺失，將重新建立隨機亂數");

                RedisTokenKey = GenRandomKey();

                try { File.WriteAllText("bot_config.json", JsonConvert.SerializeObject(this, Formatting.Indented)); }
                catch (Exception ex)
                {
                    Log.Error($"設定檔保存失敗: {ex}");
                    Log.Error($"請手動將此字串填入設定檔中的 \"{nameof(RedisTokenKey)}\" 欄位: {RedisTokenKey}");
                    Environment.Exit(3);
                }
            }

            Utility.RedisKey = RedisTokenKey;
        }
        catch (Exception ex)
        {
            Log.Error($"設定檔讀取失敗: {ex}");
            throw;
        }
    }

    private static string GenRandomKey()
    {
        var characters = "ABCDEF_GHIJKLMNOPQRSTUVWXYZ@abcdefghijklmnopqrstuvwx-yz0123456789";
        var Charsarr = new char[128];
        var random = new Random();

        for (int i = 0; i < Charsarr.Length; i++)
        {
            Charsarr[i] = characters[random.Next(characters.Length)];
        }

        var resultString = new string(Charsarr);
        return resultString;
    }
}