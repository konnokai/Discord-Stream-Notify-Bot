using Discord.Commands;
using System.Reflection;

namespace Discord_Stream_Notify_Bot.Command
{
    class CommandHandler : ICommandService
    {
        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;
        private readonly IServiceProvider _services;

        public CommandHandler(IServiceProvider services, CommandService commands, DiscordSocketClient client)
        {
            _commands = commands;
            _services = services;
            _client = client;
        }

        public async Task InitializeAsync()
        {
            await _commands.AddModulesAsync(
                assembly: Assembly.GetEntryAssembly(),
                services: _services);
            _client.MessageReceived += (msg) => { var _ = Task.Run(() => HandleCommandAsync(msg)); return Task.CompletedTask; };
        }

        private async Task HandleCommandAsync(SocketMessage messageParam)
        {
            var message = messageParam as SocketUserMessage;
            if (message == null || message.Author.IsBot) return;

            int argPos = 0;
            if (message.HasStringPrefix("s!", ref argPos))
            {
                var context = new SocketCommandContext(_client, message);

                if (_commands.Search(context, argPos).IsSuccess)
                {
                    var result = await _commands.ExecuteAsync(
                        context: context,
                        argPos: argPos,
                        services: _services);

                    if (!result.IsSuccess)
                    {
                        Log.FormatColorWrite($"[{context.Guild?.Name}/{context.Message.Channel?.Name}] {message.Author.Username} 執行 {context.Message} 發生錯誤", ConsoleColor.Red);
                        Log.FormatColorWrite(result.ErrorReason, ConsoleColor.Red);
                        await context.Channel.SendErrorAsync(result.ErrorReason);
                    }
                    else
                    {
                        try { if (context.Message.Author.Id == Program.ApplicatonOwner.Id || context.Message.CleanContent == "s!ymlc") await message.DeleteAsync(); }
                        catch { }
                        Log.FormatColorWrite($"[{context.Guild?.Name}/{context.Message.Channel?.Name}] {message.Author.Username} 執行 {context.Message}", ConsoleColor.DarkYellow);
                    }
                }
            }
        }
    }
}
