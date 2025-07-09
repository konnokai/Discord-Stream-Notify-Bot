using DiscordStreamNotifyBot.DataBase;
using TwitchLib.Communication.Interfaces;

namespace DiscordStreamNotifyBot.Command.Admin
{
    public class AdministrationService : ICommandService
    {
        private string _reloadOfficialGuildListKey = "DiscordStreamBot:Admin:ReloadOfficialGuildList";
        private readonly DiscordSocketClient _client;
        private readonly MainDbService _dbService;

        public AdministrationService(DiscordSocketClient client, MainDbService service)
        {
            _client = client;
            _dbService = service;

            Bot.RedisSub.Subscribe(new RedisChannel(_reloadOfficialGuildListKey, RedisChannel.PatternMode.Literal), (_, _) =>
            {
                try
                {
                    Utility.OfficialGuildList = JsonConvert.DeserializeObject<HashSet<ulong>>(File.ReadAllText(Utility.GetDataFilePath("OfficialList.json")));
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Demystify(), "ReloadOfficialGuildList Error");
                }
            });
        }

        public async Task ClearUser(ITextChannel textChannel)
        {
            IEnumerable<IMessage> msgs = (await textChannel.GetMessagesAsync(100).FlattenAsync().ConfigureAwait(false))
                  .Where((item) => item.Author.Id == _client.CurrentUser.Id);

            await Task.WhenAll(Task.Delay(1000), textChannel.DeleteMessagesAsync(msgs)).ConfigureAwait(false);
        }

        internal bool WriteAndReloadOfficialListFile()
        {
            try
            {
                File.WriteAllText(Utility.GetDataFilePath("OfficialList.json"), JsonConvert.SerializeObject(Utility.OfficialGuildList));
                Bot.RedisSub.Publish(new RedisChannel(_reloadOfficialGuildListKey, RedisChannel.PatternMode.Literal), "");
            }
            catch (Exception ex)
            {
                Log.Error(ex.Demystify(), "WriteOfficialListFile Error");
                return false;
            }

            return true;
        }

        internal IReadOnlyCollection<SocketGuild> GetNoNotifyGuilds()
        {
            var guilds = new List<SocketGuild>(_client.Guilds);
            using var db = _dbService.GetDbContext();

            db.NoticeYoutubeStreamChannel
                .AsEnumerable()
                .DistinctBy((x) => x.GuildId)
                .Select((x) => x.GuildId)
                .ToList()
                .ForEach((x) =>
                {
                    var guild = guilds.SingleOrDefault((x2) => x2.Id == x);
                    if (guild != null)
                        guilds.Remove(guild);
                });

            db.NoticeTwitchStreamChannels
                .AsEnumerable()
                .DistinctBy((x) => x.GuildId)
                .Select((x) => x.GuildId)
                .ToList()
                .ForEach((x) =>
                {
                    var guild = guilds.SingleOrDefault((x2) => x2.Id == x);
                    if (guild != null)
                        guilds.Remove(guild);
                });

            db.NoticeTwitterSpaceChannel
                .AsEnumerable()
                .DistinctBy((x) => x.GuildId)
                .Select((x) => x.GuildId)
                .ToList()
                .ForEach((x) =>
                {
                    var guild = guilds.SingleOrDefault((x2) => x2.Id == x);
                    if (guild != null)
                        guilds.Remove(guild);
                });

            db.NoticeTwitcastingStreamChannels
                .AsEnumerable()
                .DistinctBy((x) => x.GuildId)
                .Select((x) => x.GuildId)
                .ToList()
                .ForEach((x) =>
                {
                    var guild = guilds.SingleOrDefault((x2) => x2.Id == x);
                    if (guild != null)
                        guilds.Remove(guild);
                });

            db.GuildYoutubeMemberConfig
                .AsEnumerable()
                .DistinctBy((x) => x.GuildId)
                .Select((x) => x.GuildId)
                .ToList()
                .ForEach((x) =>
                {
                    var guild = guilds.SingleOrDefault((x2) => x2.Id == x);
                    if (guild != null)
                        guilds.Remove(guild);
                });

            Utility.OfficialGuildList
                .ToList()
                .ForEach((x) =>
                {
                    var guild = guilds.SingleOrDefault((x2) => x2.Id == x);
                    if (guild != null)
                        guilds.Remove(guild);
                });

            guilds = guilds
                .OrderByDescending((x) => x.MemberCount)
                .ToList();

            return guilds.AsReadOnly();
        }
    }
}
