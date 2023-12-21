using Discord_Stream_Notify_Bot.HttpClients;
using Discord_Stream_Notify_Bot.HttpClients.Twitter;
using Discord_Stream_Notify_Bot.Interaction;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Discord_Stream_Notify_Bot.SharedService.Twitter
{
    public class TwitterSpacesService : IInteractionService
    {
        public bool IsEnable { get; private set; } = true;

        private readonly DiscordSocketClient _client;
        private readonly EmojiService _emojiService;
        private readonly TwitterClient _twitterClient;
        private readonly Timer timer;
        private readonly HashSet<string> hashSet = new HashSet<string>();

        private bool isRuning = false;
        private string twitterSpaceRecordPath = "";

        public TwitterSpacesService(DiscordSocketClient client, TwitterClient twitterClient, BotConfig botConfig, EmojiService emojiService)
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
            twitterSpaceRecordPath = botConfig.TwitterSpaceRecordPath;
            if (string.IsNullOrEmpty(twitterSpaceRecordPath)) twitterSpaceRecordPath = Program.GetDataFilePath("");
            if (!twitterSpaceRecordPath.EndsWith(Program.GetPlatformSlash())) twitterSpaceRecordPath += Program.GetPlatformSlash();

#if !RELEASE
            return;
#endif

            timer = new(async (stats) =>
            {
                if (isRuning) return; isRuning = true;
                try
                {
                    using (var db = DataBase.MainDbContext.GetDbContext())
                    {
                        var userList = db.TwitterSpaecSpider.Select((x) => x.UserId).ToArray();

                        for (int i = 0; i < userList.Length; i += 100)
                        {
                            try
                            {
                                var spaces = await _twitterClient.GetTwitterSpaceByUsersIdAsync(userList.Skip(i).Take(100).ToArray());
                                if (spaces.Count <= 0) continue;

                                foreach (var item in spaces)
                                {
                                    if (hashSet.Contains(item.SpaceId)) continue;
                                    if (db.TwitterSpace.Any((x) => x.SpaecId == item.SpaceId)) { hashSet.Add(item.SpaceId); continue; }

                                    try
                                    {
                                        var user = db.TwitterSpaecSpider.FirstOrDefault((x) => x.UserId == item.UserId);
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
                                            Log.Error($"GetTwitterSpaceMasterUrl: {item.SpaceId}\n{ex}");
                                            hashSet.Add(item.SpaceId);
                                            continue;
                                        }

                                        var spaceData = new DataBase.Table.TwitterSpace() { UserId = item.UserId, UserName = user.UserName, UserScreenName = user.UserScreenName, SpaecId = item.SpaceId, SpaecTitle = item.SpaceTitle, SpaecActualStartTime = item.StartAt ?? DateTime.Now, SpaecMasterPlaylistUrl = masterUrl.Replace("dynamic_playlist.m3u8?type=live", "master_playlist.m3u8") };

                                        if (string.IsNullOrEmpty(spaceData.SpaecTitle))
                                            spaceData.SpaecTitle = $"語音空間 ({spaceData.SpaecActualStartTime:yyyy/MM/dd})";

                                        db.TwitterSpace.Add(spaceData);
                                        hashSet.Add(item.SpaceId);

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
                                    catch (Exception ex) { Log.Error($"Spaces-Data {item.SpaceId}: {ex}"); }
                                }
                                db.SaveChanges();
                            }
                            catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                            {
                                Log.Error($"Prepare-Spaces: 429錯誤");
                            }
                            catch (Exception ex)
                            {
                                if (!ex.Message.Contains("503") && !ex.Message.Contains("temporarily unavailable"))
                                    Log.Error($"Prepare-Spaces: {ex}");
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Error($"Spaces-Timer: {ex}"); }
                finally { isRuning = false; }
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(90));
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
            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var item = db.TwitterSpaecSpider.FirstOrDefault((x) => x.UserId == twitterSpace.UserId);
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
            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var noticeGuildList = db.NoticeTwitterSpaceChannel.Where((x) => x.NoticeTwitterSpaceUserId == twitterSpace.UserId).ToList();
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
                        if (guild == null) continue;
                        var channel = guild.GetTextChannel(item.DiscordChannelId);
                        if (channel == null) continue;

                        var message = await channel.SendMessageAsync(item.StratTwitterSpaceMessage, false, embedBuilder.Build(), components: comp);

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
                        Log.Error($"Notice Space {item.GuildId} / {item.DiscordChannelId}\n{ex.Message}");
                        if (ex.Message.Contains("50013") || ex.Message.Contains("50001")) db.NoticeTwitterSpaceChannel.RemoveRange(db.NoticeTwitterSpaceChannel.Where((x) => x.DiscordChannelId == item.DiscordChannelId));
                        db.SaveChanges();
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
                Log.Error($"更改保存路徑至Data資料夾: {Program.GetDataFilePath("")}");
                Log.Error(ex.ToString());

                twitterSpaceRecordPath = Program.GetDataFilePath("");
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