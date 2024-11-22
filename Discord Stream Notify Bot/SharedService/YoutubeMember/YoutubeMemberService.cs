using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction;
using Discord_Stream_Notify_Bot.SharedService.Youtube;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Polly;

namespace Discord_Stream_Notify_Bot.SharedService.YoutubeMember
{
    public partial class YoutubeMemberService : IInteractionService
    {
        public bool IsEnable { get; private set; } = true;

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
                IsEnable = false;
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

            Program.RedisSub.Subscribe(new RedisChannel("member.revokeToken", RedisChannel.PatternMode.Literal), async (channel, value) =>
            {
                try
                {
                    ulong userId = 0;
                    if (!ulong.TryParse(value.ToString(), out userId))
                        return;

                    Log.Info($"接收到 Redis 的 Revoke 請求: {userId}");

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

                    using MainDbContext db = MainDbContext.GetDbContext();
                    if (customId[1] == "check" && customId.Length == 4)
                    {
                        await component.DeferAsync(true);

                        if (!ulong.TryParse(customId[2], out ulong guildId))
                        {
                            await component.SendErrorAsync("GuildId 無效，請向孤之界回報此問題", true);
                            Log.Error(JsonConvert.SerializeObject(component));
                            return;
                        }

                        if (!ulong.TryParse(customId[3], out ulong userId))
                        {
                            await component.SendErrorAsync("UserId 無效，請向孤之界回報此問題", true);
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

                        await component.SendConfirmAsync("已記錄至資料庫，請稍等至多 5 分鐘讓 Bot 驗證\n請確認已開啟本伺服器的 `允許來自伺服器成員的私人訊息`，以避免收不到通知", true, true);
                    }
                }
                catch (Exception ex)
                {
                    await component.SendErrorAsync("錯誤，請向孤之界回報此問題", true);
                    Log.Error(ex.ToString());
                    return;
                }
            };

            checkMemberShipOnlyVideoId = new Timer(CheckMemberShipOnlyVideoId, null, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5));
            checkOldMemberStatus = new Timer(new TimerCallback(async (obj) => await CheckMemberShip(obj)), true, TimeSpan.FromSeconds(Math.Round(Convert.ToDateTime($"{DateTime.Now.AddDays(1):yyyy/MM/dd 04:00:00}").Subtract(DateTime.Now).TotalSeconds)), TimeSpan.FromDays(1));
            checkNewMemberStatus = new Timer(new TimerCallback(async (obj) => await CheckMemberShip(obj)), false, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(5));

            Task.Run(async () =>
            {
                try
                {
                    var redisKeyList = Program.Redis.GetServer(Program.Redis.GetEndPoints(true).First()).Keys(1, pattern: $"Google.Apis.Auth.OAuth2.Responses.TokenResponse:*", cursor: 0, pageSize: 20000);
                    if (redisKeyList.Any())
                    {
                        Log.Info("開始儲存 Youtube Member Access Token");

                        using var db = MainDbContext.GetDbContext();
                        var redisDb = Program.Redis.GetDatabase(1);

                        foreach (var item in redisKeyList)
                        {
                            var userId = ulong.Parse(item.ToString().Split(':')[1]);
                            var value = await redisDb.StringGetAsync(item);
                            var youtubeMemberAccessToken = db.YoutubeMemberAccessToken.SingleOrDefault((x) => x.DiscordUserId == userId);
                            if (youtubeMemberAccessToken == null)
                            {
                                youtubeMemberAccessToken = new YoutubeMemberAccessToken { DiscordUserId = userId, EncryptedAccessToken = value };
                                db.YoutubeMemberAccessToken.Add(youtubeMemberAccessToken);
                            }
                            else
                            {
                                youtubeMemberAccessToken.EncryptedAccessToken = value;
                                db.YoutubeMemberAccessToken.Update(youtubeMemberAccessToken);
                            }
                        }

                        await db.SaveChangesAsync();

                        Log.Info("儲存 Youtube Member Access Token 完成");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "儲存 Youtube Member Access Token 失敗");
                }
            });

            Program.RedisSub.Publish(new RedisChannel("member.syncRedisToken", RedisChannel.PatternMode.Literal), _botConfig.RedisTokenKey);
            Log.Info("已同步 Redis Token");
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

                Log.Info($"{discordUserId} 已解除 Google 憑證");
                await RemoveMemberCheckFromDbAsync(ulong.Parse(discordUserId));
            }
            catch (Exception ex)
            {
                await flow.DeleteTokenAsync(discordUserId, CancellationToken.None);
                Log.Error(ex, "RevokeToken");
                throw;
            }
        }

        public async Task RemoveMemberCheckFromDbAsync(ulong userId)
        {
            try
            {
                using var db = MainDbContext.GetDbContext();

                if (!db.YoutubeMemberCheck.Any((x) => x.UserId == userId))
                {
                    Log.Warn($"接收到 Remove 請求但不存在於資料庫內: {userId}");
                    return;
                }

                Log.Info($"接收到 Remove 請求: {userId}");

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

                var youtubeMemberAccessToken = db.YoutubeMemberAccessToken.FirstOrDefault((x) => x.DiscordUserId == userId);
                if (youtubeMemberAccessToken != null)
                    db.YoutubeMemberAccessToken.Remove(youtubeMemberAccessToken);

                db.YoutubeMemberCheck.RemoveRange(youtubeMembers);
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AfterRevokeUserCertAsync");
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
            using var db = MainDbContext.GetDbContext();

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
                        Log.Warn($"SendMsgToLogChannelAsync: {item.GuildId} 無 GuildConfig");
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

                    var embed = new EmbedBuilder()
                        .WithErrorColor()
                        .WithDescription(msg)
                        .Build();

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
                throw new NullReferenceException("RefreshToken 空白");

            var credential = new UserCredential(flow, discordUserId, token);

            try
            {
                if (token.IsStale)
                {
                    if (!await credential.RefreshTokenAsync(CancellationToken.None))
                    {
                        Log.Warn($"{discordUserId} AccessToken 無法刷新");
                        await flow.DataStore.DeleteAsync<TokenResponse>(discordUserId);
                        credential = null;
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.ToLower().Contains("token has been expired or revoked") ||
                    ex.Message.ToLower().Contains("invalid_grant"))
                {
                    Log.Warn($"{discordUserId} AccessToken 已取消授權");
                }
                else
                {
                    Log.Error(ex, $"{discordUserId} AccessToken 發生未知錯誤");
                }

                await flow.DataStore.DeleteAsync<TokenResponse>(discordUserId);
                credential = null;
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

        public static async Task<IUserMessage> SendConfirmMessageAsync(this ITextChannel tc, DiscordSocketClient client, ulong userId, EmbedBuilder embedBuilder)
        {
            try
            {
                embedBuilder.WithOkColor();

                var user = await client.Rest.GetUserAsync(userId);
                if (user != null)
                {
                    embedBuilder
                        .WithAuthor(user)
                        .WithThumbnailUrl(user.GetAvatarUrl());
                }

                return await Policy.Handle<TimeoutException>()
                    .Or<Discord.Net.HttpException>((httpEx) => ((int)httpEx.HttpCode).ToString().StartsWith("50"))
                    .WaitAndRetryAsync(3, (retryAttempt) =>
                    {
                        var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                        Log.Warn($"YoutubeMemberService-SendConfirmMessageAsync 通知 | {tc.Id} / {userId} 發送失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                        return timeSpan;
                    })
                    .ExecuteAsync(async () =>
                    {
                        return await tc.SendMessageAsync(embed: embedBuilder.Build(), options: new RequestOptions() { RetryMode = RetryMode.AlwaysRetry });
                    });
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"YoutubeMemberService-SendConfirmMessageAsync: {userId} ({tc.Name} / {tc.Id})");
                throw;
            }
        }

        public static async Task<IUserMessage> SendConfirmMessageAsync(this ITextChannel tc, string title, string dec)
        {
            try
            {
                return await Policy.Handle<TimeoutException>()
                    .Or<Discord.Net.HttpException>((httpEx) => ((int)httpEx.HttpCode).ToString().StartsWith("50"))
                    .WaitAndRetryAsync(3, (retryAttempt) =>
                    {
                        var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                        Log.Warn($"YoutubeMemberService-SendConfirmMessageAsync 通知 | {tc.Id} 發送失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                        return timeSpan;
                    })
                    .ExecuteAsync(async () =>
                    {
                        return await tc.SendMessageAsync(embed: new EmbedBuilder().WithOkColor().WithTitle(title).WithDescription(dec).Build(), options: new RequestOptions() { RetryMode = RetryMode.AlwaysRetry });
                    });
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"YoutubeMemberService-SendConfirmMessageAsync: {tc.Name} ({tc.Id})");
                return null;
            }
        }

        public static async Task<IUserMessage> SendErrorMessageAsync(this ITextChannel tc, DiscordSocketClient client, ulong userId, string channelTitle, string status)
        {
            try
            {
                var embedBuilder = new EmbedBuilder()
                    .WithErrorColor()
                    .AddField("檢查頻道", channelTitle)
                    .AddField("狀態", status);

                var user = await client.Rest.GetUserAsync(userId);
                if (user != null)
                {
                    embedBuilder
                        .WithAuthor(user)
                        .WithThumbnailUrl(user.GetAvatarUrl());
                }

                return await Policy.Handle<TimeoutException>()
                    .Or<Discord.Net.HttpException>((httpEx) => ((int)httpEx.HttpCode).ToString().StartsWith("50"))
                    .WaitAndRetryAsync(3, (retryAttempt) =>
                    {
                        var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                        Log.Warn($"YoutubeMemberService-SendErrorMessageAsync 通知 | {tc.Id} / {userId} 發送失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                        return timeSpan;
                    })
                    .ExecuteAsync(async () =>
                    {
                        return await tc.SendMessageAsync(embed: embedBuilder.Build(), options: new RequestOptions() { RetryMode = RetryMode.AlwaysRetry });
                    });
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"YoutubeMemberService-SendErrorMessageAsync: {tc.Name} ({tc.Id})");
                return null;
            }
        }

        public static async Task SendConfirmMessageAsync(this ulong userId, DiscordSocketClient client, string text, ITextChannel tc)
        {
            var user = await client.Rest.GetUserAsync(userId) as IUser;
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
                await Policy.Handle<TimeoutException>()
                    .Or<Discord.Net.HttpException>((httpEx) => ((int)httpEx.HttpCode).ToString().StartsWith("50"))
                    .WaitAndRetryAsync(3, (retryAttempt) =>
                    {
                        var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                        Log.Warn($"YoutubeMemberService-SendUserDMConfirmMessageAsync 通知 | {userId} 發送失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                        return timeSpan;
                    })
                    .ExecuteAsync(async () =>
                    {
                        return await userChannel.SendMessageAsync(embed: new EmbedBuilder().WithOkColor().WithDescription(text).Build());
                    });
            }
            catch (Discord.Net.HttpException ex)
            {
                if (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                {
                    Log.Warn($"無法傳送訊息至: {userChannel.Name} ({userId})");
                    await tc.SendMessageAsync($"無法傳送訊息至: <@{userId}>\n請向該用戶提醒開啟 `允許來自伺服器成員的私人訊息`");
                }
                else
                {
                    Log.Error(ex, $"YoutubeMemberService-SendUserDMConfirmMessageAsync - Discord 錯誤: {userId}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"YoutubeMemberService-SendUserDMConfirmMessageAsync 錯誤: {userId}");
            }
        }

        public static async Task SendErrorMessageAsync(this ulong userId, DiscordSocketClient client, string text, ITextChannel tc)
        {
            var user = await client.Rest.GetUserAsync(userId) as IUser;
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
                await Policy.Handle<TimeoutException>()
                    .Or<Discord.Net.HttpException>((httpEx) => ((int)httpEx.HttpCode).ToString().StartsWith("50"))
                    .WaitAndRetryAsync(3, (retryAttempt) =>
                    {
                        var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                        Log.Warn($"YoutubeMemberService-SendUserDMErrorMessageAsync 通知 | {userId} 發送失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                        return timeSpan;
                    })
                    .ExecuteAsync(async () =>
                    {
                        return await userChannel.SendMessageAsync(embed: new EmbedBuilder().WithErrorColor().WithDescription(text).Build());
                    });
            }
            catch (Discord.Net.HttpException ex)
            {
                if (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                {
                    Log.Warn($"無法傳送訊息至: {userChannel.Name} ({userId})");
                    await tc.SendMessageAsync($"無法傳送訊息至: <@{userId}>\n請向該用戶提醒開啟 `允許來自伺服器成員的私人訊息`");
                }
                else
                {
                    Log.Error(ex, $"YoutubeMemberService-SendUserDMErrorMessageAsync - Discord 錯誤: {userId}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"YoutubeMemberService-SendUserDMErrorMessageAsync 錯誤: {userId}");
            }
        }

        public static async Task SendErrorMessageAsync(this IDMChannel dc, string text)
        {
            if (dc == null) return;

            try
            {
                await Policy.Handle<TimeoutException>()
                    .Or<Discord.Net.HttpException>((httpEx) => ((int)httpEx.HttpCode).ToString().StartsWith("50"))
                    .WaitAndRetryAsync(3, (retryAttempt) =>
                    {
                        var timeSpan = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                        Log.Warn($"YoutubeMemberService-SendUserDMErrorMessageAsync 通知 | {dc.Id} 發送失敗，將於 {timeSpan.TotalSeconds} 秒後重試 (第 {retryAttempt} 次重試)");
                        return timeSpan;
                    })
                    .ExecuteAsync(async () =>
                    {
                        return await dc.SendMessageAsync(embed: new EmbedBuilder().WithErrorColor().WithDescription(text).Build());
                    });
            }
            catch (Discord.Net.HttpException ex)
            {
                if (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
                {
                    Log.Warn($"無法傳送訊息至: {dc.Name}");
                }
                else
                {
                    Log.Error(ex, $"YoutubeMemberService-SendUserDMErrorMessageAsync - Discord 錯誤: {dc.Name}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"YoutubeMemberService-SendUserDMErrorMessageAsync 錯誤: {dc.Name}");
            }
        }
    }
}