using Discord;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.SharedService.Youtube
{
    public partial class YoutubeStreamService
    {
        private async Task ChangeGuildBannerAsync(string channelId, string videoId)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                foreach (var item in db.BannerChange.ToList().Where(x => x.ChannelId == channelId))
                {
                    try
                    {
                        var guild = _client.GetGuild(item.GuildId);
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
                                    Log.Error($"DownloadGuildBanner - {item.GuildId}\r\n" +
                                        $"{channelId} / {videoId}\r\n" +
                                        $"{ex.Message}\r\n" +
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
                                db.BannerChange.Update(item);
                                await db.SaveChangesAsync();

                                Log.Info("ChangeGuildBanner" + (memStream == null ? "(Without Change)" : "") + $": {item.GuildId} / {videoId}");
                            }
                            catch (Exception ex)
                            {

                                Log.Error($"ChangeGuildBanner - {item.GuildId}\r\n" +
                                    $"{channelId} / {videoId}" +
                                    $"{ex.Message}\r\n" +
                                    $"{ex.StackTrace}");
                                continue;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"ChangeGuildBanner - {item.GuildId}\r\n" +
                            $"{ex.Message}\r\n" +
                            $"{ex.StackTrace}");
                        continue;
                    }
                }
            }            
        }
    }
}
