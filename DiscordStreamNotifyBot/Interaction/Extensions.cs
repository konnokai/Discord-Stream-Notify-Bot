using DiscordStreamNotifyBot.DataBase;
using DiscordStreamNotifyBot.DataBase.Table;
using DiscordStreamNotifyBot.Interaction;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Management;
using System.Reflection;

namespace DiscordStreamNotifyBot.Interaction
{
    static class Extensions
    {
        private static readonly IEmote arrow_left = new Emoji("\u2B05");
        private static readonly IEmote arrow_right = new Emoji("\u27A1");

        public static EmbedBuilder WithOkColor(this EmbedBuilder eb) =>
           eb.WithColor(00, 229, 132);
        public static EmbedBuilder WithErrorColor(this EmbedBuilder eb) =>
           eb.WithColor(40, 40, 40);
        public static EmbedBuilder WithRecordColor(this EmbedBuilder eb) =>
           eb.WithColor(255, 0, 0);

        public static string ConvertDateTimeToDiscordMarkdown(this DateTime dateTime)
        {
            long UTCTime = ((DateTimeOffset)dateTime).ToUnixTimeSeconds();
            return $"<t:{UTCTime}:F> (<t:{UTCTime}:R>)";
        }

        public static DataBase.Table.Video.YTChannelType GetProductionType(this DataBase.Table.Video streamVideo)
        {
            using (var db = Bot.DbService.GetDbContext())
            {
                DataBase.Table.Video.YTChannelType type;
                var channel = db.YoutubeChannelOwnedType.AsNoTracking().FirstOrDefault((x) => x.ChannelId == streamVideo.ChannelId);

                if (channel != null)
                    type = channel.ChannelType;
                else
                    type = streamVideo.ChannelType;

                return type;
            }
        }

        public static string GetProductionName(this DataBase.Table.Video.YTChannelType channelType) =>
                channelType == DataBase.Table.Video.YTChannelType.Holo ? "Hololive" : channelType == DataBase.Table.Video.YTChannelType.Nijisanji ? "彩虹社" : "其他";

        public static string GetCommandLine(this Process process)
        {
            if (!OperatingSystem.IsWindows()) return "";

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + process.Id))
                using (ManagementObjectCollection objects = searcher.Get())
                {
                    return objects.Cast<ManagementBaseObject>().SingleOrDefault()?["CommandLine"]?.ToString();
                }
            }
            catch
            {
                return "";
            }
        }

        public static IEnumerable<T> Distinct<T, V>(this IEnumerable<T> source, Func<T, V> keySelector)
        {
            return source.Distinct(new CommonEqualityComparer<T, V>(keySelector));
        }

        public static bool HasStreamVideoByVideoId(string videoId)
        {
            videoId = videoId.Trim();

            using var db = Bot.DbService.GetDbContext();
            if (db.HoloVideos.AsNoTracking().Any((x) => x.VideoId == videoId)) return true;
            if (db.NijisanjiVideos.AsNoTracking().Any((x) => x.VideoId == videoId)) return true;
            if (db.OtherVideos.AsNoTracking().Any((x) => x.VideoId == videoId)) return true;
            if (db.NonApprovedVideos.AsNoTracking().Any((x) => x.VideoId == videoId)) return true;

            return false;
        }

        public static DataBase.Table.Video GetStreamVideoByVideoId(string videoId)
        {
            videoId = videoId.Trim();

            using var db = Bot.DbService.GetDbContext();
            if (db.HoloVideos.AsNoTracking().Any((x) => x.VideoId == videoId))
                return db.HoloVideos.AsNoTracking().First((x) => x.VideoId == videoId);
            if (db.NijisanjiVideos.AsNoTracking().Any((x) => x.VideoId == videoId))
                return db.NijisanjiVideos.AsNoTracking().First((x) => x.VideoId == videoId);
            if (db.OtherVideos.AsNoTracking().Any((x) => x.VideoId == videoId))
                return db.OtherVideos.AsNoTracking().First((x) => x.VideoId == videoId);
            if (db.NonApprovedVideos.AsNoTracking().Any((x) => x.VideoId == videoId))
                return db.NonApprovedVideos.AsNoTracking().First((x) => x.VideoId == videoId);

            return null;
        }

        // 照開始直播時間排序好像會遇到聊天用待機室的問題，函數先保留起來可能之後會用到?
        public static DataBase.Table.Video GetLastStreamVideoByChannelId(string channelId)
        {
            channelId = channelId.Trim();

            using var db = Bot.DbService.GetDbContext();
            if (db.HoloVideos.AsNoTracking().Any((x) => x.ChannelId == channelId))
                return db.HoloVideos.AsNoTracking().OrderByDescending((x) => x.ScheduledStartTime).First((x) => x.ChannelId == channelId);
            if (db.NijisanjiVideos.AsNoTracking().Any((x) => x.ChannelId == channelId))
                return db.NijisanjiVideos.AsNoTracking().OrderByDescending((x) => x.ScheduledStartTime).First((x) => x.ChannelId == channelId);
            if (db.OtherVideos.AsNoTracking().Any((x) => x.ChannelId == channelId))
                return db.OtherVideos.AsNoTracking().OrderByDescending((x) => x.ScheduledStartTime).First((x) => x.ChannelId == channelId);
            if (db.NonApprovedVideos.AsNoTracking().Any((x) => x.ChannelId == channelId))
                return db.NonApprovedVideos.AsNoTracking().OrderByDescending((x) => x.ScheduledStartTime).First((x) => x.ChannelId == channelId);

            return null;
        }

        public static bool IsChannelInDb(string channelId)
        {
            channelId = channelId.Trim();

            using var db = Bot.DbService.GetDbContext();
            if (db.HoloVideos.AsNoTracking().Any((x) => x.ChannelId == channelId)) return true;
            if (db.NijisanjiVideos.AsNoTracking().Any((x) => x.ChannelId == channelId)) return true;
            if (db.OtherVideos.AsNoTracking().Any((x) => x.ChannelId == channelId)) return true;
            if (db.NonApprovedVideos.AsNoTracking().Any((x) => x.ChannelId == channelId)) return true;

            return false;
        }

        public static string GetYoutubeChannelTitleByChannelId(this MainDbContext _, string channelId)
        {
            channelId = channelId.Trim();

            using var db = Bot.DbService.GetDbContext();

            YoutubeChannelSpider youtubeChannelSpider;
            if ((youtubeChannelSpider = db.YoutubeChannelSpider.AsNoTracking().FirstOrDefault((x) => x.ChannelId == channelId)) != null)
                return youtubeChannelSpider.ChannelTitle;

            if (db.HoloVideos.AsNoTracking().Any((x) => x.ChannelId == channelId))
                return db.HoloVideos.AsNoTracking().OrderByDescending((x) => x.ScheduledStartTime).First((x) => x.ChannelId == channelId).ChannelTitle;
            if (db.NijisanjiVideos.AsNoTracking().Any((x) => x.ChannelId == channelId))
                return db.NijisanjiVideos.AsNoTracking().OrderByDescending((x) => x.ScheduledStartTime).First((x) => x.ChannelId == channelId).ChannelTitle;
            if (db.OtherVideos.AsNoTracking().Any((x) => x.ChannelId == channelId))
                return db.OtherVideos.AsNoTracking().OrderByDescending((x) => x.ScheduledStartTime).First((x) => x.ChannelId == channelId).ChannelTitle;

            return channelId;
        }

        public static string GetNonApprovedChannelTitleByChannelId(this MainDbContext _, string channelId)
        {
            channelId = channelId.Trim();

            using var db = Bot.DbService.GetDbContext();

            YoutubeChannelSpider youtubeChannelSpider;
            if ((youtubeChannelSpider = db.YoutubeChannelSpider.FirstOrDefault((x) => x.ChannelId == channelId)) != null)
                return youtubeChannelSpider.ChannelTitle;

            if (db.NonApprovedVideos.AsNoTracking().Any((x) => x.ChannelId == channelId))
                return db.NonApprovedVideos.OrderByDescending((x) => x.ScheduledStartTime).First((x) => x.ChannelId == channelId).ChannelId;

            return channelId;
        }

        public static string GetTwitCastingChannelTitleByScreenId(this MainDbContext _, string screenId)
        {
            screenId = screenId.Trim();

            using var db = Bot.DbService.GetDbContext();

            TwitcastingSpider twitcastingSpider;
            if ((twitcastingSpider = db.TwitcastingSpider.AsNoTracking().FirstOrDefault((x) => x.ScreenId == screenId)) != null)
                return twitcastingSpider.ChannelTitle;

            return screenId;
        }

        public static string GetTwitchUserNameByUserId(this MainDbContext _, string userId)
        {
            userId = userId.Trim();

            using var db = Bot.DbService.GetDbContext();

            TwitchSpider twitchSpider;
            if ((twitchSpider = db.TwitchSpider.AsNoTracking().FirstOrDefault((x) => x.UserId == userId)) != null)
                return twitchSpider.UserName;

            return userId;
        }

        public static Task SendConfirmAsync(this IDiscordInteraction di, string des, bool isFollowerup = false, bool ephemeral = false)
        {
            if (isFollowerup)
            {
                return di.FollowupAsync(embed: new EmbedBuilder().WithOkColor().WithDescription(des).Build(), ephemeral: ephemeral);
            }
            else
            {
                try
                {
                    return di.RespondAsync(embed: new EmbedBuilder().WithOkColor().WithDescription(des).Build(), ephemeral: ephemeral);
                }
                catch (Exception ex) when (ex.Message.ToLower().Contains("cannot respond twice to the same interaction"))
                {
                    return di.FollowupAsync(embed: new EmbedBuilder().WithOkColor().WithDescription(des).Build(), ephemeral: ephemeral);
                }
            }
        }

        public static Task SendConfirmAsync(this IDiscordInteraction di, string title, string des, bool isFollowerup = false, bool ephemeral = false)
        {
            if (isFollowerup)
                return di.FollowupAsync(embed: new EmbedBuilder().WithOkColor().WithTitle(title).WithDescription(des).Build(), ephemeral: ephemeral);
            else
                return di.RespondAsync(embed: new EmbedBuilder().WithOkColor().WithTitle(title).WithDescription(des).Build(), ephemeral: ephemeral);
        }

        public static Task SendErrorAsync(this IDiscordInteraction di, string des, bool isFollowerup = false, bool ephemeral = true)
        {
            Log.Warn($"回傳錯誤給 [{di.User.Username}]: {des}");

            if (isFollowerup)
            {
                return di.FollowupAsync(embed: new EmbedBuilder().WithErrorColor().WithDescription(des).Build(), ephemeral: ephemeral);
            }
            else
            {
                try
                {
                    return di.RespondAsync(embed: new EmbedBuilder().WithErrorColor().WithDescription(des).Build(), ephemeral: ephemeral);
                }
                catch (Exception ex) when (ex.Message.ToLower().Contains("cannot respond twice to the same interaction"))
                {
                    return di.FollowupAsync(embed: new EmbedBuilder().WithErrorColor().WithDescription(des).Build(), ephemeral: ephemeral);
                }
            }
        }

        public static Task SendErrorAsync(this IDiscordInteraction di, string title, string des, bool isFollowerup = false, bool ephemeral = true)
        {
            if (isFollowerup)
                return di.FollowupAsync(embed: new EmbedBuilder().WithErrorColor().WithTitle(title).WithDescription(des).Build(), ephemeral: ephemeral);
            else
                return di.RespondAsync(embed: new EmbedBuilder().WithErrorColor().WithTitle(title).WithDescription(des).Build(), ephemeral: ephemeral);
        }

        public static IMessage DeleteAfter(this IUserMessage msg, int seconds)
        {
            Task.Run(async () =>
            {
                await Task.Delay(seconds * 1000).ConfigureAwait(false);
                try { await msg.DeleteAsync().ConfigureAwait(false); }
                catch { }
            });
            return msg;
        }

        public static IEnumerable<Type> LoadInteractionFrom(this IServiceCollection collection, Assembly assembly)
        {
            List<Type> addedTypes = new List<Type>();

            Type[] allTypes;
            try
            {
                allTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine(ex.Message + "\n" + ex.Source);
                return Enumerable.Empty<Type>();
            }

            var services = new Queue<Type>(allTypes
                    .Where(x => x.GetInterfaces().Contains(typeof(IInteractionService))
                        && !x.GetTypeInfo().IsInterface && !x.GetTypeInfo().IsAbstract)
                    .ToArray());

            addedTypes.AddRange(services);

            var interfaces = new HashSet<Type>(allTypes
                    .Where(x => x.GetInterfaces().Contains(typeof(IInteractionService))
                        && x.GetTypeInfo().IsInterface));

            while (services.Count > 0)
            {
                var serviceType = services.Dequeue();

                if (collection.FirstOrDefault(x => x.ServiceType == serviceType) != null)
                    continue;

                var interfaceType = interfaces.FirstOrDefault(x => serviceType.GetInterfaces().Contains(x));
                if (interfaceType != null)
                {
                    addedTypes.Add(interfaceType);
                    collection.AddSingleton(interfaceType, serviceType);
                }
                else
                {
                    collection.AddSingleton(serviceType, serviceType);
                }
            }

            return addedTypes;
        }

        public static Task<IUserMessage> EmbedAsync(this IDiscordInteraction di, EmbedBuilder embed, string msg = "", bool ephemeral = false)
            => di.FollowupAsync(msg, embed: embed.Build(),
                options: new RequestOptions() { RetryMode = RetryMode.AlwaysRetry }, ephemeral: ephemeral);

        public static Task<IUserMessage> EmbedAsync(this IDiscordInteraction di, string msg = "", bool ephemeral = false)
           => di.FollowupAsync(embed: new EmbedBuilder().WithOkColor().WithDescription(msg).Build(),
               options: new RequestOptions { RetryMode = RetryMode.AlwaysRetry }, ephemeral: ephemeral);


        public static Task SendPaginatedConfirmAsync(this IInteractionContext ctx, int currentPage, Func<int, EmbedBuilder> pageFunc, int totalElements, int itemsPerPage, bool addPaginatedFooter = true, bool ephemeral = false, bool isFollowup = false)
            => ctx.SendPaginatedConfirmAsync(currentPage, (x) => Task.FromResult(pageFunc(x)), totalElements, itemsPerPage, addPaginatedFooter, ephemeral, isFollowup);

        public static async Task SendPaginatedConfirmAsync(this IInteractionContext ctx, int currentPage,
    Func<int, Task<EmbedBuilder>> pageFunc, int totalElements, int itemsPerPage, bool addPaginatedFooter = true, bool ephemeral = false, bool isFollowup = false)
        {
            var embed = await pageFunc(currentPage).ConfigureAwait(false);

            var lastPage = (totalElements - 1) / itemsPerPage;

            if (addPaginatedFooter)
                embed.AddPaginatedFooter(currentPage, lastPage);

            if (isFollowup) await ctx.Interaction.FollowupAsync(ephemeral ? "私人回應，無法換頁\n如需換頁請直接使用指令換頁" : null, embed: embed.Build(), ephemeral: ephemeral).ConfigureAwait(false);
            else await ctx.Interaction.RespondAsync(ephemeral ? "私人回應，無法換頁\n如需換頁請直接使用指令換頁" : null, embed: embed.Build(), ephemeral: ephemeral).ConfigureAwait(false);

            if (ephemeral)
                return;

            if (lastPage == 0)
                return;

            var msg = await ctx.Interaction.GetOriginalResponseAsync().ConfigureAwait(false);

            try
            {
                await msg.AddReactionAsync(arrow_left).ConfigureAwait(false);
                await msg.AddReactionAsync(arrow_right).ConfigureAwait(false);
            }
            catch (Discord.Net.HttpException httpEx) when (httpEx.DiscordCode == DiscordErrorCode.MissingPermissions)
            {
                await ctx.Interaction.ModifyOriginalResponseAsync((act) => act.Content = "無法換頁，如需換頁請直接使用指令換頁");
                return;
            }

            await Task.Delay(2000).ConfigureAwait(false);

            var lastPageChange = DateTime.MinValue;

            async Task changePage(SocketReaction r)
            {
                try
                {
                    if (r.UserId != ctx.User.Id)
                        return;
                    if (DateTime.UtcNow - lastPageChange < TimeSpan.FromSeconds(1))
                        return;
                    if (r.Emote.Name == arrow_left.Name)
                    {
                        if (currentPage == 0)
                            return;
                        lastPageChange = DateTime.UtcNow;
                        var toSend = await pageFunc(--currentPage).ConfigureAwait(false);
                        if (addPaginatedFooter)
                            toSend.AddPaginatedFooter(currentPage, lastPage);
                        await msg.ModifyAsync(x => x.Embed = toSend.Build()).ConfigureAwait(false);
                    }
                    else if (r.Emote.Name == arrow_right.Name)
                    {
                        if (lastPage > currentPage)
                        {
                            lastPageChange = DateTime.UtcNow;
                            var toSend = await pageFunc(++currentPage).ConfigureAwait(false);
                            if (addPaginatedFooter)
                                toSend.AddPaginatedFooter(currentPage, lastPage);
                            await msg.ModifyAsync(x => x.Embed = toSend.Build()).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("被取消了");
                    //ignored
                }
            }

            using (msg.OnReaction((DiscordSocketClient)ctx.Client, changePage, changePage))
            {
                await Task.Delay(30000).ConfigureAwait(false);
            }

            try
            {
                if (msg.Channel is ITextChannel && ((SocketGuild)ctx.Guild).CurrentUser.GuildPermissions.ManageMessages)
                {
                    await msg.RemoveAllReactionsAsync().ConfigureAwait(false);
                }
                else
                {
                    await Task.WhenAll(msg.Reactions.Where(x => x.Value.IsMe)
                        .Select(x => msg.RemoveReactionAsync(x.Key, ctx.Client.CurrentUser)));
                }
            }
            catch
            {
                // ignored
            }
        }

        public static EmbedBuilder AddPaginatedFooter(this EmbedBuilder embed, int curPage, int? lastPage)
        {
            if (lastPage != null)
                return embed.WithFooter(efb => efb.WithText($"{curPage + 1} / {lastPage + 1}"));
            else
                return embed.WithFooter(efb => efb.WithText(curPage.ToString()));
        }

        public static ReactionEventWrapper OnReaction(this IUserMessage msg, DiscordSocketClient client, Func<SocketReaction, Task> reactionAdded, Func<SocketReaction, Task> reactionRemoved = null)
        {
            if (reactionRemoved == null)
                reactionRemoved = _ => Task.CompletedTask;

            var wrap = new ReactionEventWrapper(client, msg);
            wrap.OnReactionAdded += (r) => { var _ = Task.Run(() => reactionAdded(r)); };
            wrap.OnReactionRemoved += (r) => { var _ = Task.Run(() => reactionRemoved(r)); };
            return wrap;
        }
    }
}
