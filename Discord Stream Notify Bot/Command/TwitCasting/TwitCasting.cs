using Discord.Commands;
using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.HttpClients;

namespace Discord_Stream_Notify_Bot.Command.TwitCasting
{
    public class TwitCasting : TopLevelModule
    {
        private readonly MainDbService _mainDbService;
        private readonly TwitcastingClient _twitcastingClient;

        public TwitCasting(MainDbService mainDbService, TwitcastingClient twitcastingClient)
        {
            _mainDbService = mainDbService;
            _twitcastingClient = twitcastingClient;
        }

        [RequireContext(ContextType.DM)]
        [Command("FixTCDb")]
        [Alias("ftcdb")]
        [RequireOwner]
        public async Task FixTCDbAsync()
        {
            await Context.Channel.TriggerTypingAsync().ConfigureAwait(false);

            using var db = _mainDbService.GetDbContext();
            var needFixList = db.TwitcastingSpider.Where((x) => string.IsNullOrEmpty(x.ChannelId)).ToList();

            foreach (var spider in needFixList)
            {
                var userInfo = await _twitcastingClient.GetUserInfoAsync(spider.ScreenId).ConfigureAwait(false);
                if (userInfo != null)
                {
                    spider.ChannelId = userInfo.User.Id;
                }
                else
                {
                    Log.Error($"Failed to fix TwitCasting Spider for ScreenId: {spider.ScreenId}");
                }
            }

            await db.SaveChangesAsync().ConfigureAwait(false);

            await Context.Channel.SendConfirmAsync("Fix TwitCasting Spider Database", $"Fixed {needFixList.Count} entries.").ConfigureAwait(false);
        }
    }
}
