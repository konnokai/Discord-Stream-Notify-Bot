using Discord.Commands;

namespace Discord_Stream_Notify_Bot.Command.YoutubeMember
{
    public class YoutubeMember : TopLevelModule, ICommandService
    {
        private readonly SharedService.Youtube.YoutubeStreamService _service;

        public YoutubeMember(SharedService.Youtube.YoutubeStreamService service)
        {
            _service = service;
        }

        [Command("ListAllGuildCheckedMember")]
        [Summary("顯示所有伺服器已完成驗證的會員數量")]
        [Alias("lagcm")]
        [RequireContext(ContextType.DM)]
        [RequireOwner]
        public async Task ListAllGuildCheckedMemberAsync(int page = 0)
        {
            using (var db = DataBase.DBContext.GetDbContext())
            {
                var guildYoutubeMemberConfigs = db.GuildYoutubeMemberConfig.Where((x) => !string.IsNullOrEmpty(x.MemberCheckChannelTitle) && x.MemberCheckVideoId != "-");
                if (!guildYoutubeMemberConfigs.Any())
                {
                    await Context.Channel.SendErrorAsync($"清單為空");
                    return;
                }

                var dic = new Dictionary<string, List<string>>();
                foreach (var item in guildYoutubeMemberConfigs)
                {
                    var checkedMemberCount = db.YoutubeMemberCheck.Count((x) => x.GuildId == item.GuildId &&
                        x.CheckYTChannelId == item.MemberCheckChannelId && x.IsChecked);

                    if (checkedMemberCount == 0)
                        continue;

                    string guildName = (await Context.Client.GetGuildAsync(item.GuildId)).Name;
                    string formatStr = $"{Format.Url(item.MemberCheckChannelTitle, $"https://www.youtube.com/channel/{item.MemberCheckChannelId}")}: {checkedMemberCount}人";

                    if (dic.ContainsKey(guildName)) dic[guildName].Add(formatStr);
                    else dic.Add(guildName, new List<string>() { formatStr });
                }

                await Context.SendPaginatedConfirmAsync(page, (page) =>
                {
                    return new EmbedBuilder().WithOkColor().WithDescription(string.Join('\n', dic
                        .Skip(page * 7).Take(7).Select((x) =>
                            $"**{x.Key}**:\n" +
                            $"{string.Join('\n', x.Value)}\n"
                    )));
                }, dic.Count(), 7);
            }
        }

        [Command("SetMemberCheckVideoId")]
        [Summary("設定指定頻道的會限影片Id")]
        [Alias("smcvi")]
        [RequireContext(ContextType.DM)]
        [RequireOwner]
        public async Task SetMemberCheckVideoIdAsync(string channelId, string videoId)
        {
            try
            {
                channelId = await _service.GetChannelIdAsync(channelId).ConfigureAwait(false);
                videoId = _service.GetVideoId(videoId);
            }
            catch (FormatException fex)
            {
                await Context.Channel.SendErrorAsync(fex.Message);
                return;
            }
            catch (ArgumentNullException)
            {
                await Context.Channel.SendErrorAsync("網址不可空白");
                return;
            }

            try
            {
                using (var db = DataBase.DBContext.GetDbContext())
                {
                    var guildYoutubeMemberConfigs = db.GuildYoutubeMemberConfig.Where((x) => x.MemberCheckChannelId == channelId);
                    if (!guildYoutubeMemberConfigs.Any())
                    {
                        await Context.Channel.SendErrorAsync($"{channelId} 不存在資料");
                        return;
                    }

                    foreach (var guildYoutubeMemberConfig in guildYoutubeMemberConfigs)
                    {
                        guildYoutubeMemberConfig.MemberCheckVideoId = videoId;
                        db.GuildYoutubeMemberConfig.Update(guildYoutubeMemberConfig);
                    }

                    db.SaveChanges();
                    await Context.Channel.SendConfirmAsync($"已將 `{guildYoutubeMemberConfigs.First().MemberCheckChannelTitle}` 的會限檢測影片更改為 `{guildYoutubeMemberConfigs.First().MemberCheckVideoId}`");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
                await Context.Channel.SendErrorAsync(ex.Message);
            }
        }
    }
}
