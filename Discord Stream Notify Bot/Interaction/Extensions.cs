﻿using Discord;
using Discord.WebSocket;
using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Interaction
{
    static class Extensions
    {
        private static readonly IEmote arrow_left = new Emoji("⬅");
        private static readonly IEmote arrow_right = new Emoji("➡");

        public static EmbedBuilder WithOkColor(this EmbedBuilder eb) =>
           eb.WithColor(00, 229, 132);
        public static EmbedBuilder WithErrorColor(this EmbedBuilder eb) =>
           eb.WithColor(40, 40, 40);
        public static EmbedBuilder WithRecordColor(this EmbedBuilder eb) =>
           eb.WithColor(255, 0, 0);

        public static DateTime ConvertToDateTime(this string str) =>
           DateTime.Parse(str);

        public static string ConvertDateTimeToDiscordMarkdown(this DateTime dateTime)
        {
            long UTCTime = ((DateTimeOffset)dateTime).ToUnixTimeSeconds();
            return $"<t:{UTCTime}:F> (<t:{UTCTime}:R>)";
        }

        public static StreamVideo.YTChannelType GetProductionType(this StreamVideo streamVideo)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                StreamVideo.YTChannelType type;
                var channel = db.YoutubeChannelOwnedType.FirstOrDefault((x) => x.ChannelId == streamVideo.ChannelId);

                if (channel != null)
                    type = channel.ChannelType;
                else
                    type = streamVideo.ChannelType;

                return type;
            }
        }

        public static string GetProductionName(this StreamVideo.YTChannelType channelType) =>
                channelType == StreamVideo.YTChannelType.Holo ? "Hololive" : channelType == StreamVideo.YTChannelType.Nijisanji ? "彩虹社" : "其他";

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

        public static bool HasStreamVideoByVideoId(this DataBase.DBContext dBContext, string videoId)
        {
            videoId = videoId.Trim();
            return dBContext.HoloStreamVideo.Any((x) => x.VideoId == videoId) ||
                dBContext.NijisanjiStreamVideo.Any((x) => x.VideoId == videoId) ||
                dBContext.OtherStreamVideo.Any((x) => x.VideoId == videoId);
        }

        public static StreamVideo GetStreamVideoByVideoId(this DataBase.DBContext dBContext, string videoId)
        {
            videoId = videoId.Trim();
            StreamVideo streamVideo;
            if ((streamVideo = dBContext.HoloStreamVideo.FirstOrDefault((x) => x.VideoId == videoId)) != null)
                return streamVideo;
            if ((streamVideo = dBContext.NijisanjiStreamVideo.FirstOrDefault((x) => x.VideoId == videoId)) != null)
                return streamVideo;
            if ((streamVideo = dBContext.OtherStreamVideo.FirstOrDefault((x) => x.VideoId == videoId)) != null)
                return streamVideo;
            return null;                
        }

        public static StreamVideo GetLastStreamVideoByChannelId(this DataBase.DBContext dBContext, string channelId)
        {
            channelId = channelId.Trim();
            StreamVideo streamVideo;
            if ((streamVideo = dBContext.HoloStreamVideo.LastOrDefault((x) => x.ChannelId == channelId)) != null)
                return streamVideo;
            if ((streamVideo = dBContext.NijisanjiStreamVideo.LastOrDefault((x) => x.ChannelId == channelId)) != null)
                return streamVideo;
            if ((streamVideo = dBContext.OtherStreamVideo.LastOrDefault((x) => x.ChannelId == channelId)) != null)
                return streamVideo;
            return null;
        }

        public static string GetChannelTitleByChannelId(this DataBase.DBContext dBContext, string channelId)
        {
            channelId = channelId.Trim();
            StreamVideo streamVideo;
            if ((streamVideo = dBContext.HoloStreamVideo.FirstOrDefault((x) => x.ChannelId == channelId)) != null)
                return streamVideo.ChannelTitle;
            if ((streamVideo = dBContext.NijisanjiStreamVideo.FirstOrDefault((x) => x.ChannelId == channelId)) != null)
                return streamVideo.ChannelTitle;
            if ((streamVideo = dBContext.OtherStreamVideo.FirstOrDefault((x) => x.ChannelId == channelId)) != null)
                return streamVideo.ChannelTitle;
            return channelId;
        }

        public static string GetTwitterUserNameByUserScreenName(this DataBase.DBContext dBContext, string userScreenName)
        {
            userScreenName = userScreenName.Trim();
            var twitterSpaecSpider = dBContext.TwitterSpaecSpider.FirstOrDefault((x) => x.UserScreenName.ToLower() == userScreenName.ToLower());
            if (twitterSpaecSpider != null)
                return twitterSpaecSpider.UserName;
            else
                return userScreenName;
        }

        public static bool IsChannelInDb(this DataBase.DBContext dBContext, string channelId)
            => dBContext.HoloStreamVideo.Any((x) => x.ChannelId == channelId) ||
                dBContext.NijisanjiStreamVideo.Any((x) => x.ChannelId == channelId) ||
                dBContext.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId);

        public static bool IsTwitterUserInDb(this DataBase.DBContext dBContext, string userId)
            => dBContext.TwitterSpaecSpider.Any((x) => x.UserId == userId);

        public static Task SendConfirmAsync(this IDiscordInteraction di, string des, bool isFollowerup = false, bool ephemeral = false)
        {
            if (isFollowerup)
                return di.FollowupAsync(embed: new EmbedBuilder().WithOkColor().WithDescription(des).Build(), ephemeral: ephemeral);
            else
                return di.RespondAsync(embed: new EmbedBuilder().WithOkColor().WithDescription(des).Build(), ephemeral: ephemeral);
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
            if (isFollowerup)
                return di.FollowupAsync(embed: new EmbedBuilder().WithErrorColor().WithDescription(des).Build(), ephemeral: ephemeral);
            else
                return di.RespondAsync(embed: new EmbedBuilder().WithErrorColor().WithDescription(des).Build(), ephemeral: ephemeral);
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


        public static Task SendPaginatedConfirmAsync(this IInteractionContext ctx, int currentPage, Func<int, EmbedBuilder> pageFunc, int totalElements, int itemsPerPage, bool addPaginatedFooter = true, bool ephemeral = false)
            => ctx.SendPaginatedConfirmAsync(currentPage, (x) => Task.FromResult(pageFunc(x)), totalElements, itemsPerPage, addPaginatedFooter, ephemeral);

        public static async Task SendPaginatedConfirmAsync(this IInteractionContext ctx, int currentPage,
    Func<int, Task<EmbedBuilder>> pageFunc, int totalElements, int itemsPerPage, bool addPaginatedFooter = true, bool ephemeral = false)
        {
            var embed = await pageFunc(currentPage).ConfigureAwait(false);

            var lastPage = (totalElements - 1) / itemsPerPage;

            if (addPaginatedFooter)
                embed.AddPaginatedFooter(currentPage, lastPage);

            await ctx.Interaction.RespondAsync(ephemeral ? "私人回應，無法換頁\n如需換頁請直接使用指令換頁" : null, embed: embed.Build(), ephemeral: ephemeral).ConfigureAwait(false);

            if (ephemeral)
                return;

            var msg = await ctx.Interaction.GetOriginalResponseAsync().ConfigureAwait(false);
            if (lastPage == 0)
                return;

            await msg.AddReactionAsync(arrow_left).ConfigureAwait(false);
            await msg.AddReactionAsync(arrow_right).ConfigureAwait(false);

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
