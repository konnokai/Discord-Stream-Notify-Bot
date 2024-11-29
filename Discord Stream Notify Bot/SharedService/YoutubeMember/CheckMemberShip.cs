using Discord_Stream_Notify_Bot.DataBase;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace Discord_Stream_Notify_Bot.SharedService.YoutubeMember
{
    public partial class YoutubeMemberService
    {
        //https://github.com/member-gentei/member-gentei/blob/90f62385f554eb4c02ed8732e15061b9dd1dd6d0/gentei/membership/membership.go#L331
        //https://discord.com/channels/@me/userChannel.Id
        public async Task CheckMemberShip(object stats)
        {
            bool isOldCheck = (bool)stats;
            int totalCheckMemberCount = 0, totalIsMemberCount = 0;

            using (var db = MainDbContext.GetDbContext())
            {
                var needCheckList = db.GuildYoutubeMemberConfig.Where((x) => !string.IsNullOrEmpty(x.MemberCheckChannelId) && !string.IsNullOrEmpty(x.MemberCheckChannelTitle) && x.MemberCheckVideoId != "-");
                if (isOldCheck && needCheckList.Any())
                {
                    try
                    {
                        int splitDay = 3;
                        int needCheckCount = needCheckList.Count() / splitDay;
                        int checkRound = 0, skipCount, lastSkipCount = 0;

                        if (File.Exists(Program.GetDataFilePath("MemberCheck.dat")))
                        {
                            string[] data = File.ReadAllText(Program.GetDataFilePath("MemberCheck.dat")).Split(',');

                            checkRound = int.Parse(data[0]);
                            lastSkipCount = int.Parse(data[1]);
                        }

                        checkRound += 1;
                        skipCount = lastSkipCount;

                        if (checkRound >= splitDay)
                        {
                            needCheckCount = needCheckList.Count() - lastSkipCount;
                        }
                        else
                        {
                            lastSkipCount += needCheckCount;
                        }

                        needCheckList = needCheckList.Skip(skipCount).Take(needCheckCount);

                        if (checkRound >= splitDay)
                        {
                            checkRound = 0;
                            lastSkipCount = 0;
                        }

                        File.WriteAllText(Program.GetDataFilePath("MemberCheck.dat"), $"{checkRound},{lastSkipCount}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex.Demystify(), "Split member check list error");
                    }
                }

                Log.Info((isOldCheck ? "舊" : "新") + $"會限檢查開始: {needCheckList.Count()} 個頻道");

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
                        db.GuildConfig.Add(new GuildConfig() { GuildId = guildYoutubeMemberConfig.GuildId });
                        Log.Warn($"{guildYoutubeMemberConfig.GuildId} Guild 不存在於資料庫內");
                        continue;
                    }

                    var guild = _client.GetGuild(guildYoutubeMemberConfig.GuildId);
                    if (guild == null)
                    {
                        Log.Warn($"{guildYoutubeMemberConfig.GuildId} Guild 不存在");
                        db.GuildYoutubeMemberConfig.RemoveRange(db.GuildYoutubeMemberConfig.Where((x) => x.GuildId == guildYoutubeMemberConfig.GuildId));
                        continue;
                    }

                    var logChannel = guild.GetTextChannel(guildConfig.LogMemberStatusChannelId);
                    if (logChannel == null)
                    {
                        Log.Warn($"{guildYoutubeMemberConfig.GuildId} 無紀錄頻道");
                        db.GuildYoutubeMemberConfig.RemoveRange(db.GuildYoutubeMemberConfig.Where((x) => x.GuildId == guildYoutubeMemberConfig.GuildId));
                        continue;
                    }

                    var role = guild.GetRole(guildYoutubeMemberConfig.MemberCheckGrantRoleId);
                    if (role == null)
                    {
                        await logChannel.SendMessageAsync($"{Format.Url(guildYoutubeMemberConfig.MemberCheckChannelId, $"https://www.youtube.com/channel/{guildYoutubeMemberConfig.MemberCheckChannelId}")} 的會限用戶組 Id 不存在，請重新設定");
                        Log.Warn($"{guildYoutubeMemberConfig.GuildId} / {guildYoutubeMemberConfig.MemberCheckChannelId} RoleId 不存在 {guildYoutubeMemberConfig.MemberCheckGrantRoleId}");
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

                    if (role == guild.EveryoneRole)
                    {
                        Log.Warn($"{guildYoutubeMemberConfig.GuildId} / {guildYoutubeMemberConfig.MemberCheckChannelId} 設定成 everoyne 用戶組");
                        await logChannel.SendMessageAsync("不可給予使用者 everyone 用戶組，請重新設定會限驗證");
                        db.GuildYoutubeMemberConfig.Remove(guildYoutubeMemberConfig);
                        continue;
                    }

                    int checkedMemberCount = 0;
                    foreach (var member in list)
                    {
                        totalCheckMemberCount++;
                        if (!checkedMemberSet.Contains($"{member.UserId}-{member.CheckYTChannelId}"))
                        {
                            var token = await flow.LoadTokenAsync(member.UserId.ToString(), CancellationToken.None);
                            if (token == null)
                            {
                                await RemoveMemberCheckFromDbAsync(member.UserId);

                                await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "未登入");
                                await member.UserId.SendErrorMessageAsync(_client, $"未登入，請至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 登入並再次於伺服器執行 `/member check`", logChannel);

                                continue;
                            }

                            UserCredential userCredential = null;
                            try
                            {
                                userCredential = await GetUserCredentialAsync(member.UserId.ToString(), token);
                            }
                            catch (Exception ex)
                            {
                                if (ex.Message == "RefreshToken空白")
                                {
                                    await RevokeUserGoogleCertAsync(member.UserId.ToString());

                                    await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "無法重複驗證");
                                    await member.UserId.SendErrorMessageAsync(_client, $"無法重新刷新您的授權\n" +
                                        $"請到 {Format.Url("Google 安全性", "https://myaccount.google.com/permissions")} 移除 `直播小幫手會限確認` 的應用程式存取權後\n" +
                                        $"至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 重新登入並再次於伺服器執行 `/member check`", logChannel);

                                    continue;
                                }

                                Log.Error(ex.Demystify().ToString());
                                continue;
                            }

                            if (userCredential == null)
                            {
                                await RemoveMemberCheckFromDbAsync(member.UserId);

                                await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "認證過期");
                                await member.UserId.SendErrorMessageAsync(_client, $"您的 Google 認證已失效\n" +
                                    $"請到 {Format.Url("Google 安全性", "https://myaccount.google.com/permissions")} 移除 `直播小幫手會限確認` 的應用程式存取權後\n" +
                                    $"至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 重新登入並再次於伺服器執行 `/member check`", logChannel);

                                continue;
                            }

                            if (guildYoutubeMemberConfig.MemberCheckVideoId == "-")
                                break;

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
                                    if (ex.Message.ToLower().Contains("parameter has disabled comments")) // Todo: 這邊可能需要在抓取新影片後重新驗證會限
                                    {
                                        Log.Warn($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {member.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗");
                                        Log.Warn($"{guildYoutubeMemberConfig.MemberCheckChannelTitle} ({guildYoutubeMemberConfig.MemberCheckChannelId}): {guildYoutubeMemberConfig.MemberCheckVideoId}已關閉留言");
                                        await Program.ApplicatonOwner.Id.SendErrorMessageAsync(_client, $"{guildYoutubeMemberConfig.GuildId} - {member.UserId} 會限資格取得失敗: {guildYoutubeMemberConfig.MemberCheckVideoId}已關閉留言", logChannel);

                                        //foreach (var item in db.GuildYoutubeMemberConfig.Where((x) => x.MemberCheckChannelId == guildYoutubeMemberConfig.MemberCheckChannelId))  
                                        //{
                                        //    db.Remove(item);
                                        //}

                                        guildYoutubeMemberConfig.MemberCheckVideoId = "-";
                                        db.GuildYoutubeMemberConfig.Update(guildYoutubeMemberConfig);
                                        db.SaveChanges();

                                        break;
                                    }
                                    else if (ex.Message.ToLower().Contains("notfound"))
                                    {
                                        Log.Warn($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {member.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗");
                                        Log.Warn($"{guildYoutubeMemberConfig.MemberCheckChannelTitle} ({guildYoutubeMemberConfig.MemberCheckChannelId}): {guildYoutubeMemberConfig.MemberCheckVideoId} 已刪除影片");
                                        await Program.ApplicatonOwner.Id.SendErrorMessageAsync(_client, $"{guildYoutubeMemberConfig.GuildId} - {member.UserId} 會限資格取得失敗: {guildYoutubeMemberConfig.MemberCheckVideoId} 已刪除影片", logChannel);

                                        guildYoutubeMemberConfig.MemberCheckVideoId = "-";
                                        db.GuildYoutubeMemberConfig.Update(guildYoutubeMemberConfig);
                                        db.SaveChanges();

                                        break;
                                    }
                                    else if (ex.Message.ToLower().Contains("403") || ex.Message.ToLower().Contains("unauthorized") || ex.Message.ToLower().Contains("the request might not be properly authorized") || ex.Message.ToLower().Contains("forbidden"))
                                    {
                                        Log.Warn($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {member.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗: 無會員");

                                        needRemoveList.Add(member);

                                        try
                                        {
                                            await _client.Rest.RemoveRoleAsync(guild.Id, member.UserId, role.Id).ConfigureAwait(false);
                                        }
                                        catch (Discord.Net.HttpException discordEx) when (discordEx.DiscordCode.Value == DiscordErrorCode.UnknownAccount ||
                                            discordEx.DiscordCode.Value == DiscordErrorCode.UnknownMember ||
                                            discordEx.DiscordCode.Value == DiscordErrorCode.UnknownUser)
                                        {
                                            Log.Warn($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {member.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 該會員已離開伺服器");
                                            continue;
                                        }
                                        catch (Discord.Net.HttpException discordEx) when (discordEx.DiscordCode == DiscordErrorCode.MissingPermissions)
                                        {
                                            Log.Warn($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {member.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 缺少權限，無法移除用戶組");
                                            await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "小幫手缺少 \"管理身分組\" 權限，無法移除用戶組\n" +
                                                "請管理員手動移除並補上小幫手的權限");
                                            continue;
                                        }
                                        catch (Exception ex2)
                                        {
                                            Log.Error(ex2, $"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {member.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 無法移除用戶組");
                                        }

                                        if (isOldCheck)
                                        {
                                            await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "會員已過期");
                                            await member.UserId.SendErrorMessageAsync(_client, $"您在 `{guild.Name}` 的 `{guildYoutubeMemberConfig.MemberCheckChannelTitle}` 會限資格已失效\n" +
                                                $"如要取消驗證請到 `{guild.Name}` 上輸入 `/member cancel-member-check`\n" +
                                                $"如要重新驗證會員請於購買會員後再次於伺服器執行 `/member check`", logChannel);
                                        }
                                        else
                                        {
                                            await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "無會員");
                                            await member.UserId.SendErrorMessageAsync(_client, $"無法在 `{guild.Name}` 的 `{guildYoutubeMemberConfig.MemberCheckChannelTitle}` 上存取會限資格\n" +
                                                $"請先使用 `/member show-youtube-account` 確認綁定的頻道是否正確，並確認已購買會員\n" +
                                                $"如要取消驗證請到 `{guild.Name}` 上輸入 `/member cancel-member-check`\n" +
                                                $"若都正確請向 `{Program.ApplicatonOwner}` 確認問題", logChannel);
                                        }
                                        continue;
                                    }
                                    else if (ex.Message.ToLower().Contains("token has been expired or revoked") ||
                                        ex.Message.ToLower().Contains("the access token has expired and could not be refreshed") ||
                                        ex.Message.ToLower().Contains("authenticateduseraccountclosed") || ex.Message.ToLower().Contains("authenticateduseraccountsuspended") ||
                                        ex.Message.ToLower().Contains("user is suspended")) // 帳號被 Google 停用
                                    {
                                        Log.Warn($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {member.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗: AccessToken 已過期或無法刷新");
                                        Log.Warn(JsonConvert.SerializeObject(userCredential.Token));
                                        Log.Warn(ex.ToString());

                                        await RemoveMemberCheckFromDbAsync(member.UserId);

                                        await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "認證過期");
                                        await member.UserId.SendErrorMessageAsync(_client, $"您的 Google 認證已失效\n" +
                                            $"請到 {Format.Url("Google 安全性", "https://myaccount.google.com/permissions?continue=https%3A%2F%2Fmyaccount.google.com%2Fsecurity")} 移除 `直播小幫手會限確認` 的應用程式存取權後\n" +
                                            $"至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 重新登入並再次於伺服器執行 `/member check`", logChannel);
                                        continue;
                                    }
                                    else if (ex.Message.ToLower().Contains("the added or subtracted value results in an un-representable"))
                                    {
                                        Log.Error($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {member.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗: 時間加減錯誤");
                                        Log.Error(ex.Demystify().ToString());

                                        await RevokeUserGoogleCertAsync(member.UserId.ToString());

                                        await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "時間加減錯誤");
                                        await member.UserId.SendErrorMessageAsync(_client, $"遇到已知但尚未處理的問題，您可以重新嘗試登入\n" +
                                            $"請到 {Format.Url("Google 安全性", "https://myaccount.google.com/permissions?continue=https%3A%2F%2Fmyaccount.google.com%2Fsecurity")} 移除 `直播小幫手會限確認` 的應用程式存取權後\n" +
                                            $"至 {Format.Url("此網站", "https://dcbot.konnokai.me/stream/")} 重新登入並再次於伺服器執行 `/member check`", logChannel);
                                        continue;
                                    }
                                    else if (ex.Message.ToLower().Contains("500") || ex.Message.ToLower().Contains("badgateway") || ex.Message.ToLower().Contains("internalservererror"))
                                    {
                                        Log.Error($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {member.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗: 500 內部錯誤");

                                        await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "Google 內部錯誤，等待下次重新檢查");
                                        continue;
                                    }
                                    else if (ex.Message.ToLower().Contains("bad req") || ex.Message.ToLower().Contains("badrequest"))
                                    {
                                        Log.Error($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {member.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗: 400 錯誤");

                                        await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "400 錯誤，等待下次重新檢查");
                                        continue;
                                    }
                                    else if (ex.Message.ToLower().Contains("quotaexceeded"))
                                    {
                                        Log.Error($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {member.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗: 無 API 配額");

                                        await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "無 API 配額，等待明天重新檢查");
                                        break;
                                    }
                                    else if (ex.Message.ToLower().Contains("resource temporarily unavailable"))
                                    {
                                        Log.Error($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {member.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗: 暫時無法存取資源");
                                        continue;
                                    }
                                    else
                                    {
                                        Log.Error($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {member.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 的會限資格取得失敗: 未知的錯誤");
                                        Log.Error(ex.Demystify().ToString());

                                        await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "不明的錯誤");
                                        await member.UserId.SendErrorMessageAsync(_client, $"無法驗證您的帳號，可能是 Google 內部錯誤\n請向 {Program.ApplicatonOwner} 確認問題", logChannel);
                                        continue;
                                    }
                                }
                                catch (Exception ex2)
                                {
                                    Log.Error($"CheckMemberStatus: {guildYoutubeMemberConfig.GuildId} - {member.UserId} \"{guildYoutubeMemberConfig.MemberCheckChannelTitle}\" 回傳會限資格訊息失敗: {ex}");
                                    Log.Error(ex2.ToString());
                                }
                            }

                            if (!isMember) continue;
                            checkedMemberSet.Add($"{member.UserId}-{member.CheckYTChannelId}");
                        }

                        checkedMemberCount++;
                        totalIsMemberCount++;
                        bool isCantAddRold = true;
                        try
                        {
                            if (!isOldCheck)
                                await _client.Rest.AddRoleAsync(guild.Id, member.UserId, role.Id).ConfigureAwait(false);
                            isCantAddRold = false;
                        }
                        catch (Discord.Net.HttpException httpEx)
                        {
                            if (httpEx.DiscordCode.HasValue && (httpEx.DiscordCode.Value == DiscordErrorCode.MissingPermissions || httpEx.DiscordCode.Value == DiscordErrorCode.InsufficientPermissions))
                            {
                                Log.Error(httpEx, $"無法新增用戶組至用戶: {guild.Id} / {member.UserId}");

                                await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "已驗證但因權限問題無法給予用戶組");
                                await member.UserId.SendConfirmMessageAsync(_client, $"你在 `{guild}` 的 `{guildYoutubeMemberConfig.MemberCheckChannelTitle}` 會限已通過驗證，但無法新增用戶組，請告知管理員協助新增", logChannel);
                            }
                            else if (httpEx.DiscordCode.HasValue && (httpEx.DiscordCode.Value == DiscordErrorCode.UnknownAccount || httpEx.DiscordCode.Value == DiscordErrorCode.UnknownMember || httpEx.DiscordCode.Value == DiscordErrorCode.UnknownUser))
                            {
                                await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "未知的使用者");
                                Log.Warn($"用戶已離開伺服器: {guild.Id} / {member.UserId}");
                                needRemoveList.Add(member);
                            }
                            else
                            {
                                Log.Error(httpEx, $"無法新增用戶組至用戶: {guild.Id} / {member.UserId}");

                                await logChannel.SendErrorMessageAsync(_client, member.UserId, guildYoutubeMemberConfig.MemberCheckChannelTitle, "已驗證但遇到未知的錯誤");
                                await member.UserId.SendConfirmMessageAsync(_client, $"你在 `{guild}` 的 `{guildYoutubeMemberConfig.MemberCheckChannelTitle}` 會限已通過驗證，但無法新增用戶組，請告知管理員協助新增", logChannel);
                            }
                        }

                        try
                        {
                            member.IsChecked = true;
                            member.LastCheckTime = DateTime.Now;
                            db.YoutubeMemberCheck.Update(member);

                            if (!isOldCheck && !isCantAddRold)
                            {
                                try
                                {
                                    await logChannel.SendConfirmMessageAsync(_client, member.UserId, new EmbedBuilder().AddField("檢查頻道", guildYoutubeMemberConfig.MemberCheckChannelTitle).AddField("狀態", "已驗證"));
                                }
                                catch (Exception ex)
                                {
                                    Log.Warn($"無法傳送紀錄訊息: {guild.Id} / {logChannel.Id}");
                                    Log.Error($"無法傳送紀錄訊息: {ex}");
                                }

                                try
                                {
                                    await member.UserId.SendConfirmMessageAsync(_client, $"你在 `{guild}` 的 `{guildYoutubeMemberConfig.MemberCheckChannelTitle}` 會限已通過驗證，現在你可至該伺服器上觀看會限頻道了", logChannel);
                                }
                                catch (Exception ex)
                                {
                                    Log.Warn($"無法傳送私訊: {guild.Id} / {member.UserId}");
                                    Log.Error($"無法傳送私訊: {ex}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"MemberCheckUpdateDb: {guildYoutubeMemberConfig.GuildId} - {member.UserId} 資料庫更新失敗");
                            Log.Error($"{ex}");
                        }
                    }

                    await logChannel.SendConfirmMessageAsync((isOldCheck ? "舊" : "新") + "會限驗證完成", $"檢查頻道: {guildYoutubeMemberConfig.MemberCheckChannelTitle}\n" +
                        $"本次驗證 {totalCheckCount} 位成員，共 {checkedMemberCount} 位驗證成功");
                }

                foreach (var item in needRemoveList)
                {
                    try
                    {
                        db.YoutubeMemberCheck.Remove(item);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"CheckMemberShip-Remove: {ex}");
                    }
                }

                var saveTime = DateTime.Now;
                bool saveFailed;
                do
                {
                    saveFailed = false;
                    try
                    {
                        db.SaveChanges();
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        saveFailed = true;
                        foreach (var item in ex.Entries)
                        {
                            try
                            {
                                item.Reload();
                            }
                            catch (Exception ex2)
                            {
                                Log.Error($"CheckMemberShip-SaveChanges-Reload");
                                Log.Error(item.DebugView.ToString());
                                Log.Error(ex2.ToString());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"CheckMemberShip-SaveChanges: {ex}");
                        Log.Error(db.ChangeTracker.DebugView.LongView);
                        await (await Program.ApplicatonOwner.CreateDMChannelAsync()).SendErrorMessageAsync($"CheckMemberShip-SaveChanges: {ex}");
                    }
                } while (saveFailed && DateTime.Now.Subtract(saveTime) <= TimeSpan.FromMinutes(1));

                needRemoveList.Clear();
            }

            if (totalCheckMemberCount > 0)
            {
                Log.Info((isOldCheck ? "舊" : "新") + $"會限檢查完畢");
                Log.Info($"總驗證: {totalCheckMemberCount} 位，成功驗證: {totalIsMemberCount} 位，驗證失敗: {totalCheckMemberCount - totalIsMemberCount} 位");
            }
        }
    }
}