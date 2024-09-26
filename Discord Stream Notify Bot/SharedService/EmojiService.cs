using Discord_Stream_Notify_Bot.Interaction;

namespace Discord_Stream_Notify_Bot.SharedService
{
    public class EmojiService : IInteractionService
    {
        // 好像要把 Emote Id 給拉到 BotConfig 設定還是怎樣的
        public Emote YouTubeEmote
        {
            get
            {
#if !RELEASE
                return null;
#endif
                if (youTubeEmote == null)
                {
                    try
                    {
                        Task.Run(async () => youTubeEmote = await _client.GetApplicationEmoteAsync(1265158558299848827));
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"無法取得YouTube Emote: {ex}");
                        youTubeEmote = null;
                    }
                }
                return youTubeEmote;
            }
        }

        public Emote PatreonEmote
        {
            get
            {
#if !RELEASE
                return null;
#endif
                if (patreonEmote == null)
                {
                    try
                    {
                        Task.Run(async () => patreonEmote = await _client.GetApplicationEmoteAsync(1265158902962458769));
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"無法取得Patreon Emote: {ex}");
                        patreonEmote = null;
                    }
                }
                return patreonEmote;
            }
        }

        public Emote PayPalEmote
        {
            get
            {
#if !RELEASE
                return null;
#endif
                if (payPalEmote == null)
                {
                    try
                    {
                        Task.Run(async () => payPalEmote = await _client.GetApplicationEmoteAsync(1265158658015236107));
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"無法取得PayPal Emote: {ex}");
                        payPalEmote = null;
                    }
                }
                return payPalEmote;
            }
        }

        private Emote youTubeEmote, patreonEmote, payPalEmote;
        private readonly DiscordSocketClient _client;

        public EmojiService(DiscordSocketClient client)
        {
            _client = client;
        }
    }
}
