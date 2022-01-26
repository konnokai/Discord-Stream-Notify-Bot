using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Interaction
{
    class InteractionHandler : IInteractionService
    {
        private readonly DiscordSocketClient _client;
        private readonly InteractionService _interactions;
        private readonly IServiceProvider _services;

        public InteractionHandler(IServiceProvider services, InteractionService interactions, DiscordSocketClient client)
        {
            _client = client;
            _interactions = interactions;
            _services = services;
        }

        public async Task InitializeAsync()
        {
            await _interactions.AddModulesAsync(
                assembly: Assembly.GetEntryAssembly(),
                services: _services);
            _client.SlashCommandExecuted += (slash) => { var _ = Task.Run(() => HandleCommandAsync(slash)); return Task.CompletedTask; };
        }

        private async Task HandleCommandAsync(SocketSlashCommand slashCommand)
        {
            //await _services.GetService<Normal.Normal>().PingAsync();
            //try { if (slashCommand.User.Id == Program.ApplicatonOwner.Id) await slashCommand.DeleteOriginalResponseAsync(); }
            //catch { }
            Log.FormatColorWrite($"[{slashCommand.Channel.Name}] {slashCommand.User.Username} 執行 {slashCommand.Data.Name}", ConsoleColor.DarkYellow);
        }
    }
}
