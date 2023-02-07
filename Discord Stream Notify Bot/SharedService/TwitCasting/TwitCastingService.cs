using Discord_Stream_Notify_Bot.HttpClients;
using Discord_Stream_Notify_Bot.Interaction;

namespace Discord_Stream_Notify_Bot.SharedService.Twitcasting
{
    public class TwitcastingService : IInteractionService
    {
        private readonly DiscordSocketClient _client;
        private readonly TwitcastingClient _twitcastingClient;

        public TwitcastingService(DiscordSocketClient client, TwitcastingClient twitcastingClient)
        {
            _client = client;
            _twitcastingClient = twitcastingClient;
        }

        public async Task<(string ChannelId, string ChannelTitle)> GetChannelIdAndTitleAsync(string channelUrl)
        {
            string channelId = channelUrl.Split('?')[0].Replace("https://twitcasting.tv/", "");
            if (string.IsNullOrEmpty(channelId))            
                return (string.Empty, string.Empty);

            string channelTitle = await GetChannelTitleAsync(channelId).ConfigureAwait(false);
            if (string.IsNullOrEmpty(channelTitle))            
                return (string.Empty, string.Empty);
            
            return (channelId, channelTitle);
        }

        public async Task<string> GetChannelTitleAsync(string channelId)
        {
            try
            {
                HtmlAgilityPack.HtmlWeb htmlWeb = new HtmlAgilityPack.HtmlWeb();
                var htmlDocument = await htmlWeb.LoadFromWebAsync($"https://twitcasting.tv/{channelId}");
                var htmlNodes = htmlDocument.DocumentNode.Descendants();
                var htmlNode = htmlNodes.SingleOrDefault((x) => x.Name == "span" && x.HasClass("tw-user-nav-name"));

                if (htmlNode != null)
                {
                    return htmlNode.InnerText.Trim();
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Error($"TwitcastingService-GetChannelNameAsync: {ex}");
                return null;
            }
        }
    }
}