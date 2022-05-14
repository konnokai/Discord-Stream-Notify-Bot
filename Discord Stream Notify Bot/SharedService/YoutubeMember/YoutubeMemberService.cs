using Discord;
using Discord.WebSocket;
using Discord_Stream_Notify_Bot.Interaction;
using Discord_Stream_Notify_Bot.SharedService.Youtube;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.SharedService.YoutubeMember
{
    public class YoutubeMemberService : IInteractionService
    {
        Timer checkMemberShipOnlyVideoId, checkOldMemberStatus, checkNewMemberStatus, checkRoleStatus;
        YoutubeStreamService _streamService;
        GoogleAuthorizationCodeFlow flow;
        DiscordSocketClient _client;
        BotConfig _botConfig;

        public YoutubeMemberService(YoutubeStreamService streamService, DiscordSocketClient discordSocketClient, BotConfig botConfig)
        {
            _streamService = streamService;
            _client = discordSocketClient;
            _botConfig = botConfig;
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

            Program.RedisSub.Subscribe("member.revokeToken",async (channel, value) =>
            {
                try
                {
                    ulong userId = 0;
                    if (!ulong.TryParse(value.ToString(), out userId))
                        return;

                    var user = _client.GetUser(userId);
                    if (user == null) return;

                    Log.Info($"接收到Revoke請求: {user.Username} ({userId})");

                    using var db = DataBase.DBContext.GetDbContext();

                    var guildConfigs = db.GuildConfig.Include((x) => x.MemberCheck).Where((x) => x.MemberCheck.Any((x2) => x2.UserId == user.Id));
                    var youtubeMembers = db.YoutubeMemberCheck.Where((x) => x.UserId == user.Id);

                    if (guildConfigs.Any())
                    {
                        foreach (var item in guildConfigs)
                        {
                            try { await _client.Rest.RemoveRoleAsync(item.GuildId, user.Id, item.MemberCheckGrantRoleId); }
                            catch (Exception) { }
                        }
                    }

                    db.YoutubeMemberCheck.RemoveRange(youtubeMembers);
                    db.SaveChanges();
                }
                catch (Exception ex)
                {
                    Log.Error($"RevokeToken: {ex}");
                }
            });

            checkMemberShipOnlyVideoId = new Timer(CheckMemberShipOnlyVideoId, null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));
            checkOldMemberStatus = new Timer(new TimerCallback(async (obj) => await CheckMemberShip(obj)), true, TimeSpan.FromHours(12), TimeSpan.FromHours(12));
            checkNewMemberStatus = new Timer(new TimerCallback(async (obj) => await CheckMemberShip(obj)), false, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
            checkRoleStatus = new Timer(new TimerCallback(async (obj) => await CheckRoleStatus()), null, TimeSpan.FromSeconds(30), TimeSpan.FromHours(12));
        }

        //https://github.com/member-gentei/member-gentei/blob/90f62385f554eb4c02ed8732e15061b9dd1dd6d0/gentei/apis/youtube.go#L100
        private async void CheckMemberShipOnlyVideoId(object stats)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                foreach (var item in db.GuildConfig.Where((x) => !string.IsNullOrEmpty(x.MemberCheckChannelId) && x.MemberCheckChannelId.Length == 24 && x.MemberCheckVideoId == "-").Distinct((x) => x.MemberCheckChannelId))
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
                                await Program.ApplicatonOwner.SendMessageAsync($"{item.MemberCheckChannelId} 無任何可檢測的會限直播!");
                                item.MemberCheckChannelId = "";
                                db.GuildConfig.Update(item);
                                db.SaveChanges();
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

                                    foreach (var item2 in db.GuildConfig.Where((x) => x.MemberCheckChannelId == item.MemberCheckChannelId))
                                    {
                                        item2.MemberCheckVideoId = videoId;
                                        db.GuildConfig.Update(item2);
                                    }

                                    db.SaveChanges();
                                    isCheck = true;
                                }
                                else
                                {
                                    Log.Error($"{item.MemberCheckChannelId} 新會限影片檢查錯誤");
                                    Log.Error(ex.Message);
                                    item.MemberCheckChannelId = "";
                                    db.GuildConfig.Update(item);
                                    db.SaveChanges();
                                    isCheck = true;
                                }
                            }
                        } while (!isCheck);
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"CheckMemberShipOnlyVideoId: {item.GuildId} / {item.MemberCheckChannelId}\n{ex.Message}");
                    }
                }
            }

            Log.Info("檢查新會限影片完成");
        }

        //https://github.com/member-gentei/member-gentei/blob/90f62385f554eb4c02ed8732e15061b9dd1dd6d0/gentei/membership/membership.go#L331
        //https://discord.com/channels/@me/userChannel.Id
        public async Task CheckMemberShip(object stats)
        {
            bool isOldCheck = (bool)stats;

            using (var db = DataBase.DBContext.GetDbContext())
            {
                var needCheckList = db.GuildConfig.Include((x) => x.MemberCheck).Where((x) => !string.IsNullOrEmpty(x.MemberCheckChannelId) && x.MemberCheckVideoId != "-").ToList();
                Log.Info((isOldCheck ? "舊" : "新") + $"會限檢查開始: {needCheckList.Count}個伺服器");

                foreach (var guildConfig in needCheckList)
                {
                    var list = guildConfig.MemberCheck
                        .Where((x) => (isOldCheck && x.LastCheckStatus != DataBase.Table.YoutubeMemberCheck.CheckStatus.NotYetStarted) ||
                            (!isOldCheck && x.LastCheckStatus == DataBase.Table.YoutubeMemberCheck.CheckStatus.NotYetStarted))
                        .ToList();
                    if (list.Count == 0)
                        continue;

                    var guild = _client.GetGuild(guildConfig.GuildId);
                    if (guild == null)
                    {
                        Log.Warn($"{guildConfig.GuildId} Guild不存在");
                        continue;
                    }

                    var role = guild.GetRole(guildConfig.MemberCheckGrantRoleId);
                    if (role == null)
                    {
                        Log.Warn($"{guildConfig.GuildId} RoleId錯誤 {guildConfig.MemberCheckGrantRoleId}");
                        continue;
                    }

                    var logChannel = guild.GetTextChannel(guildConfig.LogMemberStatusChannelId);
                    if (logChannel == null)
                    {
                        Log.Warn($"{guildConfig.GuildId} 無紀錄頻道");
                        continue;
                    }

                    var permission = guild.GetUser(_client.CurrentUser.Id).GetPermissions(logChannel);
                    if (!permission.ViewChannel || !permission.SendMessages)
                    {
                        Log.Warn($"{guildConfig.GuildId} / {guildConfig.LogMemberStatusChannelId} 無權限可紀錄");
                        continue;
                    }

                    if (!guild.GetUser(_client.CurrentUser.Id).GuildPermissions.ManageRoles)
                    {
                        await logChannel.SendMessageAsync("我沒有權限可以編輯用戶組，請幫我開啟伺服器的 `管理身分組` 權限");
                        continue;
                    }

                    int checkedMemberCount = 0;
                    foreach (var item2 in list)
                    {
                        var user = await _client.Rest.GetUserAsync(item2.UserId);
                        var userChannel = await user.CreateDMChannelAsync();
                        if (userChannel == null) Log.Warn($"{item2.UserId} 無法建立使用者私訊");

                        var token = await flow.LoadTokenAsync(item2.UserId.ToString(), CancellationToken.None);
                        if (token == null)
                        {
                            await logChannel.SendErrorMessage(user, new EmbedBuilder().AddField("檢查頻道", guildConfig.MemberCheckChannelId).AddField("狀態", "未登入"));
                            await userChannel.SendErrorMessage($"未登入，請至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 登入並再次於伺服器執行 `/youtube-member check`");

                            try
                            {
                                await _client.Rest.RemoveRoleAsync(guild.Id, item2.UserId, role.Id).ConfigureAwait(false);
                            }
                            catch (Exception ex) { Log.Warn(ex.ToString()); }

                            db.YoutubeMemberCheck.Remove(item2);
                            db.SaveChanges();
                            continue;
                        }

                        UserCredential userCredential = null;
                        try
                        {
                            userCredential = await GetUserCredential(item2.UserId.ToString(), token);
                        }
                        catch (Exception ex)
                        {
                            if (ex.Message == "RefreshToken空白")
                            {
                                await logChannel.SendErrorMessage(user, new EmbedBuilder().AddField("檢查頻道", guildConfig.MemberCheckChannelId).AddField("狀態", "無法重複驗證"));
                                await userChannel.SendErrorMessage($"無法重新刷新您的授權\n" +
                                    $"請到 {Format.Url("Google安全性", "https://myaccount.google.com/permissions")} 移除 `直播小幫手會限確認` 的應用程式存取權後\n" +
                                    $"至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 重新登入並再次於伺服器執行 `/youtube-member check`");

                                await flow.DataStore.DeleteAsync<TokenResponse>(item2.UserId.ToString());
                                continue;
                            }

                            Log.Error(ex.ToString());
                            continue;
                        }

                        if (userCredential == null)
                        {
                            await logChannel.SendErrorMessage(user, new EmbedBuilder().AddField("檢查頻道", guildConfig.MemberCheckChannelId).AddField("狀態", "認證過期"));
                            await userChannel.SendErrorMessage($"您的Google認證已失效\n" +
                                $"請到 {Format.Url("Google安全性", "https://myaccount.google.com/permissions")} 移除 `直播小幫手會限確認` 的應用程式存取權後\n" +
                                $"至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 重新登入並再次於伺服器執行 `/youtube-member check`");

                            try
                            {
                                await _client.Rest.RemoveRoleAsync(guild.Id, item2.UserId, role.Id).ConfigureAwait(false);
                            }
                            catch (Exception ex) { Log.Warn(ex.ToString()); }

                            db.YoutubeMemberCheck.Remove(item2);
                            db.SaveChanges();
                            continue;
                        }

                        var service = new YouTubeService(new BaseClientService.Initializer()
                        {
                            HttpClientInitializer = userCredential,
                            ApplicationName = "Discord Youtube Member Check"
                        }).CommentThreads.List("id");
                        bool isMember = false;

                        service.VideoId = guildConfig.MemberCheckVideoId;
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
                                    Log.Warn($"CheckMemberStatus: {guildConfig.GuildId} - {item2.UserId} 會限資格取得失敗: {guildConfig.MemberCheckVideoId}已關閉留言");
                                    await (await Program.ApplicatonOwner.CreateDMChannelAsync()).SendErrorMessage($"{guildConfig.GuildId} - {item2.UserId} 會限資格取得失敗: {guildConfig.MemberCheckVideoId}已關閉留言");

                                    guildConfig.MemberCheckVideoId = "-";
                                    db.GuildConfig.Update(guildConfig);
                                    db.SaveChanges();

                                    break;
                                }
                                else if (ex.Message.ToLower().Contains("403") || ex.Message.ToLower().Contains("the request might not be properly authorized"))
                                {
                                    Log.Warn($"CheckMemberStatus: {guildConfig.GuildId} - {item2.UserId} 會限資格取得失敗: 無會員");

                                    db.YoutubeMemberCheck.Remove(item2);
                                    db.SaveChanges();

                                    try
                                    {
                                        await _client.Rest.RemoveRoleAsync(guild.Id, item2.UserId, role.Id).ConfigureAwait(false);
                                    }
                                    catch (Exception ex2) { Log.Warn(ex2.ToString()); }

                                    await logChannel.SendErrorMessage(user, new EmbedBuilder().AddField("檢查頻道", guildConfig.MemberCheckChannelId).AddField("狀態", "會員已過期"));
                                    await userChannel.SendErrorMessage($"您在 `{guild.Name}` 的會限資格已失效\n" +
                                        $"如要重新驗證會員請於購買會員後再次於伺服器執行 `/youtube-member check`");
                                    continue;
                                }
                                else if (ex.Message.ToLower().Contains("token has been expired or revoked") ||
                                    ex.Message.ToLower().Contains("the access token has expired and could not be refreshed") ||
                                    ex.Message.ToLower().Contains("authenticateduseraccountclosed") || ex.Message.ToLower().Contains("authenticateduseraccountsuspended"))
                                {
                                    Log.Warn($"CheckMemberStatus: {guildConfig.GuildId} - {item2.UserId} 會限資格取得失敗: AccessToken已過期或無法刷新");
                                    Log.Warn(JsonConvert.SerializeObject(userCredential.Token));
                                    Log.Warn(ex.ToString());

                                    db.YoutubeMemberCheck.Remove(item2);
                                    db.SaveChanges();

                                    try
                                    {
                                        await _client.Rest.RemoveRoleAsync(guild.Id, item2.UserId, role.Id).ConfigureAwait(false);
                                    }
                                    catch (Exception ex2) { Log.Warn(ex2.ToString()); }

                                    await logChannel.SendErrorMessage(user, new EmbedBuilder().AddField("檢查頻道", guildConfig.MemberCheckChannelId).AddField("狀態", "認證過期"));
                                    await userChannel.SendErrorMessage($"您的Google認證已失效\n" +
                                        $"請到 {Format.Url("Google安全性", "https://myaccount.google.com/permissions?continue=https%3A%2F%2Fmyaccount.google.com%2Fsecurity")} 移除 `直播小幫手會限確認` 的應用程式存取權後\n" +
                                        $"至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 重新登入並再次於伺服器執行 `/youtube-member check`");
                                    continue;
                                }
                                else if (ex.Message.ToLower().Contains("the added or subtracted value results in an un-representable"))
                                {
                                    Log.Error($"CheckMemberStatus: {guildConfig.GuildId} - {item2.UserId} 會限資格取得失敗: 時間加減錯誤");
                                    Log.Error(ex.ToString());

                                    db.YoutubeMemberCheck.Remove(item2);
                                    db.SaveChanges();

                                    try
                                    {
                                        await _client.Rest.RemoveRoleAsync(guild.Id, item2.UserId, role.Id).ConfigureAwait(false);
                                    }
                                    catch (Exception ex2) { Log.Warn(ex2.ToString()); }

                                    await logChannel.SendErrorMessage(user, new EmbedBuilder().AddField("檢查頻道", guildConfig.MemberCheckChannelId).AddField("狀態", "時間加減錯誤"));
                                    await userChannel.SendErrorMessage($"遇到已知但尚未處理的問題，您可以重新嘗試登入\n" +
                                        $"請到 {Format.Url("Google安全性", "https://myaccount.google.com/permissions?continue=https%3A%2F%2Fmyaccount.google.com%2Fsecurity")} 移除 `直播小幫手會限確認` 的應用程式存取權後\n" +
                                        $"至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 重新登入並再次於伺服器執行 `/youtube-member check`");
                                    continue;
                                }
                                else
                                {
                                    Log.Error($"CheckMemberStatus: {guildConfig.GuildId} - {item2.UserId} 會限資格取得失敗: 未知的錯誤");
                                    Log.Error(ex.ToString());

                                    db.YoutubeMemberCheck.Remove(item2);
                                    db.SaveChanges();

                                    try
                                    {
                                        await _client.Rest.RemoveRoleAsync(guild.Id, item2.UserId, role.Id).ConfigureAwait(false);
                                    }
                                    catch (Exception ex2) { Log.Warn(ex2.ToString()); }

                                    await logChannel.SendErrorMessage(user, new EmbedBuilder().AddField("檢查頻道", guildConfig.MemberCheckChannelId).AddField("狀態", "不明的錯誤"));
                                    await userChannel.SendErrorMessage($"無法驗證您的帳號，請向 {Program.ApplicatonOwner} 確認問題");
                                    continue;
                                }
                            }
                            catch (Exception ex2)
                            {
                                Log.Error($"CheckMemberStatus: {guildConfig.GuildId} - {item2.UserId} 回傳會限資格訊息失敗: {ex}");
                                Log.Error(ex2.ToString());
                            }
                        }

                        if (!isMember) return;

                        checkedMemberCount++; 
                        try
                        {
                            await _client.Rest.AddRoleAsync(guild.Id, item2.UserId, role.Id).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"無法新增用戶組至用戶: {guild.Id} / {user.Id}");
                            Log.Error($"{ex}");

                            await logChannel.SendErrorMessage(user, new EmbedBuilder().AddField("檢查頻道", guildConfig.MemberCheckChannelId).AddField("狀態", "已驗證但無法給予用戶組"));
                            await userChannel.SendConfirmMessage($"你在 `{guild}` 的會限已通過驗證，但無法新增用戶組，請告知管理員協助新增");

                            continue;
                        }

                        try
                        {
                            item2.LastCheckStatus = DataBase.Table.YoutubeMemberCheck.CheckStatus.Success;
                            item2.LastCheckTime = DateTime.Now;

                            db.YoutubeMemberCheck.Update(item2);
                            db.SaveChanges();

                            try
                            {
                                if (!isOldCheck)
                                {
                                    await logChannel.SendConfirmMessage(user, new EmbedBuilder().AddField("檢查頻道", guildConfig.MemberCheckChannelId).AddField("狀態", "已驗證"));
                                    await userChannel.SendConfirmMessage($"你在 `{guild}` 的會限已通過驗證，現在你可至該伺服器上觀看會限頻道了");
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
                            Log.Error($"SaveDatebase: {guildConfig.GuildId} - {item2.UserId} 資料庫儲存失敗");
                            Log.Error($"{ex}");
                        }
                    }

                    await logChannel.SendConfirmMessage((isOldCheck ? "舊" : "新") + "會限驗證完成", $"本次驗證 {list.Count} 位成員，共 {checkedMemberCount} 位驗證成功");
                }
            }

            Log.Info("會限檢查完畢");
        }

        //Todo: 實作用戶組檢查
        private async Task CheckRoleStatus()
        {
            return;
        }

        private async Task<UserCredential> GetUserCredential(string discordUserId, TokenResponse token)
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
                        Log.Info($"{discordUserId} AccessToken過期，重新刷新");
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

        public async Task<bool> IsExistUserTokenAsync(string discordUserId)
        {
            return await ((RedisDataStore)flow.DataStore).IsExistUserTokenAsync<TokenResponse>(discordUserId);
        }
        
        public async Task RevokeUserGoogleCert(string userId = "")
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                    throw new NullReferenceException("userId");

                var token = await flow.LoadTokenAsync(userId, CancellationToken.None);
                if (token == null)
                    throw new NullReferenceException("token");

                string revokeToken = token.RefreshToken ?? token.AccessToken; 
                await flow.RevokeTokenAsync(userId, revokeToken, CancellationToken.None);

                Log.Info($"{userId} 已解除Google憑證");
            }
            catch (Exception ex)   
            {
                await flow.DeleteTokenAsync(userId, CancellationToken.None);
                Log.Error($"RevokeToken: {ex}");
                throw;
            }
        }
    }

    static class Ext
    {
        public static async Task SendConfirmMessage(this SocketTextChannel tc, IUser user, EmbedBuilder embedBuilder)
            => await tc.SendMessageAsync(embed: embedBuilder.WithOkColor().WithAuthor(user).WithThumbnailUrl(user.GetAvatarUrl()).Build());

        public static async Task SendConfirmMessage(this SocketTextChannel tc, string title, string dec)
            => await tc.SendMessageAsync(embed: new EmbedBuilder().WithOkColor().WithTitle(title).WithDescription(dec).Build());

        public static async Task SendErrorMessage(this SocketTextChannel tc, IUser user, EmbedBuilder embedBuilder)
            => await tc.SendMessageAsync(embed: embedBuilder.WithErrorColor().WithAuthor(user).WithThumbnailUrl(user.GetAvatarUrl()).Build());

        public static async Task SendConfirmMessage(this IDMChannel dc, string text)
        {
            if (dc == null) return;

            try
            {
                await dc.SendMessageAsync(embed: new EmbedBuilder().WithOkColor().WithDescription(text).Build());
            }
            catch (Discord.Net.HttpException ex)
            {
                if (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                    Log.Warn($"無法傳送訊息至: {dc.Name}");
                else
                    Log.Error(ex.ToString());
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
        }

        public static async Task SendErrorMessage(this IDMChannel dc, string text)
        {
            if (dc == null) return;

            try
            {
                await dc.SendMessageAsync(embed: new EmbedBuilder().WithErrorColor().WithDescription(text).Build());
            }
            catch (Discord.Net.HttpException ex)
            {
                if (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                    Log.Warn($"無法傳送訊息至: {dc.Name}");
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