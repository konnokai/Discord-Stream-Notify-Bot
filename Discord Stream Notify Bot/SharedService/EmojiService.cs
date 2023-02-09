using Discord_Stream_Notify_Bot.Interaction;

namespace Discord_Stream_Notify_Bot.SharedService
{
    public class EmojiService : IInteractionService
    {

        public Emote YouTubeEmote
        {
            get
            {
                if (youTubeEmote == null)
                {
                    try
                    {
                        youTubeEmote = _client.Guilds.FirstOrDefault((x) => x.Id == 1040482713213345872).Emotes.FirstOrDefault((x) => x.Id == 1041913109926903878);
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
                if (patreonEmote == null)
                {
                    try
                    {
                        patreonEmote = _client.Guilds.FirstOrDefault((x) => x.Id == 1040482713213345872).Emotes.FirstOrDefault((x) => x.Id == 1041988445830119464);
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
                if (payPalEmote == null)
                {
                    try
                    {
                        payPalEmote = _client.Guilds.FirstOrDefault((x) => x.Id == 1040482713213345872).Emotes.FirstOrDefault((x) => x.Id == 1042004146208899102);
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
