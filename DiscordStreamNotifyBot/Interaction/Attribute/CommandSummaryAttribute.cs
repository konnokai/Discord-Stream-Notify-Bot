namespace DiscordStreamNotifyBot.Interaction.Attribute
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    sealed class CommandSummaryAttribute : System.Attribute
    {
        readonly string summary;

        public CommandSummaryAttribute(string summary)
        {
            this.summary = summary;
        }

        public string Summary
        {
            get { return summary; }
        }
    }
}
