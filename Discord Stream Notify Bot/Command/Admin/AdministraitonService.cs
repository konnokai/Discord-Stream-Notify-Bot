namespace Discord_Stream_Notify_Bot.Command.Admin
{
    public class AdministrationService : ICommandService
    {
        private string _reloadOfficialGuildListKey = "DiscordStreamBot:Admin:ReloadOfficialGuildList";
        private readonly DiscordSocketClient _Client;

        public AdministrationService(DiscordSocketClient client)
        {
            _Client = client;

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
                  .Where((item) => item.Author.Id == _Client.CurrentUser.Id);

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
    }
}
