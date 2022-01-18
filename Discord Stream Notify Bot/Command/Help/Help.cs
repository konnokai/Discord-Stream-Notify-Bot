using Discord;
using Discord.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Discord_Stream_Notify_Bot.Command.Help
{
    public class Help : TopLevelModule<HelpService>
    {
        private readonly CommandService _cmds;
        private readonly IServiceProvider _services;

        public const string PatreonUrl = "https://patreon.com/jun112561";
        public const string PaypalUrl = "https://paypal.me/jun112561";
        public Help(CommandService cmds, IServiceProvider service)
        {
            _cmds = cmds;
            _services = service;
        }

        [Command("Modules")]
        [Summary("顯示模組")]
        public async Task Modules()
        {
            await ReplyAsync("", false, new EmbedBuilder().WithOkColor().WithTitle("模組清單")
                .WithDescription(string.Join("\n", _cmds.Modules.Select((x) => "。" + x.Name)))
                .WithFooter("輸入 `s!Commands 模組名稱` 以顯示模組內全部的指令，例 `s!Commands Help`")
                .Build());
        }

        [Command("Commands")]
        [Summary("顯示模組內包含的指令")]
        [Alias("Cmds")]
        public async Task Commands([Summary("模組名稱")]string module = null)
        {
            module = module?.Trim();
            if (string.IsNullOrWhiteSpace(module)) return;

            var cmds = _cmds.Commands.Where(c => c.Module.Name.ToUpperInvariant().StartsWith(module.ToUpperInvariant(), StringComparison.InvariantCulture)).OrderBy(c => c.Aliases[0]).Distinct(new CommandTextEqualityComparer());
            if (cmds.Count() == 0) { await Context.Channel.SendConfirmAsync($"找不到 {module} 模組"); return; }

            var succ = new HashSet<CommandInfo>((await Task.WhenAll(cmds.Select(async x =>
            {
                var pre = (await x.CheckPreconditionsAsync(Context, _services).ConfigureAwait(false));
                return (Cmd: x, Succ: pre.IsSuccess);
            })).ConfigureAwait(false))
                .Where(x => x.Succ)
                .Select(x => x.Cmd));
            cmds = cmds.Where(x => succ.Contains(x));

            if (cmds.Count() == 0)
            {
                await Context.Channel.SendConfirmAsync(module + " 未包含你可使用的指令");
                return;
            }

            var embed = new EmbedBuilder().WithOkColor().WithTitle($"{cmds.First().Module.Name} 內包含的指令").WithFooter("輸入 `s!Help 指令` 以顯示指令的詳細說明，例 `s!Help Help`");
            var commandList = new List<string>();

            foreach (var item in cmds)
            {
                var prefix = "s!";

                var str = string.Format("**`{0}`**", prefix + item.Aliases.First());
                var alias = item.Aliases.Skip(1).FirstOrDefault();
                if (alias != null)
                    str += string.Format(" **/ `{0}`**", prefix + alias);

                if (!commandList.Contains(str)) commandList.Add(str);
            }
            embed.WithDescription(string.Join('\n', commandList));

            await ReplyAsync("", false, embed.Build());
        }

        [Command("Help")]
        [Summary("顯示指令的詳細說明")]
        [Alias("H")]
        public async Task H([Summary("指令名稱")]string command = null)
        {
            command = command?.Trim();

            if (string.IsNullOrWhiteSpace(command))
            {
                EmbedBuilder embed = new EmbedBuilder().WithOkColor().WithFooter("輸入 `s!Modules` 取得所有的模組");
                embed.Title = "直播小幫手 建置版本" + Program.VERSION;
#if DEBUG
                embed.Title += " (測試版)";
#endif
                embed.WithDescription(System.IO.File.ReadAllText(Program.GetDataFilePath("HelpDescription.txt")).Replace("\\n","\n") + $"\n\n您可以透過：\nPatreon <{PatreonUrl}> \nPaypal <{PaypalUrl}>\n來贊助直播小幫手");
                await ReplyAsync("", false, embed.Build());
                return;
            }

            CommandInfo commandInfo = _cmds.Commands.FirstOrDefault((x) => x.Aliases.Any((x2) => x2.ToLowerInvariant() == command.ToLowerInvariant()));
            if (commandInfo == null) { await Context.Channel.SendConfirmAsync($"找不到 {command} 指令"); return; }
             
            await ReplyAsync("", false, _service.GetCommandHelp(commandInfo).Build());
        }
    }

    public class CommandTextEqualityComparer : IEqualityComparer<CommandInfo>
    {
        public bool Equals(CommandInfo x, CommandInfo y) => x.Aliases[0] == y.Aliases[0];

        public int GetHashCode(CommandInfo obj) => obj.Aliases[0].GetHashCode(StringComparison.InvariantCulture);

    }
}
