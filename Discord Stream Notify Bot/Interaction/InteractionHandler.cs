using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Discord_Stream_Notify_Bot.Interaction
{
    class InteractionHandler : IInteractionService
    {
        private readonly DiscordSocketClient _client;
        private readonly InteractionService _interactions;
        private readonly IServiceProvider _services;

        public int CommandCount => _interactions.ComponentCommands.Count + _interactions.ContextCommands.Count + _interactions.SlashCommands.Count;

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

            #region 檢查指令是否符合Discord的Regex規範
#if DEBUG
            bool isError = false;
            Regex regex = new Regex(@"^[\w-]{1,32}$");
            var list = _interactions.Modules.Select(module => module.SlashCommands.Select((x) => new KeyValuePair<string, List<string>>(x.Name, x.Parameters.Select((x2) => x2.Name).ToList())));
            foreach (var item in list)
            {
                foreach (var item2 in item)
                {
                    if (!regex.IsMatch(item2.Key))
                        Log.Error(item2.Key);
                    foreach (var item3 in item2.Value)
                    {
                        if (!regex.IsMatch(item3))
                        {
                            isError = true;
                            Log.Error($"{item2.Key}: {item3}");
                        }
                    }
                }
            }
            if (isError) return;
#endif
            #endregion

            _client.InteractionCreated += (slash) => { var _ = Task.Run(() => HandleInteraction(slash)); return Task.CompletedTask; };
            _interactions.SlashCommandExecuted += SlashCommandExecuted;
        }

        private async Task HandleInteraction(SocketInteraction arg)
        {
            try
            {
                var ctx = new SocketInteractionContext(_client, arg);
                await _interactions.ExecuteCommandAsync(ctx, _services);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                // If a Slash Command execution fails it is most likely that the original interaction acknowledgement will persist. It is a good idea to delete the original
                // response, or at least let the user know that something went wrong during the command execution.
                if (arg.Type == InteractionType.ApplicationCommand)
                    await arg.GetOriginalResponseAsync().ContinueWith(async (msg) => await msg.Result.DeleteAsync());
            }
        }

        private Task SlashCommandExecuted(SlashCommandInfo arg1, IInteractionContext arg2, IResult arg3)
        {
            string slashCommand = $"/{arg1}";
            var commandData = arg2.Interaction.Data as SocketSlashCommandData;
            if (commandData.Options.Count > 0) slashCommand += GetOptionsValue(commandData.Options.First());

            if (arg3.IsSuccess)
            {
                Log.FormatColorWrite($"[{arg2.Guild.Name}/{arg2.Channel.Name}] {arg2.User.Username} 執行 `{slashCommand}`", ConsoleColor.DarkYellow);
            }
            else
            {
                Log.FormatColorWrite($"[{arg2.Guild.Name}/{arg2.Channel.Name}] {arg2.User.Username} 執行 `{slashCommand}` 發生錯誤\n{arg3.ErrorReason}", ConsoleColor.Red);
                switch (arg3.Error)
                {
                    case InteractionCommandError.UnmetPrecondition:
                        arg2.Interaction.SendErrorAsync(arg3.ErrorReason);
                        break;
                    case InteractionCommandError.UnknownCommand:
                        arg2.Interaction.SendErrorAsync("未知的指令，也許被移除或變更了");
                        break;
                    case InteractionCommandError.BadArgs:
                        arg2.Interaction.SendErrorAsync("輸入的參數錯誤");
                        break;
                    default:
                        arg2.Interaction.SendErrorAsync("未知的錯誤，請向Bot擁有者回報");
                        break;
                }
            }

            return Task.CompletedTask;
        }

        private string GetOptionsValue(SocketSlashCommandDataOption socketSlashCommandDataOption)
        {
            try
            {
                if (socketSlashCommandDataOption.Type == ApplicationCommandOptionType.SubCommand || socketSlashCommandDataOption.Type == ApplicationCommandOptionType.SubCommandGroup) GetOptionsValue(socketSlashCommandDataOption.Options.First());
                return " " + string.Join(' ', socketSlashCommandDataOption.Options.Select(option => option.Value));
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
