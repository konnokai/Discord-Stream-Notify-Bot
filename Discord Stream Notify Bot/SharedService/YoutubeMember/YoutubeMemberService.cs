using Discord;
using Discord.WebSocket;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction;
using Discord_Stream_Notify_Bot.SharedService.Youtube;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.SharedService.YoutubeMember
{
    public class YoutubeMemberService : IInteractionService
    {
        public bool Enable { get; private set; } = true;

        Timer checkMemberShipOnlyVideoId, checkOldMemberStatus, checkNewMemberStatus;
        YoutubeStreamService _streamService;
        GoogleAuthorizationCodeFlow flow;
        DiscordSocketClient _client;
        BotConfig _botConfig;

        public YoutubeMemberService(YoutubeStreamService streamService, DiscordSocketClient discordSocketClient, BotConfig botConfig)
        {
            _botConfig = botConfig;
            _streamService = streamService;
            _client = discordSocketClient;

            if (string.IsNullOrEmpty(_botConfig.GoogleClientId) || string.IsNullOrEmpty(_botConfig.GoogleClientSecret))
            {
                Log.Warn($"{nameof(BotConfig.GoogleClientId)} 或 {nameof(BotConfig.GoogleClientSecret)} 空白，無法使用會限驗證系統");
                Enable = false;
                return;
            }

            flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _botConfig.GoogleClientId,
                    ClientSecret = _botConfig.GoogleClientSecret
                },
                Scopes = new string[] { "https://www.googleapis.com/auth/youtube.force-ssl" },
                DataStore = new RedisDataStore(RedisConnection.Instance.ConnectionMultiplexer)
            });

            Program.RedisSub.Subscribe("member.revokeToken", async (channel, value) =>
            {
                try
                {
                    ulong userId = 0;
                    if (!ulong.TryParse(value.ToString(), out userId))
                        return;

                    Log.Info($"接收到Redis的Revoke請求: {userId}");

                    await RemoveMemberCheckFromDbAsync(userId);
                }
                catch (Exception ex)
                {
                    Log.Error($"MemberRevokeTokenFromRedis: {ex}");
                }
            });

            _client.SelectMenuExecuted += async (component) =>
            {
                if (component.HasResponded)
                    return;

                try
                {
                    string[] customId = component.Data.CustomId.Split(new char[] { ':' });
                    if (customId.Length <= 2 || customId[0] != "member")
                        await component.RespondAsync("選單錯誤");

                    using DataBase.DBContext db = DataBase.DBContext.GetDbContext();
                    if (customId[1] == "check" && customId.Length == 4)
                    {
                        await component.DeferAsync(true);

                        if (!ulong.TryParse(customId[2], out ulong guildId))
                        {
                            await component.SendErrorAsync("GuildId無效，請向孤之界回報此問題", true);
                            Log.Error(JsonConvert.SerializeObject(component));
                            return;
                        }

                        if (!ulong.TryParse(customId[3], out ulong userId))
                        {
                            await component.SendErrorAsync("UserId無效，請向孤之界回報此問題", true);
                            Log.Error(JsonConvert.SerializeObject(component));
                            return;
                        }

                        if (component.User.Id != userId)
                        {
                            await component.SendErrorAsync("你無法使用此選單", true);
                            return;
                        }

                        var youtubeMembers = db.YoutubeMemberCheck.Where((x) => x.UserId == userId && x.GuildId == guildId);
                        var guildYoutubeMemberConfigs = db.GuildYoutubeMemberConfig.Where((x) => youtubeMembers.Any((x2) => x2.GuildId == x.GuildId));

                        db.YoutubeMemberCheck.RemoveRange(youtubeMembers);
                        db.SaveChanges();

                        if (guildYoutubeMemberConfigs.Any())
                        {
                            foreach (var item in guildYoutubeMemberConfigs)
                            {
                                try { await _client.Rest.RemoveRoleAsync(item.GuildId, userId, item.MemberCheckGrantRoleId); }
                                catch (Exception) { }
                            }
                        }

                        foreach (var item in component.Data.Values)
                        {
                            db.YoutubeMemberCheck.Add(new DataBase.Table.YoutubeMemberCheck() { UserId = userId, GuildId = guildId, CheckYTChannelId = item });
                        }
                        db.SaveChanges();

                        try { await component.Message.DeleteAsync(); }
                        catch
                        {
                            await DisableSelectMenuAsync(component, $"已選擇 {component.Data.Values.Count} 個頻道");
                        }

                        await component.SendConfirmAsync("已記錄至資料庫，請稍等至多5分鐘讓Bot驗證\n請確認已開啟本伺服器的 `允許來自伺服器成員的私人訊息` ，以避免收不到通知", true, true);
                    }
                }
                catch (Exception ex)
                {
                    await component.SendErrorAsync("錯誤，請向孤之界回報此問題", true);
                    Log.Error(ex.ToString());
                    return;
                }
            };

            checkMemberShipOnlyVideoId = new Timer(CheckMemberShipOnlyVideoId, null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));
            checkOldMemberStatus = new Timer(new TimerCallback(async (obj) => await CheckMemberShip(obj)), true, TimeSpan.FromHours(12), TimeSpan.FromHours(24));
            checkNewMemberStatus = new Timer(new TimerCallback(async (obj) => await CheckMemberShip(obj)), false, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));

            Program.RedisSub.Publish("member.syncRedisToken", _botConfig.RedisTokenKey);
            Log.Info("已同步Redis Token");
        }

        public async Task<bool> IsExistUserTokenAsync(string discordUserId)
        {
            return await ((RedisDataStore)flow.DataStore).IsExistUserTokenAsync<TokenResponse>(discordUserId);
        }

        public async Task RevokeUserGoogleCertAsync(string discordUserId = "")
        {
            try
            {
                if (string.IsNullOrEmpty(discordUserId))
                    throw new NullReferenceException("userId");

                var token = await flow.LoadTokenAsync(discordUserId, CancellationToken.None);
                if (token == null)
                    throw new NullReferenceException("token");

                string revokeToken = token.RefreshToken ?? token.AccessToken;
                await flow.RevokeTokenAsync(discordUserId, revokeToken, CancellationToken.None);

                Log.Info($"{discordUserId} 已解除Google憑證");
                await RemoveMemberCheckFromDbAsync(ulong.Parse(discordUserId));
            }
            catch (Exception ex)
            {
                await flow.DeleteTokenAsync(discordUserId, CancellationToken.None);
                Log.Error($"RevokeToken: {ex}");
                throw;
            }
        }

        public async Task RemoveMemberCheckFromDbAsync(ulong userId)
        {
            try
            {
                using var db = DataBase.DBContext.GetDbContext();

                if (!db.YoutubeMemberCheck.Any((x) => x.UserId == userId))
                {
                    Log.Info($"接收到Remove請求但不存在於資料庫內: {userId}");
                    return;
                }

                Log.Info($"接收到Remove請求: {userId}");

                var youtubeMembers = db.YoutubeMemberCheck.Where((x) => x.UserId == userId);
                var guildYoutubeMemberConfigs = db.GuildYoutubeMemberConfig.Where((x) => youtubeMembers.Any((x2) => x2.GuildId == x.GuildId));

                if (guildYoutubeMemberConfigs.Any())
                {
                    foreach (var item in guildYoutubeMemberConfigs)
                    {
                        try { await _client.Rest.RemoveRoleAsync(item.GuildId, userId, item.MemberCheckGrantRoleId); }
                        catch { }
                    }
                }

                db.YoutubeMemberCheck.RemoveRange(youtubeMembers);
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                Log.Error($"AfterRevokeUserCertAsync: {ex}");
                throw;
            }
        }

        public async Task<string> GetYoutubeDataAsync(string discordUserId = "")
        {
            try
            {
                if (string.IsNullOrEmpty(discordUserId))
                    throw new NullReferenceException("userId");

                var token = await flow.LoadTokenAsync(discordUserId, CancellationToken.None);
                if (token == null)
                    throw new NullReferenceException("token");

                var userCert = await GetUserCredentialAsync(discordUserId, token);
                if (userCert == null)
                    throw new NullReferenceException("userCert");

                var service = new YouTubeService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = userCert,
                    ApplicationName = "Discord Youtube Member Check"
                }).Channels.List("id,snippet");
                service.Mine = true;

                try 
                { 
                    var result = await service.ExecuteAsync();
                    var channel = result.Items.FirstOrDefault();
                    if (channel == null)
                        throw new NullReferenceException("channel");

                    return Format.Url(channel.Snippet.Title, $"https://www.youtube.com/channel/{channel.Id}");
                }
                catch { throw; }

            }
            catch { throw; }
        }

        private async Task DisableSelectMenuAsync(SocketMessageComponent component, string placeholder = "")
        {
            SelectMenuBuilder selectMenuBuilder = new SelectMenuBuilder()
                .WithPlaceholder(string.IsNullOrEmpty(placeholder) ? "已選擇" : placeholder)
                .WithMinValues(1)
                .WithMaxValues(1)
                .AddOption("1", "2")
                .WithCustomId("1234")
                .WithDisabled(true);

            var newComponent = new ComponentBuilder()
                        .WithSelectMenu(selectMenuBuilder)
                        .Build();

            try
            {
                await component.UpdateAsync((act) =>
                {
                    act.Components = new Optional<MessageComponent>(newComponent);
                });
            }
            catch
            {
                await component.ModifyOriginalResponseAsync((act) =>
                {
                    act.Components = new Optional<MessageComponent>(newComponent);
                });
            }
        }

        //https://github.com/member-gentei/member-gentei/blob/90f62385f554eb4c02ed8732e15061b9dd1dd6d0/gentei/apis/youtube.go#L100
        private async void CheckMemberShipOnlyVideoId(object stats)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                foreach (var item in db.GuildYoutubeMemberConfig.Where((x) => !string.IsNullOrEmpty(x.MemberCheckChannelId) && x.MemberCheckChannelId.Length == 24 && (x.MemberCheckVideoId == "-" || string.IsNullOrEmpty(x.MemberCheckChannelTitle))).Distinct((x) => x.MemberCheckChannelId))
                {
                    try
                    {
                        var s = _streamService.yt.PlaylistItems.List("snippet");
                        s.PlaylistId = item.MemberCheckChannelId.Replace("UC", "UUMO");
                        var result = await s.ExecuteAsync().ConfigureAwait(false);
                        var videoList = result.Items.ToList();

                        bool isCheck = false;
                        do
                        {
                            if (videoList.Count == 0)
                            {
                                await Program.ApplicatonOwner.SendMessageAsync($"{item.MemberCheckChannelId} 無任何可檢測的會限影片!");
                                await SendMsgToLogChannelAsync(item.MemberCheckChannelId, $"{item.MemberCheckChannelId} 無會限影片，請等待該頻道主有新的會限影片且可留言時時再使用會限驗證功能\n" +
                                    $"你可以使用 `/youtube get-member-only-playlist` 來確認該頻道是否有可驗證的影片");
                                break;
                            }

                            var videoSnippet = videoList[new Random().Next(0, videoList.Count)];
                            var videoId = videoSnippet.Snippet.ResourceId.VideoId;
                            var ct = _streamService.yt.CommentThreads.List("snippet");
                            ct.VideoId = videoId;

                            try
                            {
                                var commentResult = await ct.ExecuteAsync().ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                if (ex.Message.ToLower().Contains("disabled comments"))
                                {
                                    videoList.Remove(videoSnippet);
                                }
                                else if (ex.Message.ToLower().Contains("403") || ex.Message.ToLower().Contains("the request might not be properly authorized"))
                                {
                                    Log.Info($"新會限影片 - ({item.MemberCheckChannelId}): {videoId}");
                                    await SendMsgToLogChannelAsync(item.MemberCheckChannelId, $"新會限檢測影片 - ({item.MemberCheckChannelId}): {videoId}", false, false);

                                    foreach (var item2 in db.GuildYoutubeMemberConfig.Where((x) => x.MemberCheckChannelId == item.MemberCheckChannelId))
                                    {
                                        item2.MemberCheckVideoId = videoId;
                                        db.GuildYoutubeMemberConfig.Update(item2);
                                    }

                                    isCheck = true;
                                }
                                else
                                {
                                    Log.Error($"{item.MemberCheckChannelId} 新會限影片檢查錯誤");
                                    Log.Error(ex.Message);

                                    foreach (var item2 in db.GuildYoutubeMemberConfig.Where((x) => x.MemberCheckChannelId == item.MemberCheckChannelId))
                                    {
                                        item2.MemberCheckVideoId = "";
                                        db.GuildYoutubeMemberConfig.Update(item2);
                                    }

                                    isCheck = true;
                                }
                            }
                        } while (!isCheck);
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.ToLower().Contains("playlistid"))
                        {
                            Log.Warn($"CheckMemberShipOnlyVideoId: {item.GuildId} / {item.MemberCheckChannelId} 無會限影片可供檢測");
                            await SendMsgToLogChannelAsync(item.MemberCheckChannelId, $"{item.MemberCheckChannelId} 無會限影片，請等待該頻道主有新的會限影片且可留言時再使用會限驗證功能\n" +
                                $"你可以使用 `/youtube get-member-only-playlist` 來確認該頻道是否有可驗證的影片");
                            continue;
                        }
                        else Log.Warn($"CheckMemberShipOnlyVideoId: {item.GuildId} / {item.MemberCheckChannelId}\n{ex.Message}");
                    }
                    finally
                    {
                        db.SaveChanges();
                    }

                    try
                    {
                        var c = _streamService.yt.Channels.List("snippet");
                        c.Id = item.MemberCheckChannelId;
                        var channelResult = await c.ExecuteAsync();
                        var channel = channelResult.Items.First();

                        Log.Info($"會限頻道名稱已變更 - ({item.MemberCheckChannelId}): `" + (string.IsNullOrEmpty(item.MemberCheckChannelTitle) ? "無" : item.MemberCheckChannelTitle) + $"` -> `{channel.Snippet.Title}`");
                        await SendMsgToLogChannelAsync(item.MemberCheckChannelId, $"會限頻道名稱已變更: `" + (string.IsNullOrEmpty(item.MemberCheckChannelTitle) ? "無" : item.MemberCheckChannelTitle) + $"` -> `{channel.Snippet.Title}`", false, false);

                        foreach (var item2 in db.GuildYoutubeMemberConfig.Where((x) => x.MemberCheckChannelId == item.MemberCheckChannelId))
                        {
                            item2.MemberCheckChannelTitle = channel.Snippet.Title;
                            db.GuildYoutubeMemberConfig.Update(item2);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"CheckMemberShipOnlyChannelName: {item.GuildId} / {item.MemberCheckChannelId}\n{ex.Message}");
                    }
                    finally
                    {
                        db.SaveChanges();
                    }
                }
            }

            //Log.Info("檢查新會限影片完成");
        }

        private async Task SendMsgToLogChannelAsync(string checkChannelId, string msg, bool isNeedRemove = true, bool isNeedSendToOwner = true)
        {
            using var db = DataBase.DBContext.GetDbContext();

            foreach (var item in db.GuildYoutubeMemberConfig.Where((x) => x.MemberCheckChannelId == checkChannelId))
            {
                try
                {
                    bool isExistLogChannel = true;

                    var guild = _client.GetGuild(item.GuildId);
                    if (guild == null)
                    {
                        Log.Warn($"SendMsgToLogChannelAsync: {item.GuildId} 不存在!");
                        db.GuildYoutubeMemberConfig.Remove(item);
                        continue;
                    }

                    var guildConfig = db.GuildConfig.FirstOrDefault((x) => x.GuildId == item.GuildId);
                    if (guildConfig == null)
                    {
                        Log.Warn($"SendMsgToLogChannelAsync: {item.GuildId} 無GuildConfig");
                        db.GuildConfig.Add(new DataBase.Table.GuildConfig { GuildId = guild.Id });
                        db.GuildYoutubeMemberConfig.Remove(item);

                        msg += $"\n另外: `{guild.Name}` 無會限紀錄頻道，請新增頻道並給予小幫手 `讀取、發送及嵌入連結` 權限後使用 `/member-set set-notice-member-status-channel` 設定";
                        try { await guild.Owner.SendMessageAsync(embed: new EmbedBuilder().WithErrorColor().WithDescription(msg).Build()); }
                        catch { }

                        continue;
                    }

                    var logChannel = guild.GetTextChannel(guildConfig.LogMemberStatusChannelId);
                    if (logChannel == null)
                    {
                        isExistLogChannel = false;
                        msg += $"\n另外: `{guild.Name}` 無會限紀錄頻道，請新增頻道並給予小幫手 `讀取、發送及嵌入連結` 權限後使用 `/member-set set-notice-member-status-channel` 設定";
                    }
                    else
                    {
                        var permission = guild.GetUser(_client.CurrentUser.Id).GetPermissions(logChannel);
                        if (!permission.ViewChannel || !permission.SendMessages || !permission.EmbedLinks)
                        {
                            Log.Warn($"{item.GuildId} / {guildConfig.LogMemberStatusChannelId} 無權限可紀錄");
                            msg += $"\n另外: `{guild.Name}` 的 `{logChannel.Name}`無權限可紀錄，請給予小幫手 `讀取、發送及嵌入連結` 權限";
                            isExistLogChannel = false;
                        }
                    }

                    var embed = new EmbedBuilder().WithErrorColor().WithDescription(msg).Build();

                    if (isNeedSendToOwner)
                    {
                        try { await guild.Owner.SendMessageAsync(embed: embed); }
                        catch { }
                    }

                    if (isExistLogChannel)
                    {
                        try { await logChannel.SendMessageAsync(embed: embed); }
                        catch { }
                    }

                    if (isNeedRemove) db.GuildYoutubeMemberConfig.Remove(item);
                }
                catch (Exception ex)
                {
                    Log.Error($"SendMsgToLogChannelAsync: {ex}");
                }
            }
        }

        //Todo: 驗證一次後可同時向同頻道多個伺服器設定驗證結果
        //https://github.com/member-gentei/member-gentei/blob/90f62385f554eb4c02ed8732e15061b9dd1dd6d0/gentei/membership/membership.go#L331
        //https://discord.com/channels/@me/userChannel.Id
        public async Task CheckMemberShip(object stats)
        {
            bool isOldCheck = (bool)stats;

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var needCheckList = db.GuildYoutubeMemberConfig.Where((x) => !string.IsNullOrEmpty(x.MemberCheckChannelId) && !string.IsNullOrEmpty(x.MemberCheckChannelTitle) && x.MemberCheckVideoId != "-");
                Log.Info((isOldCheck ? "舊" : "新") + $"會限檢查開始: {needCheckList.Count()}個頻道");

                HashSet<string> checkedMemberSet = new();
                List<YoutubeMemberCheck> needRemoveList = new();
                foreach (var guildYoutubeMemberConfig in needCheckList)
                {
                    var list = db.YoutubeMemberCheck
                        .Where((x) => x.GuildId == guildYoutubeMemberConfig.GuildId && x.CheckYTChannelId == guildYoutubeMemberConfig.MemberCheckChannelId)
                        .Where((x) => (isOldCheck && x.IsChecked) || (!isOldCheck && !x.IsChecked));
                    if (!list.Any())
                        continue;

                    int totalCheckCount = list.Count();

                    var guildConfig = db.GuildConfig.FirstOrDefault((x) => x.GuildId == guildYoutubeMemberConfig.GuildId);
                    if (guildConfig == null)
                    {
                        db.GuildConfig.Add(new DataBase.Table.GuildConfig() { GuildId = guildYoutubeMemberConfig.GuildId });
                        Log.Warn($"{guildYoutubeMemberConfig.GuildId} Guild不存在於資料庫內");
                        continue;
                    }

                    var guild = _client.GetGuild(guildYoutubeMemberConfig.GuildId);
                    if (guild == null)
                    {
                        Log.Warn($"{guildYoutubeMemberConfig.GuildId} Guild不存在");
                        db.GuildYoutubeMemberConfig.RemoveRange(db.GuildYoutubeMemberConfig.Where((x) => x.GuildId == guildYoutubeMemberConfig.GuildId));
                        continue;
                    }

                    var logChannel = guild.GetTextChannel(guildConfig.LogMemberStatusChannelId);
                    if (logChannel == null)
                    {
                        Log.Warn($"{guildYoutubeMemberConfig.GuildId} 無紀錄頻道");
                        continue;
                    }

                    var role = guild.GetRole(guildYoutubeMemberConfig.MemberCheckGrantRoleId);
                    if (role == null)
                    {
                        await logChannel.SendMessageAsync($"{Format.Url(guildYoutubeMemberConfig.MemberCheckChannelId, $"https://www.youtube.com/channel/{guildYoutubeMemberConfig.MemberCheckChannelId}")} 的會限用戶組Id不存在，請重新設定");
                        Log.Warn($"{guildYoutubeMemberConfig.GuildId} / {guildYoutubeMemberConfig.MemberCheckChannelId} RoleId不存在 {guildYoutubeMemberConfig.MemberCheckGrantRoleId}");
                        db.GuildYoutubeMemberConfig.Remove(guildYoutubeMemberConfig);
                        continue;
                    }

                    var permission = guild.CurrentUser.GetPermissions(logChannel);
                    if (!permission.ViewChannel || !permission.SendMessages || !permission.EmbedLinks)
                    {
                        Log.Warn($"{guildYoutubeMemberConfig.GuildId} / {guildConfig.LogMemberStatusChannelId} 無權限可紀錄");
                        db.GuildYoutubeMemberConfig.Remove(guildYoutubeMemberConfig);
                        continue;
                    }

                    if (!guild.CurrentUser.GuildPermissions.ManageRoles)
                    {
                        await logChannel.SendMessageAsync("我沒有權限可以編輯用戶組，請幫我開啟伺服器的 `管理身分組` 權限");
                        Log.Warn($"{guildYoutubeMemberConfig.GuildId} 無權限可給予用戶組");
                        continue;
                    }

                    int checkedMemberCount = 0;
                    foreach (var item2 in list)
                    {
                        var user = await _client.Rest.GetUserAsync(item2.UserId);

                        var userChannel = await user.CreateDMChannelAsync();
                        if (userChannel == null) Log.Warn($"{item2.UserId} 無法建立使用者私訊");

                        if (!checkedMemberSet.Contains($"{item2.Id}-{item2.CheckYTChannelId}"))
                        {
                            var token = await flow.LoadTokenAsync(item2.UserId.ToString(), CancellationToken.None);
                            if (token == null)
                            {
                                await RemoveMemberCheckFromDbAsync(item2.UserId);

                                await logChannel.SendErrorMessageAsync(user, guildYoutubeMemberConfig.MemberCheckChannelTitle, "未登入");
                                await userChannel.SendErrorMessageAsync($"未登入，請至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 登入並再次於伺服器執行 `/member check`", item2.UserId, logChannel);

                                continue;
                            }

                            UserCredential userCredential = null;
                            try
                            {
                                userCredential = await GetUserCredentialAsync(item2.UserId.ToString(), token);
                            }
                            catch (Exception ex)
                            {
                                if (ex.Message == "RefreshToken空白")
                                {
                                    await RevokeUserGoogleCertAsync(item2.UserId.ToString());

                                    await logChannel.SendErrorMessageAsync(user, guildYoutubeMemberConfig.MemberCheckChannelTitle, "無法重複驗證");
                                    await userChannel.SendErrorMessageAsync($"無法重新刷新您的授權\n" +
                                        $"請到 {Format.Url("Google安全性", "https://myaccount.google.com/permissions")} 移除 `直播小幫手會限確認` 的應用程式存取權後\n" +
                                        $"至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 重新登入並再次於伺服器執行 `/member check`", item2.UserId, logChannel);

                                    continue;
                                }

                                Log.Error(ex.ToString());
                                continue;
                            }

                            if (userCredential == null)
                            {
                                await RemoveMemberCheckFromDbAsync(item2.UserId);

                                await logChannel.SendErrorMessageAsync(user, guildYoutubeMemberConfig.MemberCheckChannelTitle, "認證過期");
                                await userChannel.SendErrorMessageAsync($"您的Google認證已失效\n" +
                                    $"請到 {Format.Url("Google安全性", "https://myaccount.google.com/permissions")} 移除 `直播小幫手會限確認` 的應用程式存取權後\n" +
                                    $"至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 重新登入並再次於伺服器執行 `/member check`", item2.UserId, logChannel);

                                continue;
                            }

                            var service = new YouTubeService(new BaseClientService.Initializer()
                            {
                                HttpClientInitializer = userCredential,
                                ApplicationName = "Discord Youtube Member Check"
                            }).CommentThreads.List("id");
                            service.VideoId = guildYoutubeMemberConfig.MemberCheckVideoId;

                            bool isMember = false;
                            try
                            {
                                await service.ExecuteAsync().ConfigureAwait(false);
                                isMember = true;
                            }
                            catch (Exception ex)
                            {
                                try
                                {
                                    if (ex.Message.ToLower().Contains("parameter has disabled comments"))
                                    {
                                        Log.Warn($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {item2.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗");
                                        Log.Warn($"{guildYoutubeMemberConfig.MemberCheckChannelTitle} ({guildYoutubeMemberConfig.MemberCheckChannelId}): {guildYoutubeMemberConfig.MemberCheckVideoId}已關閉留言");
                                        await (await Program.ApplicatonOwner.CreateDMChannelAsync()).SendErrorMessageAsync($"{guildYoutubeMemberConfig.GuildId} - {item2.UserId} 會限資格取得失敗: {guildYoutubeMemberConfig.MemberCheckVideoId}已關閉留言", item2.UserId, logChannel);

                                        guildYoutubeMemberConfig.MemberCheckVideoId = "-";
                                        db.GuildYoutubeMemberConfig.Update(guildYoutubeMemberConfig);
                                        db.SaveChanges();

                                        break;
                                    }
                                    else if (ex.Message.ToLower().Contains("403") || ex.Message.ToLower().Contains("the request might not be properly authorized"))
                                    {
                                        Log.Warn($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {item2.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗: 無會員");

                                        needRemoveList.Add(item2);

                                        try
                                        {
                                            await _client.Rest.RemoveRoleAsync(guild.Id, item2.UserId, role.Id).ConfigureAwait(false);
                                        }
                                        catch (Exception ex2) { Log.Warn(ex2.ToString()); }

                                        if (isOldCheck)
                                        {
                                            await logChannel.SendErrorMessageAsync(user, guildYoutubeMemberConfig.MemberCheckChannelTitle, "會員已過期");
                                            await userChannel.SendErrorMessageAsync($"您在 `{guild.Name}` 的 `{guildYoutubeMemberConfig.MemberCheckChannelTitle}` 會限資格已失效\n" +
                                             $"如要重新驗證會員請於購買會員後再次於伺服器執行 `/member check`", item2.UserId, logChannel);
                                        }
                                        else
                                        {
                                            await logChannel.SendErrorMessageAsync(user, guildYoutubeMemberConfig.MemberCheckChannelTitle, "無會員");
                                            await userChannel.SendErrorMessageAsync($"無法在 `{guild.Name}` 的 `{guildYoutubeMemberConfig.MemberCheckChannelTitle}` 上存取會限資格\n" +
                                             $"請先使用 `/member show-youtube-account` 確認綁定的頻道是否正確，並確認已購買會員\n若都正確請向 `{Program.ApplicatonOwner}` 確認問題", item2.UserId, logChannel);
                                        }
                                        continue;
                                    }
                                    else if (ex.Message.ToLower().Contains("token has been expired or revoked") ||
                                        ex.Message.ToLower().Contains("the access token has expired and could not be refreshed") ||
                                        ex.Message.ToLower().Contains("authenticateduseraccountclosed") || ex.Message.ToLower().Contains("authenticateduseraccountsuspended"))
                                    {
                                        Log.Warn($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {item2.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗: AccessToken已過期或無法刷新");
                                        Log.Warn(JsonConvert.SerializeObject(userCredential.Token));
                                        Log.Warn(ex.ToString());

                                        await RemoveMemberCheckFromDbAsync(item2.UserId);

                                        await logChannel.SendErrorMessageAsync(user, guildYoutubeMemberConfig.MemberCheckChannelTitle, "認證過期");
                                        await userChannel.SendErrorMessageAsync($"您的Google認證已失效\n" +
                                            $"請到 {Format.Url("Google安全性", "https://myaccount.google.com/permissions?continue=https%3A%2F%2Fmyaccount.google.com%2Fsecurity")} 移除 `直播小幫手會限確認` 的應用程式存取權後\n" +
                                            $"至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 重新登入並再次於伺服器執行 `/member check`", item2.UserId, logChannel);
                                        continue;
                                    }
                                    else if (ex.Message.ToLower().Contains("the added or subtracted value results in an un-representable"))
                                    {
                                        Log.Error($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {item2.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗: 時間加減錯誤");
                                        Log.Error(ex.ToString());

                                        await RevokeUserGoogleCertAsync(item2.UserId.ToString());

                                        await logChannel.SendErrorMessageAsync(user, guildYoutubeMemberConfig.MemberCheckChannelTitle, "時間加減錯誤");
                                        await userChannel.SendErrorMessageAsync($"遇到已知但尚未處理的問題，您可以重新嘗試登入\n" +
                                            $"請到 {Format.Url("Google安全性", "https://myaccount.google.com/permissions?continue=https%3A%2F%2Fmyaccount.google.com%2Fsecurity")} 移除 `直播小幫手會限確認` 的應用程式存取權後\n" +
                                            $"至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 重新登入並再次於伺服器執行 `/member check`", item2.UserId, logChannel);
                                        continue;
                                    }
                                    else if (ex.Message.ToLower().Contains("500"))
                                    {
                                        Log.Error($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {item2.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗: 500內部錯誤");

                                        await logChannel.SendErrorMessageAsync(user, guildYoutubeMemberConfig.MemberCheckChannelTitle, "Google內部錯誤");
                                        continue;
                                    }
                                    else if (ex.Message.ToLower().Contains("bad req"))
                                    {
                                        Log.Error($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {item2.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗: 400錯誤");
                                        Log.Error(ex.ToString());

                                        await logChannel.SendErrorMessageAsync(user, guildYoutubeMemberConfig.MemberCheckChannelTitle, "400錯誤");
                                        continue;
                                    }
                                    else
                                    {
                                        Log.Error($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {item2.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗: 未知的錯誤");
                                        Log.Error(ex.ToString());

                                        await RevokeUserGoogleCertAsync(item2.UserId.ToString());

                                        await logChannel.SendErrorMessageAsync(user, guildYoutubeMemberConfig.MemberCheckChannelTitle, "不明的錯誤");
                                        await userChannel.SendErrorMessageAsync($"無法驗證您的帳號，可能是Google內部錯誤\n請重新執行驗證步驟並向 {Program.ApplicatonOwner} 確認問題", item2.UserId, logChannel);
                                        continue;
                                    }
                                }
                                catch (Exception ex2)
                                {
                                    Log.Error($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {item2.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 回傳會限資格訊息失敗: {ex}");
                                    Log.Error(ex2.ToString());
                                }
                            }

                            if (!isMember) return;
                            checkedMemberSet.Add($"{item2.Id}-{item2.CheckYTChannelId}");
                        }

                        checkedMemberCount++;
                        try
                        {
                            await _client.Rest.AddRoleAsync(guild.Id, item2.UserId, role.Id).ConfigureAwait(false);
                        }
                        catch (Discord.Net.HttpException httpEx)
                        {
                            if (!httpEx.DiscordCode.HasValue)
                            {
                                Log.Error($"無法新增用戶組至用戶，非Discord錯誤: {guild.Id} / {user.Id}");
                                Log.Error($"{httpEx}");
                                continue;
                            }

                            if (httpEx.DiscordCode.Value == DiscordErrorCode.MissingPermissions || httpEx.DiscordCode.Value == DiscordErrorCode.InsufficientPermissions)
                            {
                                await logChannel.SendErrorMessageAsync(user, guildYoutubeMemberConfig.MemberCheckChannelTitle, "已驗證但無法給予用戶組");
                                await userChannel.SendConfirmMessageAsync($"你在 `{guild}` 的 `{guildYoutubeMemberConfig.MemberCheckChannelTitle}` 會限已通過驗證，但無法新增用戶組，請告知管理員協助新增", item2.UserId, logChannel);
                            }
                            else if (httpEx.DiscordCode.Value == DiscordErrorCode.UnknownAccount || httpEx.DiscordCode.Value == DiscordErrorCode.UnknownMember || httpEx.DiscordCode.Value == DiscordErrorCode.UnknownUser)
                            {
                                await logChannel.SendErrorMessageAsync(user, guildYoutubeMemberConfig.MemberCheckChannelTitle, "未知的使用者");
                                Log.Warn($"用戶已離開伺服器: {guild.Id} / {user.Id}");
                                needRemoveList.Add(item2);
                            }
                            else
                            {
                                Log.Error($"無法新增用戶組至用戶: {guild.Id} / {user.Id}");
                                Log.Error($"{httpEx}");

                                await logChannel.SendErrorMessageAsync(user, guildYoutubeMemberConfig.MemberCheckChannelTitle, "已驗證但遇到未知的錯誤");
                                await userChannel.SendConfirmMessageAsync($"你在 `{guild}` 的 `{guildYoutubeMemberConfig.MemberCheckChannelTitle}` 會限已通過驗證，但無法新增用戶組，請告知管理員協助新增", item2.UserId, logChannel);
                            }
                            continue;
                        }

                        try
                        {
                            item2.IsChecked = true;
                            item2.LastCheckTime = DateTime.Now;

                            db.YoutubeMemberCheck.Update(item2);

                            try
                            {
                                if (!isOldCheck)
                                {
                                    await logChannel.SendConfirmMessageAsync(user, new EmbedBuilder().AddField("檢查頻道", guildYoutubeMemberConfig.MemberCheckChannelTitle).AddField("狀態", "已驗證"));
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warn($"無法傳送紀錄訊息: {guild.Id} / {logChannel.Id}");
                                Log.Error($"{ex}");
                            }

                            try
                            {
                                if (!isOldCheck)
                                {
                                    await userChannel.SendConfirmMessageAsync($"你在 `{guild}`  的 `{guildYoutubeMemberConfig.MemberCheckChannelTitle}` 會限已通過驗證，現在你可至該伺服器上觀看會限頻道了", item2.UserId, logChannel);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warn($"無法傳送私訊: {guild.Id} / {user.Id}");
                                Log.Error($"{ex}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"MemberCheckUpdateDb: {guildYoutubeMemberConfig.GuildId} - {item2.UserId} 資料庫更新失敗");
                            Log.Error($"{ex}");
                        }
                    }

                    await logChannel.SendConfirmMessageAsync((isOldCheck ? "舊" : "新") + "會限驗證完成", $"檢查頻道: {guildYoutubeMemberConfig.MemberCheckChannelTitle}\n" +
                        $"本次驗證 {totalCheckCount} 位成員，共 {checkedMemberCount} 位驗證成功");
                }

                try
                {
                    db.YoutubeMemberCheck.RemoveRange(needRemoveList);
                }
                catch (Exception ex)
                {                    
                    Log.Error($"CheckMemberShip-RemoveRange: {ex}");
                    await (await Program.ApplicatonOwner.CreateDMChannelAsync()).SendErrorMessageAsync($"CheckMemberShip-RemoveRange: {ex}");
                }

                try
                {
                    db.SaveChanges();
                }
                catch (Exception ex)
                {
                    Log.Error($"CheckMemberShip-SaveChanges: {ex}");
                    await (await Program.ApplicatonOwner.CreateDMChannelAsync()).SendErrorMessageAsync($"CheckMemberShip-SaveChanges: {ex}");
                }

                needRemoveList.Clear();
            }

            //Log.Info("會限檢查完畢");
        }

        private async Task<UserCredential> GetUserCredentialAsync(string discordUserId, TokenResponse token)
        {
            if (string.IsNullOrEmpty(token.RefreshToken))
                throw new NullReferenceException("RefreshToken空白");

            var credential = new UserCredential(flow, discordUserId, token);

            using (var db = DataBase.DBContext.GetDbContext())
            {
                try
                {
                    if (token.IsExpired(Google.Apis.Util.SystemClock.Default))
                    {
                        if (!await credential.RefreshTokenAsync(CancellationToken.None))
                        {
                            Log.Warn($"{discordUserId} AccessToken無法刷新");
                            await flow.DataStore.DeleteAsync<TokenResponse>(discordUserId);
                            credential = null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (ex.Message.ToLower().Contains("token has been expired or revoked"))
                    {
                        Log.Warn($"{discordUserId} 已取消授權");
                    }
                    else
                    {
                        Log.Warn($"{discordUserId} AccessToken發生未知錯誤");
                        Log.Warn($"{ex.Message}");
                    }
                    await flow.DataStore.DeleteAsync<TokenResponse>(discordUserId);
                    credential = null;
                }
            }

            return credential;
        }
    }

    static class Ext
    {
        public static async Task SendConfirmMessageAsync(this ITextChannel tc, IUser user, EmbedBuilder embedBuilder)
            => await tc.SendMessageAsync(embed: embedBuilder.WithOkColor().WithAuthor(user).WithThumbnailUrl(user.GetAvatarUrl()).Build());

        public static async Task SendConfirmMessageAsync(this ITextChannel tc, string title, string dec)
            => await tc.SendMessageAsync(embed: new EmbedBuilder().WithOkColor().WithTitle(title).WithDescription(dec).Build());

        public static async Task SendErrorMessageAsync(this ITextChannel tc, IUser user, string channelTitle, string status)
            => await tc.SendMessageAsync(embed: new EmbedBuilder().WithErrorColor().WithAuthor(user).WithThumbnailUrl(user.GetAvatarUrl()).AddField("檢查頻道", channelTitle).AddField("狀態", status).Build());

        public static async Task SendConfirmMessageAsync(this IDMChannel dc, string text, ulong userId, ITextChannel tc)
        {
            if (dc == null) return;

            try
            {
                await dc.SendMessageAsync(embed: new EmbedBuilder().WithOkColor().WithDescription(text).Build());
            }
            catch (Discord.Net.HttpException ex)
            {
                if (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                {
                    Log.Warn($"無法傳送訊息至: {dc.Name} ({userId})");
                    await tc.SendMessageAsync($"無法傳送訊息至: <@{userId}>\n請向該用戶提醒開啟 `允許來自伺服器成員的私人訊息`");
                }
                else
                    Log.Error(ex.ToString());
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
        }

        public static async Task SendErrorMessageAsync(this IDMChannel dc, string text, ulong userId, ITextChannel tc)
        {
            if (dc == null) return;

            try
            {
                await dc.SendMessageAsync(embed: new EmbedBuilder().WithErrorColor().WithDescription(text).Build());
            }
            catch (Discord.Net.HttpException ex)
            {
                if (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                {
                    Log.Warn($"無法傳送訊息至: {dc.Name} ({userId})");
                    await tc.SendMessageAsync($"無法傳送訊息至: <@{userId}>\n請向該用戶提醒開啟 `允許來自伺服器成員的私人訊息`");
                }
                else
                    Log.Error(ex.ToString());
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
        }

        public static async Task SendErrorMessageAsync(this IDMChannel dc, string text)
        {
            if (dc == null) return;

            try
            {
                await dc.SendMessageAsync(embed: new EmbedBuilder().WithErrorColor().WithDescription(text).Build());
            }
            catch (Discord.Net.HttpException ex)
            {
                if (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                {
                    Log.Warn($"無法傳送訊息至: {dc.Name}");
                }
                else
                    Log.Error(ex.ToString());
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
        }
    }
}