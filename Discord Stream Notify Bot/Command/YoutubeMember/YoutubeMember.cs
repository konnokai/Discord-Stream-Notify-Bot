using Discord;
using Discord.Commands;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Command.YoutubeMember
{
    public class YoutubeMember : TopLevelModule, ICommandService
    {
        [Command("ListAllGuildCheckedMember")]
        [Summary("顯示所有伺服器已完成驗證的會員數量")]
        [Alias("lagcm")]
        [RequireContext(ContextType.DM)]
        [RequireOwner]
        public async Task ListAllGuildCheckedMemberAsync()
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

                await Context.SendPaginatedConfirmAsync(0, (page) =>
                {
                    return new EmbedBuilder().WithOkColor().WithDescription(string.Join('\n', dic
                        .Skip(page * 7).Take(7).Select((x) =>
                            $"**{x.Key}**:\n" +
                            $"{string.Join('\n', x.Value)}\n"
                    )));
                }, dic.Count(), 7);
            }
        }
    }
}
