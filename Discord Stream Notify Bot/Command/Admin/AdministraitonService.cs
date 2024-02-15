namespace Discord_Stream_Notify_Bot.Command.Admin
{
    public class AdministrationService : ICommandService
    {
        private readonly DiscordSocketClient _Client;

        public AdministrationService(DiscordSocketClient client)
        {
            _Client = client;
        }

        public async Task ClearUser(ITextChannel textChannel)
        {
            IEnumerable<IMessage> msgs = (await textChannel.GetMessagesAsync(100).FlattenAsync().ConfigureAwait(false))
                  .Where((item) => item.Author.Id == _Client.CurrentUser.Id);

            await Task.WhenAll(Task.Delay(1000), textChannel.DeleteMessagesAsync(msgs)).ConfigureAwait(false);
        }
    }
}
