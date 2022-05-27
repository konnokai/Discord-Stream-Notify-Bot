using Newtonsoft.Json;
using System;
using System.IO;

public class BotConfig
{
    public string DiscordToken { get; set; } = "";
    public string GoogleApiKey { get; set; } = "";
    public string GoogleClientId { get; set; } = "";
    public string GoogleClientSecret { get; set; } = "";
    public string RedisOption { get; set; } = "127.0.0.1,syncTimeout=3000";
    public ulong TestSlashCommandGuildId { get; set; } = 0;
    public string TwitterApiKey { get; set; } = "";
    public string TwitterApiKeySecret { get; set; } = "";
    public string TwitterSpaceRecordPath { get; set; } = "";
    public string WebHookUrl { get; set; } = "";

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

            //if (string.IsNullOrWhiteSpace(config.TwitterApiKey))
            //{
            //    Log.Error("TwitterApiKey遺失，請輸入至bot_config.json後重開Bot");
            //    if (!Console.IsInputRedirected)
            //        Console.ReadKey();
            //    Environment.Exit(3);
            //}

            //if (string.IsNullOrWhiteSpace(config.TwitterApiKeySecret))
            //{
            //    Log.Error("TwitterApiKeySecret遺失，請輸入至bot_config.json後重開Bot");
            //    if (!Console.IsInputRedirected)
            //        Console.ReadKey();
            //    Environment.Exit(3);
            //}

            //if (string.IsNullOrWhiteSpace(config.GoogleClientId))
            //{
            //    Log.Error("GoogleClientId遺失，請輸入至credentials.json後重開Bot");
            //    if (!Console.IsInputRedirected)
            //        Console.ReadKey();
            //    Environment.Exit(3);
            //}

            //if (string.IsNullOrWhiteSpace(config.GoogleClientSecret))
            //{
            //    Log.Error("GoogleClientSecret遺失，請輸入至credentials.json後重開Bot");
            //    if (!Console.IsInputRedirected)
            //        Console.ReadKey();
            //    Environment.Exit(3);
            //}

            DiscordToken = config.DiscordToken;
            WebHookUrl = config.WebHookUrl;
            GoogleApiKey = config.GoogleApiKey;
            TwitterApiKey = config.TwitterApiKey;
            TwitterApiKeySecret = config.TwitterApiKeySecret;
            TwitterSpaceRecordPath = config.TwitterSpaceRecordPath;
            GoogleClientId = config.GoogleClientId;
            GoogleClientSecret = config.GoogleClientSecret;
            RedisOption = config.RedisOption;
            TestSlashCommandGuildId = config.TestSlashCommandGuildId;
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            throw;
        }
    }
}