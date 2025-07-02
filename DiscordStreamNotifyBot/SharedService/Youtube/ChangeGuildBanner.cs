namespace DiscordStreamNotifyBot.SharedService.Youtube
{
    public partial class YoutubeStreamService
    {
        private async Task ChangeGuildBannerAsync(string channelId, string videoId)
        {
#if DEBUG || DEBUG_DONTREGISTERCOMMAND
            return;
#endif
            List<DataBase.Table.BannerChange> list;

            using (var db = _dbService.GetDbContext())
            {
                list = _dbService.GetDbContext().BannerChange.AsNoTracking()
                    .Where(x => x.ChannelId == channelId)
                    .ToList();
            }

            if (list.Count == 0) return;

            foreach (var item in list)
            {
                try
                {
                    var guild = _client.GetGuild(item.GuildId);
                    if (guild == null)
                    {
                        Log.Warn($"Guild not found: {item.GuildId} / {channelId} / {videoId}");
                        using (var db = _dbService.GetDbContext())
                        {
                            db.BannerChange.Remove(item);
                            await db.SaveChangesAsync();
                        }
                        continue;
                    }

                    if (guild.PremiumTier < PremiumTier.Tier2) continue;

                    if (videoId != item.LastChangeStreamId)
                    {
                        MemoryStream memStream;
                        try
                        {
                            memStream = new MemoryStream(await _httpClientFactory.CreateClient("").GetByteArrayAsync($"https://i.ytimg.com/vi/{videoId}/maxresdefault.jpg"));
                            if (memStream.Length < 2048) memStream = null;
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"DownloadGuildBanner - {item.GuildId}\n" +
                                $"{channelId} / {videoId}\n" +
                                $"{ex.Message}\n" +
                                $"{ex.StackTrace}");
                            continue;
                        }

                        try
                        {
                            if (memStream != null)
                            {
                                Image image = new Image(memStream);
                                await guild.ModifyAsync((func) => func.Banner = image);
                            }

                            item.LastChangeStreamId = videoId;

                            using (var db = _dbService.GetDbContext())
                            {
                                db.BannerChange.Update(item);
                                await db.SaveChangesAsync();
                            }

                            Log.Info("ChangeGuildBanner" + (memStream == null ? "(Without Change)" : "") + $": {item.GuildId} / {videoId}");
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex.Demystify(), $"ChangeGuildBanner - {item.GuildId}: {channelId} / {videoId}");
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Demystify(), $"ChangeGuildBanner - {item.GuildId}");
                    continue;
                }
            }
        }
    }
}
