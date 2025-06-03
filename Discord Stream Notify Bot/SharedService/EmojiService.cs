using Discord_Stream_Notify_Bot.Interaction;

namespace Discord_Stream_Notify_Bot.SharedService
{
    public class EmojiService : IInteractionService
    {
        public Emote YouTubeEmote { get; private set; }
        public Emote PayPalEmote { get; private set; }
        public Emote ECPayEmote { get; private set; }

        private readonly DiscordSocketClient _client;

        public EmojiService(DiscordSocketClient client, BotConfig botConfig)
        {
            _client = client;

            try
            {
                YouTubeEmote = _client.GetApplicationEmoteAsync(botConfig.YouTubeEmoteId).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error($"無法取得 YouTube Emote: {ex}");
                YouTubeEmote = null;
            }

            try
            {
                PayPalEmote = _client.GetApplicationEmoteAsync(botConfig.PayPalEmoteId).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error($"無法取得 PayPal Emote: {ex}");
                PayPalEmote = null;
            }

            try
            {
                ECPayEmote = _client.GetApplicationEmoteAsync(botConfig.ECPayEmoteId).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Error($"無法取得 ECPay Emote: {ex}");
                ECPayEmote = null;
            }
        }
    }
}
