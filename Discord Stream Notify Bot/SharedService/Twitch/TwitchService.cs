using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using TwitchLib.Api;
using Stream = TwitchLib.Api.Helix.Models.Streams.GetStreams.Stream;
using User = TwitchLib.Api.Helix.Models.Users.GetUsers.User;

namespace Discord_Stream_Notify_Bot.SharedService.Twitch
{
    public class TwitchService : IInteractionService
    {
        public bool IsEnable { get; private set; } = true;

        private Regex UserLoginRegex { get; } = new(@"twitch.tv/(?<name>[\w\d\-_]+)/?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly EmojiService _emojiService;
        private readonly DiscordSocketClient _client;
        private readonly Lazy<TwitchAPI> _twitchApi;
        private readonly Timer _timer;
        private string twitchRecordPath;
        private bool isRuning = false;
        private HashSet<string> hashSet = new();

        public TwitchService(DiscordSocketClient client, BotConfig botConfig, EmojiService emojiService)
        {
            if (string.IsNullOrEmpty(botConfig.TwitchClientId) || string.IsNullOrEmpty(botConfig.TwitchClientSecret))
            {
                Log.Warn($"{nameof(botConfig.TwitchClientId)} 或 {nameof(botConfig.TwitchClientSecret)} 遺失，無法運行 Twitch 類功能");
                IsEnable = false;
                return;
            }

            twitchRecordPath = botConfig.TwitchRecordPath;
            if (string.IsNullOrEmpty(twitchRecordPath)) twitchRecordPath = Program.GetDataFilePath("");
            if (!twitchRecordPath.EndsWith(Program.GetPlatformSlash())) twitchRecordPath += Program.GetPlatformSlash();

            _client = client;
            _emojiService = emojiService;

            _twitchApi = new(() => new()
            {
                Helix =
                {
                    Settings =
                    {
                        ClientId = botConfig.TwitchClientId,
                        Secret = botConfig.TwitchClientSecret
                    }
                }
            });

            _timer = new Timer(async (obj) => { await TimerHandel(); },
                null, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1));
        }

        public string GetUserLoginByUrl(string url)
        {
            url = url.Split('?')[0];

            var match = UserLoginRegex.Match(url);
            if (match.Success)
            {
                url = match.Groups["name"].Value;
            }

            return url;
        }

        public async Task<User> GetUserAsync(string twitchUserLogin)
        {
            try
            {
                string accessToken = await _twitchApi.Value.Auth.GetAccessTokenAsync();

                if (string.IsNullOrEmpty(accessToken))
                {
                    Log.Warn($"Access Token 獲取失敗，請確認 {nameof(BotConfig.TwitchClientId)} 或 {nameof(BotConfig.TwitchClientSecret)} 是否正常");
                    return null;
                }

                var users = await _twitchApi.Value.Helix.Users.GetUsersAsync(logins: new List<string>() { twitchUserLogin }, accessToken: accessToken);
                return users.Users.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"無法取得 Twitch 資料，請確認 {nameof(BotConfig.TwitchClientId)} 或 {nameof(BotConfig.TwitchClientSecret)} 是否正常");
                return null;
            }
        }

        public async Task<IReadOnlyList<User>> GetUsersAsync(params string[] twitchUserLogins)
        {
            try
            {
                string accessToken = await _twitchApi.Value.Auth.GetAccessTokenAsync();

                if (string.IsNullOrEmpty(accessToken))
                {
                    Log.Warn($"Access Token 獲取失敗，請確認 {nameof(BotConfig.TwitchClientId)} 或 {nameof(BotConfig.TwitchClientSecret)} 是否正常");
                    return Array.Empty<User>();
                }

                List<User> result = new();
                foreach (var item in twitchUserLogins.Chunk(100))
                {
                    var users = await _twitchApi.Value.Helix.Users.GetUsersAsync(logins: new List<string>(twitchUserLogins), accessToken: accessToken);
                    if (users.Users.Any())
                    {
                        result.AddRange(users.Users);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"無法取得 Twitch 資料，請確認 {nameof(BotConfig.TwitchClientId)} 或 {nameof(BotConfig.TwitchClientSecret)} 是否正常");
                return Array.Empty<User>();
            }
        }

        public async Task<IReadOnlyList<Stream>> GetNowStreamsAsync(params string[] twitchUserLogins)
        {
            try
            {
                string accessToken = await _twitchApi.Value.Auth.GetAccessTokenAsync();

                if (string.IsNullOrEmpty(accessToken))
                {
                    Log.Warn($"Access Token 獲取失敗，請確認 {nameof(BotConfig.TwitchClientId)} 或 {nameof(BotConfig.TwitchClientSecret)} 是否正常");
                    return Array.Empty<Stream>();
                }

                List<Stream> result = new();
                foreach (var item in twitchUserLogins.Chunk(100))
                {
                    var streams = await _twitchApi.Value.Helix.Streams.GetStreamsAsync(first: 100, userLogins: new List<string>(twitchUserLogins), accessToken: accessToken);
                    if (streams.Streams.Any())
                    {
                        result.AddRange(streams.Streams);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"無法取得 Twitch 資料，請確認 {nameof(BotConfig.TwitchClientId)} 或 {nameof(BotConfig.TwitchClientSecret)} 是否正常");
                return Array.Empty<Stream>();
            }
        }

        private async Task TimerHandel()
        {
            if (isRuning) return;
            isRuning = true;

            using var twitchStreamDb = TwitchStreamContext.GetDbContext();
            using var db = DBContext.GetDbContext();

            try
            {
                foreach (var twitchSpiders in db.TwitchSpider.Distinct((x) => x.UserLogin).Chunk(100))
                {
                    var streams = await GetNowStreamsAsync(twitchSpiders.Select((x) => x.UserLogin).ToArray());
                    if (!streams.Any())
                        continue;

                    foreach (var stream in streams)
                    {
                        if (hashSet.Contains(stream.Id))
                            continue;

                        hashSet.Add(stream.Id);

                        if (twitchStreamDb.TwitchStreams.Any((x) => x.StreamId == stream.Id))
                            continue;

                        var twitchSpider = twitchSpiders.Single((x) => x.UserLogin == stream.UserLogin);

                        var userData = await GetUserAsync(twitchSpider.UserLogin);
                        twitchSpider.OfflineImageUrl = userData.OfflineImageUrl;
                        twitchSpider.ProfileImageUrl = userData.ProfileImageUrl;
                        twitchSpider.UserName = userData.DisplayName;
                        db.TwitchSpider.Update(twitchSpider);

                        try
                        {
                            var twitchStream = new TwitchStream()
                            {
                                StreamId = stream.Id,
                                StreamTitle = stream.Title,
                                GameName = stream.GameName,
                                ThumbnailUrl = stream.ThumbnailUrl.Replace("{width}", "854").Replace("{height}", "480"),
                                UserLogin = stream.UserLogin,
                                UserName = stream.UserName,
                                StreamStartAt = stream.StartedAt
                            };

                            twitchStreamDb.TwitchStreams.Add(twitchStream);

                            await SendStreamMessageAsync(twitchStream, twitchSpider, twitchSpider.IsRecord && RecordTwitch(twitchStream));
                        }
                        catch (Exception ex) { Log.Error(ex, $"TwitchService-GetData: {twitchSpider.UserLogin}"); }
                    }

                    await Task.Delay(1000); // 等個一秒鐘避免觸發429之類的錯誤，雖然也不知道有沒有用
                }
            }
            catch (Exception ex) { Log.Error(ex, "TwitchService-Timer"); }
            finally { isRuning = false; }

            try
            {
                twitchStreamDb.SaveChanges();
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TwitchService-Timer: SaveDb Error");
            }
        }

        private async Task SendStreamMessageAsync(TwitchStream twitchStream, TwitchSpider twitchSpider, bool isRecord = false)
        {
#if DEBUG || DEBUG_DONTREGISTERCOMMAND
            Log.New($"Twitch 開台通知: {twitchStream.UserName} - {twitchStream.StreamTitle}");
#else
            using (var db = DBContext.GetDbContext())
            {
                var noticeGuildList = db.NoticeTwitchStreamChannels.Where((x) => x.NoticeTwitchUserLogin == twitchStream.UserLogin).ToList();
                Log.New($"發送 Twitch 開台通知 ({noticeGuildList.Count}): {twitchStream.UserName} - {twitchStream.StreamTitle}");

                EmbedBuilder embedBuilder = new EmbedBuilder()
                    .WithTitle(twitchStream.StreamTitle)
                    .WithDescription(Format.Url($"{twitchStream.UserName}", $"https://twitch.tv/{twitchStream.UserLogin}"))
                    .WithUrl($"https://twitch.tv/{twitchStream.UserLogin}")
                    .WithThumbnailUrl(twitchSpider.ProfileImageUrl)
                    .WithImageUrl(twitchStream.ThumbnailUrl);

                if (!string.IsNullOrEmpty(twitchStream.GameName)) embedBuilder.AddField("遊戲", twitchStream.GameName, true);

                embedBuilder.AddField("開始時間", twitchStream.StreamStartAt.ConvertDateTimeToDiscordMarkdown());

                if (isRecord) embedBuilder.WithRecordColor();
                else embedBuilder.WithOkColor();

                MessageComponent comp = new ComponentBuilder()
                        .WithButton("贊助小幫手 (Patreon) #ad", style: ButtonStyle.Link, emote: _emojiService.PatreonEmote, url: Utility.PatreonUrl, row: 1)
                        .WithButton("贊助小幫手 (Paypal) #ad", style: ButtonStyle.Link, emote: _emojiService.PayPalEmote, url: Utility.PaypalUrl, row: 1).Build();

                foreach (var item in noticeGuildList)
                {
                    try
                    {
                        var guild = _client.GetGuild(item.GuildId);
                        if (guild == null) continue;
                        var channel = guild.GetTextChannel(item.DiscordChannelId);
                        if (channel == null) continue;

                        var message = await channel.SendMessageAsync(item.StartStreamMessage, false, embedBuilder.Build(), components: comp);

                        try
                        {
                            if (channel is INewsChannel)
                                await message.CrosspostAsync();
                        }
                        catch (Discord.Net.HttpException httpEx) when (httpEx.DiscordCode == DiscordErrorCode.MessageAlreadyCrossposted)
                        {
                            // ignore
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Notice Twitch {item.GuildId} / {item.DiscordChannelId}\n{ex.Message}");
                        if (ex.Message.Contains("50013") || ex.Message.Contains("50001")) db.NoticeTwitchStreamChannels.RemoveRange(db.NoticeTwitchStreamChannels.Where((x) => x.DiscordChannelId == item.DiscordChannelId));
                        db.SaveChanges();
                    }
                }
            }
#endif
        }

        private bool RecordTwitch(TwitchStream twitchStream)
        {
            Log.Info($"{twitchStream.UserName} ({twitchStream.StreamId}): {twitchStream.StreamTitle}");

            try
            {
                if (!Directory.Exists(twitchRecordPath))
                    Directory.CreateDirectory(twitchRecordPath);
            }
            catch (Exception ex)
            {
                Log.Error($"Twitch 保存路徑不存在且不可建立: {twitchRecordPath}");
                Log.Error($"更改保存路徑至 Data 資料夾: {Program.GetDataFilePath("")}");
                Log.Error(ex.ToString());

                twitchRecordPath = Program.GetDataFilePath("");
            }

            string procArgs = $"streamlink --twitch-disable-ads https://twitch.tv/{twitchStream.UserLogin} best --output \"{twitchRecordPath}[{twitchStream.UserLogin}]{twitchStream.StreamStartAt:yyyyMMdd} - {twitchStream.StreamId}.ts\"";
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) Process.Start("tmux", $"new-window -d -n \"Twitch {twitchStream.UserLogin}\" {procArgs}");
                else Process.Start(new ProcessStartInfo()
                {
                    FileName = "streamlink",
                    Arguments = procArgs.Replace("streamlink", ""),
                    CreateNoWindow = false,
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "RecordTwitch 失敗，請確認是否已安裝 StreamLink");
                return false;
            }
        }

    }
}
