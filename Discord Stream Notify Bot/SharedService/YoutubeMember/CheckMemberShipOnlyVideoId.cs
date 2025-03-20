using Discord_Stream_Notify_Bot.DataBase.Table;
using Discord_Stream_Notify_Bot.Interaction;
using Microsoft.EntityFrameworkCore;

namespace Discord_Stream_Notify_Bot.SharedService.YoutubeMember
{
    public partial class YoutubeMemberService
    {
        //https://github.com/member-gentei/member-gentei/blob/90f62385f554eb4c02ed8732e15061b9dd1dd6d0/gentei/apis/youtube.go#L100
        private async void CheckMemberShipOnlyVideoId(object stats)
        {
            using (var db = _dbService.GetDbContext())
            {
                List<GuildYoutubeMemberConfig> needRemoveList = new();
                foreach (var item in db.GuildYoutubeMemberConfig.Where((x) => !string.IsNullOrEmpty(x.MemberCheckChannelId) && x.MemberCheckChannelId.Length == 24 && (x.MemberCheckVideoId == "-" || string.IsNullOrEmpty(x.MemberCheckChannelTitle))).Distinct((x) => x.MemberCheckChannelId))
                {
                    try
                    {
                        var s = _streamService.YouTubeService.PlaylistItems.List("snippet");
                        s.PlaylistId = item.MemberCheckChannelId.Replace("UC", "UUMO");
                        var result = await s.ExecuteAsync().ConfigureAwait(false);
                        var videoList = result.Items.ToList();

                        bool isCheck = false;
                        do
                        {
                            if (!videoList.Any())
                            {
                                await Bot.ApplicatonOwner.SendMessageAsync($"{item.MemberCheckChannelId} 無任何可檢測的會限影片!");
                                await SendMsgToLogChannelAsync(item.MemberCheckChannelId, $"{item.MemberCheckChannelId} 無會限影片，請等待該頻道主有新的會限影片且可留言時再使用會限驗證功能\n" +
                                    $"你可以使用 `/youtube get-member-only-playlist` 來確認該頻道是否有可驗證的影片");
                                needRemoveList.Add(item);
                                break;
                            }

                            var videoSnippet = videoList[new Random().Next(0, videoList.Count)];
                            var videoId = videoSnippet.Snippet.ResourceId.VideoId;
                            var ct = _streamService.YouTubeService.CommentThreads.List("snippet");
                            ct.VideoId = videoId;

                            try
                            {
                                _ = await ct.ExecuteAsync().ConfigureAwait(false);
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
                                    await SendMsgToLogChannelAsync(item.MemberCheckChannelId, $"新會限檢測影片 - ({item.MemberCheckChannelId}): {videoId}", false, false);

                                    foreach (var item2 in db.GuildYoutubeMemberConfig.Where((x) => x.MemberCheckChannelId == item.MemberCheckChannelId))
                                    {
                                        item2.MemberCheckVideoId = videoId;
                                        db.GuildYoutubeMemberConfig.Update(item2);
                                    }

                                    isCheck = true;
                                }
                                else
                                {
                                    Log.Error(ex, $"{item.MemberCheckChannelId} 新會限影片檢查錯誤");

                                    foreach (var item2 in db.GuildYoutubeMemberConfig.Where((x) => x.MemberCheckChannelId == item.MemberCheckChannelId))
                                    {
                                        item2.MemberCheckVideoId = "";
                                        db.GuildYoutubeMemberConfig.Update(item2);
                                    }

                                    isCheck = true;
                                }
                            }
                        } while (!isCheck);
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.ToLower().Contains("playlistid"))
                        {
                            Log.Warn($"CheckMemberShipOnlyVideoId: {item.GuildId} / {item.MemberCheckChannelId} 無會限影片可供檢測");
                            await SendMsgToLogChannelAsync(item.MemberCheckChannelId, $"{item.MemberCheckChannelId} 無會限影片，請等待該頻道主有新的會限影片且可留言時再使用會限驗證功能\n" +
                                $"你可以使用 `/youtube get-member-only-playlist` 來確認該頻道是否有可驗證的影片");
                            needRemoveList.Add(item);
                            continue;
                        }
                        else Log.Warn($"CheckMemberShipOnlyVideoId: {item.GuildId} / {item.MemberCheckChannelId}\n{ex}");
                    }

                    try
                    {
                        var c = _streamService.YouTubeService.Channels.List("snippet");
                        c.Id = item.MemberCheckChannelId;
                        var channelResult = await c.ExecuteAsync();
                        var channel = channelResult.Items.First();

                        Log.Info($"會限頻道名稱已變更 - ({item.MemberCheckChannelId}): `" + (string.IsNullOrEmpty(item.MemberCheckChannelTitle) ? "無" : item.MemberCheckChannelTitle) + $"` -> `{channel.Snippet.Title}`");
                        await SendMsgToLogChannelAsync(item.MemberCheckChannelId, $"會限頻道名稱已變更: `" + (string.IsNullOrEmpty(item.MemberCheckChannelTitle) ? "無" : item.MemberCheckChannelTitle) + $"` -> `{channel.Snippet.Title}`", false, false);

                        foreach (var item2 in db.GuildYoutubeMemberConfig.Where((x) => x.MemberCheckChannelId == item.MemberCheckChannelId))
                        {
                            item2.MemberCheckChannelTitle = channel.Snippet.Title;
                            db.GuildYoutubeMemberConfig.Update(item2);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"CheckMemberShipOnlyChannelName: {item.GuildId} / {item.MemberCheckChannelId}\n{ex}");
                    }
                }

                try
                {
                    db.GuildYoutubeMemberConfig.RemoveRange(needRemoveList);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"CheckMemberShipOnlyVideoId-RemoveRange");
                    await (await Bot.ApplicatonOwner.CreateDMChannelAsync()).SendErrorMessageAsync($"CheckMemberShipOnlyVideoId-RemoveRange: {ex}");
                }

                try
                {
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
                            foreach (var item2 in ex.Entries)
                            {
                                try
                                {
                                    item2.Reload();
                                }
                                catch (Exception ex2)
                                {
                                    Log.Error($"VideoContext-SaveChanges-Reload");
                                    Log.Error(item2.DebugView.ToString());
                                    Log.Error(ex2.ToString());
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"VideoContext-SaveChanges: {ex}");
                            Log.Error(db.ChangeTracker.DebugView.LongView);
                        }
                    } while (saveFailed && DateTime.Now.Subtract(saveTime) <= TimeSpan.FromMinutes(1));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"CheckMemberShipOnlyVideoId-SaveChanges");
                    await (await Bot.ApplicatonOwner.CreateDMChannelAsync()).SendErrorMessageAsync($"CheckMemberShipOnlyVideoId-SaveChanges: {ex}");
                }
            }

            //Log.Info("檢查新會限影片完成");
        }
    }
}