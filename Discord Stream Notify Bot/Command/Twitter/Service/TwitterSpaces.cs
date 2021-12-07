﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SocialOpinionAPI.Core;
using SocialOpinionAPI.Services.Spaces;
using SocialOpinionAPI.Services.Users;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord.WebSocket;
using Discord;
using System.Runtime.InteropServices;
using System.Diagnostics;
using SocialOpinionAPI.Models.Users;

namespace Discord_Stream_Notify_Bot.Command.Twitter.Service
{
    public partial class TwitterService
    {
        public bool IsEnbale { get; private set; } = true;
        public UserService UserService { get; private set; }
        public SpacesService SpacesService { get; private set; }

        OAuthInfo oAuthInfo;
        bool isRuning = false;
        //List<string> spaceRunningList = new();
        DiscordSocketClient _client;
        Timer timer;

        public TwitterService(DiscordSocketClient client, BotConfig botConfig)
        {
            if (string.IsNullOrWhiteSpace(botConfig.TwitterApiKey) || string.IsNullOrWhiteSpace(botConfig.TwitterApiKeySecret))
            {
                Log.Warn("TwitterApiKey或TwitterApiKeySecret遺失，無法運行推特類功能");
                IsEnbale = false;
                return;
            }

            _client = client;
            oAuthInfo = new() { ConsumerKey = botConfig.TwitterApiKey, ConsumerSecret = botConfig.TwitterApiKeySecret };
            UserService = new(oAuthInfo);
            SpacesService = new(oAuthInfo);

            timer = new(async (stats) =>
            {
                if (isRuning) return; isRuning = true;
                try
                {
                    using (var db = DataBase.DBContext.GetDbContext())
                    {
                        var userList = db.TwitterSpaecSpider.Select((x) => x.UserId).ToArray();

                        for (int i = 0; i < userList.Length; i += 100)
                        {
                            try
                            {
                                var spaces = SpacesService.LookupByCreatorId(userList.Skip(i).Take(100).ToList()); i += 100;
                                if (spaces.data.Count <= 0) continue;

                                foreach (var item in spaces.data)
                                {
                                    if (db.TwitterSpace.Any((x) => x.SpaecId == item.id)) continue;

                                    try
                                    {
                                        var user = db.TwitterSpaecSpider.FirstOrDefault((x) => x.UserId == item.creator_id);
                                        var userData = UserService.GetUser(user.UserScreenName);

                                        if (user.UserScreenName == null || user.UserName == null)
                                        {
                                            user.UserScreenName = userData.data.username;
                                            user.UserName = userData.data.name;
                                            await db.SaveChangesAsync();
                                        }

                                        var spaceData = new TwitterSpace() { UserId = item.creator_id, UserName = user.UserName, UserScreenName = user.UserScreenName, SpaecId = item.id, SpaecTitle = item.title, SpaecActualStartTime = (DateTime)(item?.started_at).GetValueOrDefault().AddHours(8) };
                                        db.TwitterSpace.Add(spaceData);

                                        if (IsRecordSpace(spaceData))
                                        {
                                            try
                                            {
                                                await SendSpaceMessageAsync(userData, spaceData, true);

                                                var metadataJson = GetTwitterSpaceMetadata(item.id);
                                                var masterUrl = GetTwitterSpaceMasterUrl(metadataJson["media_key"].ToString());
                                                RecordSpace(spaceData, masterUrl);
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.Error($"Spaces-Record {item.id} {ex.Message}\r\n{ex.StackTrace}");
                                                await SendSpaceMessageAsync(userData, spaceData);
                                            }
                                        }
                                        else await SendSpaceMessageAsync(userData, spaceData);
                                    }
                                    catch (Exception ex) { Log.Error($"Spaces-Data {item.id} {ex.Message}\r\n{ex.StackTrace}"); }
                                }
                                await db.SaveChangesAsync();
                            }
                            catch (Exception ex) { Log.Error($"Prepare-Spaces {ex.Message}\r\n{ex.StackTrace}"); }
                        }
                    }
                }
                catch (Exception ex) { Log.Error($"Spaces-Timer {ex.Message}\r\n{ex.StackTrace}"); }
                finally { isRuning = false; }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
        }

        public UserModel GetTwitterUser(string userScreenName)
        {
            if (string.IsNullOrWhiteSpace(userScreenName))
                return null;

            try
            {
                return UserService.GetUser(userScreenName);
            }
            catch (Exception) { return null; }
        }

        private bool IsRecordSpace(TwitterSpace twitterSpace)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                try
                {
                    return db.TwitterSpaecSpider.Any((x) => x.UserId == twitterSpace.UserId);
                }
                catch (Exception ex)
                {
                    Log.Error($"IsRecordSpace: {twitterSpace.SpaecId} {ex.Message}\r\n{ex.StackTrace}");
                    return false;
                }
            }
        }

        private async Task SendSpaceMessageAsync(UserModel userModel, TwitterSpace twitterSpace, bool isRecord = false)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var noticeGuildList = db.NoticeTwitterSpaceChannel.Where((x) => x.NoticeTwitterSpaceUserId == twitterSpace.UserId).ToList();
                Log.NewStream($"發送推特空間開台通知 ({noticeGuildList.Count}): {twitterSpace.UserScreenName} - {twitterSpace.SpaecTitle}");

                EmbedBuilder embedBuilder = new EmbedBuilder()
                    .WithTitle(twitterSpace.SpaecTitle)
                    .WithDescription(Format.Url($"{twitterSpace.UserName}", $"https://twitter.com/{twitterSpace.UserScreenName}"))
                    .WithUrl($"https://twitter.com/i/spaces/{twitterSpace.SpaecId}/peek")
                    .WithThumbnailUrl(userModel.data.profile_image_url)
                    .WithFooter($"開始時間: {twitterSpace.SpaecActualStartTime}");

                if (isRecord) embedBuilder.WithRecordColor();
                else embedBuilder.WithOkColor();

                foreach (var item in noticeGuildList)
                {
                    try
                    {
                        var guild = _client.GetGuild(item.GuildId);
                        if (guild == null) continue;
                        var channel = guild.GetTextChannel(item.DiscordChannelId);
                        if (channel == null) continue;

                        await channel.SendMessageAsync(item.StratTwitterSpaceMessage, false, embedBuilder.Build());
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Notice Space {item.GuildId} / {item.DiscordChannelId}\r\n{ex.Message}");
                        if (ex.Message.Contains("50013") || ex.Message.Contains("50001")) db.NoticeTwitterSpaceChannel.Remove(db.NoticeTwitterSpaceChannel.First((x) => x.DiscordChannelId == item.DiscordChannelId));
                        await db.SaveChangesAsync();
                    }
                }
            }
        }

        private JToken GetTwitterSpaceMetadata(string spaceId)
        {
            string query = WebUtility.UrlEncode(JsonConvert.SerializeObject(new
            {
                id = spaceId,
                isMetatagsQuery = false,
                withSuperFollowsUserFields = true,
                withUserResults = true,
                withBirdwatchPivots = false,
                withReactionsMetadata = false,
                withReactionsPerspective = false,
                withSuperFollowsTweetFields = true,
                withReplays = true,
                withScheduledSpaces = true
            }));

            try
            {
                string url = "https://twitter.com/i/api/graphql/jyQ0_DEMZHeoluCgHJ-U5Q/AudioSpaceById?variables=" + query;
                HttpWebRequest request = HttpWebRequest.Create(url) as HttpWebRequest;
                request.Method = "GET";
                request.ContentType = "application/json";
                request.Timeout = 30000;
                request.Headers.Add(HttpRequestHeader.Authorization, "Bearer AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs=1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA");
                request.Headers.Add("x-guest-token", GetTwitterGuestToken());
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/96.0.4664.45 Safari/537.36";
                request.Referer = " https://twitter.com/";

                // 取得回應資料
                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                {
                    using (var res = new StreamReader(response.GetResponseStream()))
                    {
                        return JObject.Parse(res.ReadToEnd())["data"]["audioSpace"]["metadata"];
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        private string GetTwitterSpaceMasterUrl(string mediaKey)
        {
            try
            {
                string url = $"https://twitter.com/i/api/1.1/live_video_stream/status/{mediaKey}";
                HttpWebRequest request = HttpWebRequest.Create(url) as HttpWebRequest;
                request.Method = "GET";
                request.ContentType = "application/json";
                request.Timeout = 30000;
                request.Headers.Add(HttpRequestHeader.Authorization, "Bearer AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs=1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA");
                request.Headers.Add(HttpRequestHeader.Cookie, "auth_token=");
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/96.0.4664.45 Safari/537.36";
                request.Referer = " https://twitter.com/";

                // 取得回應資料
                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                {
                    using (var res = new StreamReader(response.GetResponseStream()))
                    {
                        return JObject.Parse(res.ReadToEnd())["source"]["location"].ToString();
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        private string GetTwitterGuestToken()
        {
            try
            {
                HttpWebRequest request = HttpWebRequest.Create("https://twitter.com") as HttpWebRequest;
                request.Method = "GET";
                request.ContentType = "application/x-www-form-urlencoded";
                request.Timeout = 30000;
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/96.0.4664.45 Safari/537.36";

                using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
                {
                    using (var res = new StreamReader(response.GetResponseStream()))
                    {
                        string webHtml = res.ReadToEnd();
                        Regex regex = new Regex(@"(?<=gt\=)\d{19}");
                        var r = regex.Match(webHtml);
                        return r.Value;
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        private void RecordSpace(TwitterSpace twitterSpace ,string masterUrl)
        {
            Log.Info($"{twitterSpace.UserName} ({twitterSpace.SpaecTitle}): {masterUrl}");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) Process.Start("tmux", $"new-window -d -n \"Twitter Space @{twitterSpace.UserScreenName}\" ffmpeg -i \"{masterUrl}\" \"/mnt/live/twitter_{twitterSpace.UserId}_{twitterSpace.SpaecId}.m4a\"");
            else Process.Start(new ProcessStartInfo()
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{masterUrl}\" \"{Program.GetDataFilePath($"twitter_{twitterSpace.UserId}_{twitterSpace.SpaecId}.m4a")}\"",
                CreateNoWindow = false,
                UseShellExecute = true
            });
        }
    }
}