using Discord.Interactions;
using DiscordStreamNotifyBot.Interaction.Attribute;

namespace DiscordStreamNotifyBot.Interaction.Help.Service
{
    public class HelpService : IInteractionService
    {
        public EmbedBuilder GetCommandHelp(SlashCommandInfo com)
        {
            var str = string.Format($"**`/{com.Name}`**");
            var des = com.Description;
            if (com.Attributes.Any((x) => x is CommandSummaryAttribute))
            {
                var att = com.Attributes.FirstOrDefault((x) => x is CommandSummaryAttribute) as CommandSummaryAttribute;
                des = att.Summary;
            }
            var em = new EmbedBuilder().WithTitle(com.Name).WithDescription(des);

            if (com.Parameters.Count > 0)
            {
                string par = "";
                foreach (var item in com.Parameters)
                    par += item.Name + " " + item.Description + "\n";
                em.AddField("參數", par.TrimEnd('\n'));
            }

            var reqs = GetCommandRequirements(com);
            if (reqs.Any()) em.AddField("指令執行者權限要求", string.Join("\n", reqs));

            var botReqs = GetBotCommandRequirements(com);
            if (botReqs.Any()) em.AddField("Bot權限要求", string.Join("\n", botReqs));

            var exp = GetCommandExampleString(com);
            if (!string.IsNullOrEmpty(exp)) em.AddField("例子", exp);

            em.WithFooter(efb => efb.WithText("模組: " + com.Module.Name))
              .WithOkColor();

            return em;
        }

        public static string[] GetCommandRequirements(SlashCommandInfo cmd) =>
            cmd.Preconditions
                  .Where(ca => ca is RequireOwnerAttribute || ca is RequireUserPermissionAttribute)
                  .Select(ca =>
                  {
                      if (ca is RequireOwnerAttribute)
                      {
                          return "Bot擁有者限定";
                      }

                      var cau = (RequireUserPermissionAttribute)ca;
                      if (cau.GuildPermission != null)
                      {
                          return ("伺服器 " + cau.GuildPermission.ToString() + " 權限")
                                       .Replace("Guild", "Server", StringComparison.InvariantCulture);
                      }

                      return ("頻道 " + cau.ChannelPermission + " 權限")
                                       .Replace("Guild", "Server", StringComparison.InvariantCulture);
                  })
                .ToArray();

        public static string[] GetBotCommandRequirements(SlashCommandInfo cmd) =>
            cmd.Preconditions
                  .Where(ca => ca is RequireBotPermissionAttribute)
                  .Select(ca =>
                  {
                      var cau = (RequireBotPermissionAttribute)ca;
                      if (cau.GuildPermission != null)
                      {
                          return ("伺服器 " + cau.GuildPermission.ToString() + " 權限")
                                       .Replace("Guild", "Server", StringComparison.InvariantCulture);
                      }

                      return ("頻道 " + cau.ChannelPermission + " 權限")
                                       .Replace("Guild", "Server", StringComparison.InvariantCulture);
                  })
                .ToArray();

        public static string GetCommandExampleString(SlashCommandInfo cmd)
        {
            var att = cmd.Attributes.FirstOrDefault((x) => x is CommandExampleAttribute);
            if (att == null) return "";

            var commandExampleAttribute = att as CommandExampleAttribute;

            return string.Join("\n", commandExampleAttribute.ExpArray
                .Select((x) => $"`/{cmd.Module.SlashGroupName} {cmd.Name} {x}`")
                .ToArray());
        }
    }
}