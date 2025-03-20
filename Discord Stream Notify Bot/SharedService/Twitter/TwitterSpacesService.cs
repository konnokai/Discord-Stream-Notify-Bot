using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.HttpClients;
using Discord_Stream_Notify_Bot.HttpClients.Twitter;
using Discord_Stream_Notify_Bot.Interaction;
using System.Diagnostics;
using System.Runtime.InteropServices;

#if RELEASE
using Microsoft.EntityFrameworkCore;
using Polly;
#endif

namespace Discord_Stream_Notify_Bot.SharedService.Twitter
{
    public class TwitterSpacesService : IInteractionService
    {
        internal bool IsEnable { get; private set; } = true;

        private readonly DiscordSocketClient _client;
        private readonly EmojiService _emojiService;
        private readonly TwitterClient _twitterClient;
        private readonly Timer timer;
        private readonly HashSet<string> hashSet = new HashSet<string>();
        private readonly MainDbService _dbService;

        private bool isRuning = false;
        private string twitterSpaceRecordPath = "";

        public TwitterSpacesService(DiscordSocketClient client, TwitterClient twitterClient, BotConfig botConfig, EmojiService emojiService, MainDbService dbService)
        {
            if (string.IsNullOrWhiteSpace(botConfig.TwitterAuthToken) || string.IsNullOrWhiteSpace(botConfig.TwitterCSRFToken))
            {
                Log.Warn($"{nameof(BotConfig.TwitterAuthToken)} 或 {nameof(BotConfig.TwitterCSRFToken)} 遺失，無法運行推特類功能");
                IsEnable = false;
                return;
            }

            _client = client;
            _twitterClient = twitterClient;
            _emojiService = emojiService;
            _dbService = dbService;

            twitterSpaceRecordPath = botConfig.TwitterSpaceRecordPath;
            if (string.IsNullOrEmpty(twitterSpaceRecordPath)) twitterSpaceRecordPath = Utility.GetDataFilePath("");
            if (!twitterSpaceRecordPath.EndsWith(Utility.GetPlatformSlash())) twitterSpaceRecordPath += Utility.GetPlatformSlash();

#if DEBUG_API
            Task.Run(async () => await _twitterClient.GetQueryIdAndFeatureSwitchesAsync());
            return;
#elif !RELEASE
            return;
#endif

            timer = new(async (stats) =>
            {
                if (isRuning)
                    return;

                isRuning = true;

                try
                {
                    using (var db = _dbService.GetDbContext())
                    {
                        var userList = db.TwitterSpaceSpider.Select((x) => x.UserId).ToArray();

                        for (int i = 0; i < userList.Length; i += 100)
                        {
                            try
                            {
                                var spaces = await _twitterClient.GetTwitterSpaceByUsersIdAsync(userList.Skip(i).Take(100).ToArray());
                                if (spaces.Count <= 0) continue;

                                foreach (var item in spaces)
                                {
                                    if (hashSet.Contains(item.SpaceId))
                                        continue;

                                    hashSet.Add(item.SpaceId);

                                    if (db.TwitterSpace.Any((x) => x.SpaecId == item.SpaceId))
                                        continue;

                                    try
                                    {
                                        var user = db.TwitterSpaceSpider.FirstOrDefault((x) => x.UserId == item.UserId);
                                        var userData = await GetTwitterUserAsync(user.UserScreenName);

                                        if (user.UserScreenName != userData.Legacy.ScreenName || user.UserName != userData.Legacy.Name)
                                        {
                                            user.UserScreenName = userData.Legacy.ScreenName;
                                            user.UserName = userData.Legacy.Name;
                                            db.SaveChanges();
                                        }

                                        string masterUrl = "";
                                        try
                                        {
                                            var metadataJson = await _twitterClient.GetTwitterSpaceMetadataAsync(item.SpaceId);
                                            masterUrl = (await _twitterClient.GetTwitterSpaceMasterUrlAsync(metadataJson["media_key"].ToString())).Replace(" ", "");
                                        }
                                        catch (Exception ex)
                                        {
                                            Log.Error(ex, $"GetTwitterSpaceMasterUrl: {item.SpaceId}");
                                            continue;
                                        }

                                        var spaceData = new DataBase.Table.TwitterSpace() { UserId = item.UserId, UserName = user.UserName, UserScreenName = user.UserScreenName, SpaecId = item.SpaceId, SpaecTitle = item.SpaceTitle, SpaecActualStartTime = item.StartAt ?? DateTime.Now, SpaecMasterPlaylistUrl = masterUrl.Replace("dynamic_playlist.m3u8?type=live", "master_playlist.m3u8") };

                                        if (string.IsNullOrEmpty(spaceData.SpaecTitle))
                                            spaceData.SpaecTitle = $"語音空間 ({spaceData.SpaecActualStartTime:yyyy/MM/dd})";

                                        db.TwitterSpace.Add(spaceData);

                                        if (IsRecordSpace(spaceData) && !string.IsNullOrEmpty(masterUrl))
                                        {
                                            try
                                            {
                                                RecordSpace(spaceData, masterUrl);
                                                await SendSpaceMessageAsync(userData, spaceData, true);
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.Error($"Spaces-Record {item.SpaceId}: {ex}");
                                                await SendSpaceMessageAsync(userData, spaceData);
                                            }
                                        }
                                        else await SendSpaceMessageAsync(userData, spaceData);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error($"Spaces-Data {item.SpaceId}: {ex}");
                                    }
                                }

                                db.SaveChanges();
                            }
                            catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                            {
                                Log.Warn($"Prepare-Spaces: 429錯誤");
                            }
                            catch (Exception ex)
                            {
                                if (!ex.Message.Contains("50") && !ex.Message.Contains("temporarily unavailable"))
                                    Log.Error(ex, "Prepare-Spaces");
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Error(ex, "Spaces-Timer"); }
                finally { isRuning = false; }
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(120));
        }

        public async Task<Result> GetTwitterUserAsync(string userScreenName)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
                return null;

            try
            { return (await _twitterClient.GetUserDataByScreenNameAsync(userScreenName)).Data.User.Result; }
            catch (Exception)
            { return null; }
        }

        private bool IsRecordSpace(DataBase.Table.TwitterSpace twitterSpace)
        {
            using (var db = _dbService.GetDbContext())
            {
                var item = db.TwitterSpaceSpider.FirstOrDefault((x) => x.UserId == twitterSpace.UserId);
                if (item == null)
                    return false;

                try
                {
                    return item.IsRecord;
                }
                catch (Exception ex)
                {
                    Log.Error($"IsRecordSpace: {twitterSpace.SpaecId} {ex.Message}\n{ex.StackTrace}");
                    return false;
                }
            }
        }

        private async Task SendSpaceMessageAsync(Result userModel, DataBase.Table.TwitterSpace twitterSpace, bool isRecord = false)
        {
#if !RELEASE
            Log.New($"推特空間開台通知: {twitterSpace.UserScreenName} - {twitterSpace.SpaecTitle}");
#else
            using (var db = _dbService.GetDbContext())
            {
                var noticeGuildList = db.NoticeTwitterSpaceChannel.AsNoTracking().Where((x) => x.NoticeTwitterSpaceUserId == twitterSpace.UserId).ToList();
                Log.New($"發送推特空間開台通知 ({noticeGuildList.Count}): {twitterSpace.UserScreenName} - {twitterSpace.SpaecTitle}");

                EmbedBuilder embedBuilder = new EmbedBuilder()
                    .WithTitle(twitterSpace.SpaecTitle)
                    .WithDescription(Format.Url($"{twitterSpace.UserName}", $"https://twitter.com/{twitterSpace.UserScreenName}"))
                    .WithUrl($"https://twitter.com/i/spaces/{twitterSpace.SpaecId}/peek")
                    .WithThumbnailUrl(userModel.Legacy.ProfileImageUrlHttps)
                    .AddField("開始時間", twitterSpace.SpaecActualStartTime.ConvertDateTimeToDiscordMarkdown());

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
                        if (guild == null)
                        {
                            Log.Warn($"Twitter Space 通知 ({item.DiscordChannelId}) | 找不到伺服器 {item.GuildId}");
                            db.NoticeTwitterSpaceChannel.RemoveRange(db.NoticeTwitterSpaceChannel.Where((x) => x.GuildId == item.GuildId));
                            db.SaveChanges();
                            continue;
                        }

                        var channel = guild.GetTextChannel(item.DiscordChannelId);
                        if (channel == null) continue;

                        await Policy.Handle<TimeoutException>()
                            .Or<Discord.Net.HttpException>((httpEx) => ((int)httpEx.HttpCode).ToString().StartsWith("50"))
                            .WaitAndRetryAsync(3, (retryAttempt) =>
                            {
                                var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                                Log.Warn($"{item.GuildId} / {item.DiscordChannelId} 發送失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                                return timeSpan;
                            })
                            .ExecuteAsync(async () =>
                            {
                                var message = await channel.SendMessageAsync(text: item.StratTwitterSpaceMessage, embed: embedBuilder.Build(), components: comp, options: new RequestOptions() { RetryMode = RetryMode.AlwaysRetry });

                                try
                                {
                                    if (channel is INewsChannel && Utility.OfficialGuildList.Contains(guild.Id))
                                        await message.CrosspostAsync();
                                }
                                catch (Discord.Net.HttpException httpEx) when (httpEx.DiscordCode == DiscordErrorCode.MessageAlreadyCrossposted)
                                {
                                    // ignore
                                }
                            });
                    }
                    catch (Discord.Net.HttpException httpEx)
                    {
                        if (httpEx.DiscordCode.HasValue && (httpEx.DiscordCode.Value == DiscordErrorCode.InsufficientPermissions || httpEx.DiscordCode.Value == DiscordErrorCode.MissingPermissions))
                        {
                            Log.Warn($"Twitter Space 通知 - 遺失權限 {item.GuildId} / {item.DiscordChannelId}");
                            db.NoticeTwitterSpaceChannel.RemoveRange(db.NoticeTwitterSpaceChannel.Where((x) => x.DiscordChannelId == item.DiscordChannelId));
                            db.SaveChanges();
                        }
                        else if (((int)httpEx.HttpCode).ToString().StartsWith("50"))
                        {
                            Log.Warn($"Twitter Space 通知 - Discord 50X 錯誤: {httpEx.HttpCode}");
                        }
                        else
                        {
                            Log.Error(httpEx, $"Twitter Space 通知 - Discord 未知錯誤 {item.GuildId} / {item.DiscordChannelId}");
                        }
                    }
                    catch (TimeoutException)
                    {
                        Log.Warn($"Twitter Space 通知 - Timeout {item.GuildId} / {item.DiscordChannelId}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Twitter Space 通知 - 未知錯誤 {item.GuildId} / {item.DiscordChannelId}");
                    }
                }
            }
#endif
        }

        private void RecordSpace(DataBase.Table.TwitterSpace twitterSpace, string masterUrl)
        {
            Log.Info($"{twitterSpace.UserName} ({twitterSpace.SpaecTitle}): {masterUrl}");

            try
            {
                if (!Directory.Exists(twitterSpaceRecordPath))
                    Directory.CreateDirectory(twitterSpaceRecordPath);
            }
            catch (Exception ex)
            {
                Log.Error($"推特語音保存路徑不存在且不可建立: {twitterSpaceRecordPath}");
                Log.Error($"更改保存路徑至Data資料夾: {Utility.GetDataFilePath("")}");
                Log.Error(ex.ToString());

                twitterSpaceRecordPath = Utility.GetDataFilePath("");
            }

            string procArgs = $"ffmpeg -i \"{masterUrl}\" \"{twitterSpaceRecordPath}[{twitterSpace.UserScreenName}]{twitterSpace.SpaecActualStartTime:yyyyMMdd} - {twitterSpace.SpaecId}.m4a\"";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) Process.Start("tmux", $"new-window -d -n \"Twitter Space @{twitterSpace.UserScreenName}\" {procArgs}");
            else Process.Start(new ProcessStartInfo()
            {
                FileName = "ffmpeg",
                Arguments = procArgs.Replace("ffmpeg", ""),
                CreateNoWindow = false,
                UseShellExecute = true
            });
        }
    }
}