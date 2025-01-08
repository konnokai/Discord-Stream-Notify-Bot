using Discord.Commands;
using Discord_Stream_Notify_Bot.Command.Attribute;
using Discord_Stream_Notify_Bot.DataBase.Table;

namespace Discord_Stream_Notify_Bot.Command.Twitch
{
    public partial class Twitch : TopLevelModule, ICommandService
    {
        private readonly DiscordSocketClient _client;
        private readonly SharedService.Twitch.TwitchService _service;

        public Twitch(DiscordSocketClient client, SharedService.Twitch.TwitchService service)
        {
            _client = client;
            _service = service;
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("TwitchAddSpiderToGuild")]
        [Summary("新增爬蟲並指定伺服器")]
        [Alias("tastg")]
        public async Task AddSpiderToGuild(string channelUrl, ulong guildId)
        {
            string userLogin = _service.GetUserLoginByUrl(channelUrl);

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var twitchSpider = db.TwitchSpider.SingleOrDefault((x) => x.UserLogin == userLogin);
                if (twitchSpider != null)
                {
                    await Context.Channel.SendErrorAsync($"`{userLogin}` 已被 `{twitchSpider.GuildId}` 設定").ConfigureAwait(false);
                    return;
                }

                var user = await _service.GetUserAsync(twitchUserLogin: userLogin);
                if (user == null)
                {
                    await Context.Channel.SendErrorAsync($"頻道 `{userLogin}` 不存在").ConfigureAwait(false);
                    return;
                }

                db.TwitchSpider.Add(new TwitchSpider() { UserId = user.Id, GuildId = guildId, UserLogin = user.Login, IsWarningUser = false, UserName = user.DisplayName });
                db.SaveChanges();

                await Context.Channel.SendConfirmAsync($"已將 `{user.DisplayName}` (`{user.Login}`) 設定至 `{guildId}`").ConfigureAwait(false);
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("TwitchSetChannelSpiderGuildId")]
        [Summary("設定爬蟲頻道的伺服器 Id")]
        [CommandExample("https://twitch.tv/998rrr 0")]
        [Alias("tscsg")]
        public async Task SetChannelSpiderGuildId([Summary("頻道網址")] string channelUrl, ulong guildId = 0)
        {
            string userLogin = _service.GetUserLoginByUrl(channelUrl);

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var twitchSpider = db.TwitchSpider.SingleOrDefault((x) => x.UserLogin == userLogin);
                if (twitchSpider != null)
                {
                    twitchSpider.GuildId = guildId;
                    db.TwitchSpider.Update(twitchSpider);
                    db.SaveChanges();

                    await Context.Channel.SendConfirmAsync($"已設定 `{twitchSpider.UserName}` (`{twitchSpider.UserLogin}`) 的 GuildId 為 `{guildId}`").ConfigureAwait(false);
                }
                else
                {
                    await Context.Channel.SendErrorAsync($"尚未設定 `{userLogin}` 的爬蟲").ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("TwitchToggleIsTrustedChannel")]
        [Summary("切換頻道是否為認可頻道")]
        [CommandExample("https://twitch.tv/998rrr")]
        [Alias("tttc")]
        public async Task ToggleIsTrustedChannel([Summary("頻道網址")] string channelUrl = "")
        {
            string userLogin = _service.GetUserLoginByUrl(channelUrl);

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var twitchSpider = db.TwitchSpider.SingleOrDefault((x) => x.UserLogin == userLogin);
                if (twitchSpider != null)
                {
                    twitchSpider.IsWarningUser = !twitchSpider.IsWarningUser;
                    db.TwitchSpider.Update(twitchSpider);
                    db.SaveChanges();

                    await Context.Channel.SendConfirmAsync($"已設定 `{twitchSpider.UserName}` (`{twitchSpider.UserLogin}`) 為 __" + (twitchSpider.IsWarningUser ? "已" : "未") + "__ 認可頻道").ConfigureAwait(false);
                }
                else
                {
                    await Context.Channel.SendErrorAsync($"尚未設定 `{userLogin}` 的爬蟲").ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("TwitchRemoveChannelSpider")]
        [Summary("移除頻道爬蟲")]
        [CommandExample("https://twitch.tv/998rrr")]
        [Alias("trcs")]
        public async Task RemoveChannelSpider([Summary("頻道網址")] string channelUrl = "")
        {
            string userLogin = _service.GetUserLoginByUrl(channelUrl);

            using (var db = DataBase.MainDbContext.GetDbContext())
            {
                var twitchSpider = db.TwitchSpider.SingleOrDefault((x) => x.UserLogin == userLogin);
                if (twitchSpider != null)
                {
                    db.TwitchSpider.Remove(twitchSpider);
                    db.SaveChanges();

                    await Context.Channel.SendConfirmAsync($"已移除 `{twitchSpider.UserName}` 的爬蟲").ConfigureAwait(false);
                }
                else
                {
                    await Context.Channel.SendErrorAsync($"尚未設定 `{userLogin}` 的爬蟲").ConfigureAwait(false);
                }
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("CreateEventSubSubscription")]
        [Summary("建立事件訂閱頻道")]
        [CommandExample("174268844")]
        [Alias("cess")]
        public async Task CreateEventSubSubscriptionAsync(string broadcasterUserId)
        {
            await Context.Channel.TriggerTypingAsync();

            if (await _service.CreateEventSubSubscriptionAsync(broadcasterUserId))
            {
                await Context.Channel.SendConfirmAsync($"已註冊圖奇事件通知: {broadcasterUserId}");
            }
            else
            {
                await Context.Channel.SendErrorAsync($"圖奇事件通知註冊失敗");
            }
        }

        [RequireContext(ContextType.DM)]
        [RequireOwner]
        [Command("TwitchGetLatestVOD")]
        [Summary("取得 Twitch 頻道最新的 VOD 資訊")]
        [CommandExample("https://twitch.tv/998rrr")]
        [Alias("tglv")]
        public async Task GetLatestVOD([Summary("頻道網址")] string channelUrl)
        {
            try
            {
                await Context.Channel.TriggerTypingAsync();

                string userLogin = _service.GetUserLoginByUrl(channelUrl);

                var user = await _service.GetUserAsync(twitchUserLogin: userLogin);
                if (user == null)
                {
                    await Context.Channel.SendErrorAsync("找不到對應的 User 資料");
                    return;
                }

                var video = await _service.GetLatestVODAsync(user.Id);
                if (video == null)
                {
                    await Context.Channel.SendErrorAsync("找不到對應的 Video 資料");
                    return;
                }

                var createAt = DateTime.Parse(video.CreatedAt);
                var endAt = createAt + _service.ParseToTimeSpan(video.Duration);
                var clips = await _service.GetClipsAsync(video.UserId, createAt, endAt);

                Log.Debug($"Duration: {video.Duration} | createAt: {createAt} | endAt: {endAt}");

                var embedBuilder = new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle(video.Title)
                    .WithUrl(video.Url)
                    .WithDescription($"{Format.Url($"{video.UserName}", $"https://twitch.tv/{video.UserLogin}")}")
                    .WithImageUrl(video.ThumbnailUrl.Replace("%{width}", "854").Replace("%{height}", "480"))
                    .AddField("開始時間", createAt.ConvertDateTimeToDiscordMarkdown())
                    .AddField("結束時間", endAt.ConvertDateTimeToDiscordMarkdown())
                    .AddField("直播時長", video.Duration.Replace("h", "時").Replace("m", "分").Replace("s", "秒"));

                if (clips != null && clips.Any((x) => x.VideoId == video.Id))
                {
                    Log.Debug(JsonConvert.SerializeObject(clips));
                    int i = 0;
                    embedBuilder.AddField("最多觀看的 Clip", string.Join('\n', clips.Where((x) => x.VideoId == video.Id)
                        .Select((x) => $"{i++}. {Format.Url(x.Title, x.Url)} By `{x.CreatorName}` (`{x.ViewCount}` 次觀看)")));
                }

                await Context.Channel.SendMessageAsync(embed: embedBuilder.Build());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TwitchGetLatestVOD Error");
                await Context.Channel.SendErrorAsync("Error");
            }
        }
    }
}
