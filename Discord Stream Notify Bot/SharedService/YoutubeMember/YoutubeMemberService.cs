using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction;
using Discord_Stream_Notify_Bot.SharedService.Youtube;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;

namespace Discord_Stream_Notify_Bot.SharedService.YoutubeMember
{
    public partial class YoutubeMemberService : IInteractionService
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

                    using DBContext db = DBContext.GetDbContext();
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
                            db.YoutubeMemberCheck.Add(new YoutubeMemberCheck() { UserId = userId, GuildId = guildId, CheckYTChannelId = item });
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
            checkOldMemberStatus = new Timer(new TimerCallback(async (obj) => await CheckMemberShip(obj)), true, TimeSpan.FromSeconds(Math.Round(Convert.ToDateTime($"{DateTime.Now.AddDays(1):yyyy/MM/dd 04:00:00}").Subtract(DateTime.Now).TotalSeconds)), TimeSpan.FromDays(1));
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
                using var db = DBContext.GetDbContext();

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

        public async Task<string> GetYoutubeDataAsync(string discordUserId)
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

        private async Task SendMsgToLogChannelAsync(string checkChannelId, string msg, bool isNeedRemove = true, bool isNeedSendToOwner = true)
        {
            using var db = DBContext.GetDbContext();

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
                        db.GuildConfig.Add(new GuildConfig { GuildId = guild.Id });
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

        private async Task<UserCredential> GetUserCredentialAsync(string discordUserId, TokenResponse token)
        {
            if (string.IsNullOrEmpty(token.RefreshToken))
                throw new NullReferenceException("RefreshToken空白");

            var credential = new UserCredential(flow, discordUserId, token);

            using (var db = DBContext.GetDbContext())
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
        // RestUser無法被序列化，暫時放棄Cache
        //private static async Task<RestUser> GetRestUserFromCatchOrCreate(ulong userId)
        //{
        //    try
        //    {
        //        var userJson = await Program.RedisDb.StringGetAsync($"discord_stream_bot:restuser:{userId}");
        //        if (userJson.IsNull)
        //        {
        //            var user = await Program._client.Rest.GetUserAsync(userId);
        //            if (user == null) return null;

        //            await Program.RedisDb.StringSetAsync($"discord_stream_bot:restuser:{userId}", JsonConvert.SerializeObject(user), TimeSpan.FromHours(1));
        //            return user;
        //        }
        //        else
        //        {
        //            RestUser restUser = JsonConvert.DeserializeObject<RestUser>(userJson.ToString());
        //            return restUser;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error($"Member-GetRestUserFromCatchOrCreate: {userId}");
        //        Log.Error(ex.ToString());
        //        return null;
        //    }
        //}

        public static async Task<IUserMessage> SendConfirmMessageAsync(this ITextChannel tc, ulong userId, EmbedBuilder embedBuilder)
        {
            try
            {
                var user = await Program._client.Rest.GetUserAsync(userId);
                if (user == null)
                    return await tc.SendMessageAsync(embed: embedBuilder.WithOkColor().Build(), options: new RequestOptions() { RetryMode = RetryMode.AlwaysRetry });
                else
                    return await tc.SendMessageAsync(embed: embedBuilder.WithOkColor().WithAuthor(user).WithThumbnailUrl(user.GetAvatarUrl()).Build(), options: new RequestOptions() { RetryMode = RetryMode.AlwaysRetry });
            }
            catch (Discord.Net.HttpException discordEx) when (discordEx.HttpCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                Log.Warn("SendConfirmMessageAsync: Discord 503 錯誤，嘗試重發...");
                await Task.Delay(3000);
                return await SendConfirmMessageAsync(tc, userId, embedBuilder);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "YoutubeMemberService-SendConfirmMessageAsync");
                throw;
            }
        }

        public static async Task<IUserMessage> SendConfirmMessageAsync(this ITextChannel tc, string title, string dec)
        {
            try
            {
                return await tc.SendMessageAsync(embed: new EmbedBuilder().WithOkColor().WithTitle(title).WithDescription(dec).Build(), options: new RequestOptions() { RetryMode = RetryMode.AlwaysRetry });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SendConfirmMessageAsync");
                return null;
            }
        }

        public static async Task<IUserMessage> SendErrorMessageAsync(this ITextChannel tc, ulong userId, string channelTitle, string status)
        {
            try
            {
                var user = await Program._client.Rest.GetUserAsync(userId);
                if (user == null)
                    return await tc.SendMessageAsync(embed: new EmbedBuilder().WithErrorColor().AddField("檢查頻道", channelTitle).AddField("狀態", status).Build(), options: new RequestOptions() { RetryMode = RetryMode.AlwaysRetry });
                else
                    return await tc.SendMessageAsync(embed: new EmbedBuilder().WithErrorColor().WithAuthor(user).WithThumbnailUrl(user.GetAvatarUrl()).AddField("檢查頻道", channelTitle).AddField("狀態", status).Build(), options: new RequestOptions() { RetryMode = RetryMode.AlwaysRetry });
            }
            catch (Discord.Net.HttpException discordEx) when (discordEx.HttpCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                Log.Warn("SendErrorMessageAsync: Discord 503 錯誤，嘗試重發...");
                await Task.Delay(3000);
                return await SendErrorMessageAsync(tc, userId, channelTitle, status);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "YoutubeMemberService-SendErrorMessageAsync");
                throw;
            }
        }

        public static async Task SendConfirmMessageAsync(this ulong userId, string text, ITextChannel tc)
        {
            var user = await Program._client.Rest.GetUserAsync(userId) as IUser;
            if (user == null)
            {
                Log.Warn($"找不到使用者 {userId}");
                return;
            }

            var userChannel = await user.CreateDMChannelAsync();
            if (userChannel == null)
            {
                Log.Warn($"{user.Id} 無法建立使用者私訊");
                return;
            }

            try
            {
                await userChannel.SendMessageAsync(embed: new EmbedBuilder().WithOkColor().WithDescription(text).Build());
            }
            catch (Discord.Net.HttpException ex)
            {
                if (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                {
                    Log.Warn($"無法傳送訊息至: {userChannel.Name} ({userId})");
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

        public static async Task SendErrorMessageAsync(this ulong userId, string text, ITextChannel tc)
        {
            var user = await Program._client.Rest.GetUserAsync(userId) as IUser;
            if (user == null)
            {
                Log.Warn($"找不到使用者 {userId}");
                return;
            }

            var userChannel = await user.CreateDMChannelAsync();
            if (userChannel == null)
            {
                Log.Warn($"{user.Id} 無法建立使用者私訊");
                return;
            }

            try
            {
                await userChannel.SendMessageAsync(embed: new EmbedBuilder().WithErrorColor().WithDescription(text).Build());
            }
            catch (Discord.Net.HttpException ex)
            {
                if (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                {
                    Log.Warn($"無法傳送訊息至: {userChannel.Name} ({userId})");
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