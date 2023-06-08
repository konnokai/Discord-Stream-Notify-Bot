using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace Discord_Stream_Notify_Bot.Interaction.Help
{
    [Group("help", "說明")]
    public class Help : TopLevelModule<Service.HelpService>
    {
        private readonly InteractionService _interaction;
        private readonly IServiceProvider _services;

        public Help(InteractionService interaction, IServiceProvider service)
        {
            _interaction = interaction;
            _services = service;
        }

        public class HelpGetModulesAutocompleteHandler : AutocompleteHandler
        {
            public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
            {
                List<AutocompleteResult> results = new();
                var succ = new HashSet<SlashCommandInfo>((await Task.WhenAll(services.GetService<InteractionService>().SlashCommands.Select(async x =>
                {
                    var pre = await x.CheckPreconditionsAsync(context, services).ConfigureAwait(false);
                    return (Cmd: x, Succ: pre.IsSuccess);
                })).ConfigureAwait(false))
                   .Where(x => x.Succ)
                   .Select(x => x.Cmd));

                try
                {
                    foreach (var item in succ.GroupBy((x) => x.Module.Name))
                    {
                        var module = item.First().Module;
                        results.Add(new AutocompleteResult((string.IsNullOrWhiteSpace(module.Description) ? "" : module.Description + " ") + $"({module.Name})", module.Name));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("HelpGetModulesAutocompleteHandler");
                    Log.Error(ex.ToString());
                }

                return AutocompletionResult.FromSuccess(results.Take(25));
            }
        }

        [SlashCommand("get-all-modules", "顯示全部模組")]
        public async Task Modules()
        {
            var succ = new HashSet<SlashCommandInfo>((await Task.WhenAll(_interaction.SlashCommands.Select(async x =>
            {
                var pre = await x.CheckPreconditionsAsync(Context, _services).ConfigureAwait(false);
                return (Cmd: x, Succ: pre.IsSuccess);
            })).ConfigureAwait(false))
               .Where(x => x.Succ)
               .Select(x => x.Cmd));

            await RespondAsync(embed: new EmbedBuilder().WithOkColor().WithTitle("模組清單")
                .WithDescription(string.Join("\n", succ.GroupBy((x) => x.Module.Name).Select((x) => "。" + x.Key)))
                .WithFooter("輸入 `/help getallcommands 模組名稱` 以顯示模組內全部的指令，例 `/help get-all-commands help`")
                .Build());
        }

        [SlashCommand("get-all-commands", "顯示模組內包含的指令")]
        public async Task Commands([Summary("模組名稱"), Autocomplete(typeof(HelpGetModulesAutocompleteHandler))] string module)
        {
            module = module?.Trim();
            if (string.IsNullOrWhiteSpace(module))
            {
                await Context.Interaction.SendErrorAsync("未輸入模組名稱");
                return;
            }

            var cmds = _interaction.SlashCommands.Where(c => c.Module.Name.ToUpperInvariant() == module.ToUpperInvariant()).OrderBy(c => c.Name).Distinct(new CommandTextEqualityComparer());
            if (cmds.Count() == 0) { await Context.Interaction.SendErrorAsync($"找不到 {module} 模組", ephemeral: true); return; }

            var succ = new HashSet<SlashCommandInfo>((await Task.WhenAll(cmds.Select(async x =>
            {
                var pre = await x.CheckPreconditionsAsync(Context, _services).ConfigureAwait(false);
                return (Cmd: x, Succ: pre.IsSuccess);
            })).ConfigureAwait(false))
                .Where(x => x.Succ)
                .Select(x => x.Cmd));
            cmds = cmds.Where(x => succ.Contains(x));

            if (cmds.Count() == 0)
            {
                await Context.Interaction.SendErrorAsync(module + " 未包含你可使用的指令");
                return;
            }

            var embed = new EmbedBuilder().WithOkColor().WithTitle($"{cmds.First().Module.Name} 內包含的指令").WithFooter("輸入 `/help get-command-help 指令` 以顯示指令的詳細說明，例 `/help get-command-help add-youtube-notice`");
            var commandList = new List<string>();

            foreach (var item in cmds)
            {
                var str = string.Format($"**`/{cmds.First().Module.SlashGroupName} {item.Name}`**");
                if (!commandList.Contains(str)) commandList.Add(str);
            }
            embed.WithDescription(string.Join('\n', commandList));

            await RespondAsync(embed: embed.Build());
        }

        [SlashCommand("get-command-help", "顯示指令的詳細說明")]
        public async Task H([Summary("模組名稱"), Autocomplete(typeof(HelpGetModulesAutocompleteHandler))] string module = "", [Summary("指令名稱")] string command = "")
        {
            command = command?.Trim();

            if (string.IsNullOrWhiteSpace(module))
            {
                EmbedBuilder embed = new EmbedBuilder().WithOkColor().WithFooter("輸入 `/help get-all-modules` 取得所有的模組");
                embed.Title = "直播小幫手 建置版本" + Program.VERSION;
#if DEBUG || DEBUG_DONTREGISTERCOMMAND
                embed.Title += " (測試版)";
#endif
                embed.WithDescription(System.IO.File.ReadAllText(Program.GetDataFilePath("HelpDescription.txt")).Replace("\\n", "\n") +
                    $"\n\n您可以透過 {Format.Url("Patreon", Discord_Stream_Notify_Bot.Utility.PatreonUrl)} 或 {Format.Url("Paypal", Discord_Stream_Notify_Bot.Utility.PaypalUrl)} 來贊助直播小幫手");
                await RespondAsync(embed: embed.Build());
                return;
            }

            var cmds = _interaction.SlashCommands.Where(c => c.Module.Name.ToUpperInvariant() == module.ToUpperInvariant()).OrderBy(c => c.Name).Distinct(new CommandTextEqualityComparer());
            if (cmds.Count() == 0)
            {
                await Context.Interaction.SendErrorAsync($"找不到 {module} 模組\n輸入 `/help get-all-modules` 取得所有的模組", ephemeral: true);
                return;
            }

            SlashCommandInfo commandInfo = cmds.FirstOrDefault((x) => x.Name == command.ToLowerInvariant());
            if (commandInfo == null)
            {
                await Context.Interaction.SendErrorAsync($"找不到 {command} 指令");
                return;
            }

            await RespondAsync(embed: _service.GetCommandHelp(commandInfo).Build());
        }
    }

    public class CommandTextEqualityComparer : IEqualityComparer<SlashCommandInfo>
    {
        public bool Equals(SlashCommandInfo x, SlashCommandInfo y) => x.Name == y.Name;

        public int GetHashCode(SlashCommandInfo obj) => obj.Name.GetHashCode(StringComparison.InvariantCulture);
    }
}
