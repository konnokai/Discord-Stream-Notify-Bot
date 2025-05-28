using Discord_Stream_Notify_Bot.Interaction;
using Dorssel.Utilities;
using System.Collections.Concurrent;
using static Discord_Stream_Notify_Bot.SharedService.Twitch.TwitchService;

namespace Discord_Stream_Notify_Bot.SharedService.Twitch.Debounce
{
    // https://blog.darkthread.net/blog/dotnet-debounce/
    // https://github.com/dorssel/dotnet-debounce
    internal class DebounceChannelUpdateMessage
    {
        private readonly Debouncer _debouncer;
        private readonly TwitchService _twitchService;
        private readonly string _twitchUserName, _twitchUserLogin, _twitchUserId;
        private readonly ConcurrentQueue<string> messageQueue = new();

        public DebounceChannelUpdateMessage(TwitchService twitchService, string twitchUserName, string twitchUserLogin, string twitchUserId)
        {
            _twitchService = twitchService;
            _twitchUserName = twitchUserName;
            _twitchUserLogin = twitchUserLogin;
            _twitchUserId = twitchUserId;

            _debouncer = new()
            {
                DebounceWindow = TimeSpan.FromMinutes(1),
                DebounceTimeout = TimeSpan.FromMinutes(3),
            };
            _debouncer.Debounced += _debouncer_Debounced;
        }

        private void _debouncer_Debounced(object sender, DebouncedEventArgs e)
        {
            try
            {
                Log.Info($"{_twitchUserLogin} 發送頻道更新通知 (Debouncer 觸發數量: {e.Count})");

                var description = string.Join("\n\n", messageQueue);

                var embedBuilder = new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle($"{_twitchUserName} 直播資料更新")
                    .WithUrl($"https://twitch.tv/{_twitchUserLogin}")
                    .WithDescription(description);

                using var db = Bot.DbService.GetDbContext();
                var twitchSpider = db.TwitchSpider.AsNoTracking().FirstOrDefault((x) => x.UserId == _twitchUserId);
                if (twitchSpider != null)
                    embedBuilder.WithThumbnailUrl(twitchSpider.ProfileImageUrl);

                Task.Run(async () => { await _twitchService.SendStreamMessageAsync(_twitchUserId, embedBuilder.Build(), NoticeType.ChangeStreamData); });
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), $"{_twitchUserLogin} 訊息去抖動失敗");
            }
            finally
            {
                messageQueue.Clear();
                _debouncer.Reset();
            }
        }

        public void AddMessage(string message)
        {
            Log.Debug($"DebounceChannelUpdateMessage ({_twitchUserLogin}): {message}");

            messageQueue.Enqueue(message);
            _debouncer.Trigger();
        }

        bool isDisposed;
        public void Dispose()
        {
            if (!isDisposed)
            {
                _debouncer.Debounced -= _debouncer_Debounced;
                _debouncer.Dispose();
                isDisposed = true;
            }
        }
    }
}
