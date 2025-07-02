﻿namespace DiscordStreamNotifyBot.Interaction.Attribute
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    sealed class CommandExampleAttribute : System.Attribute
    {
        readonly string[] expArray;

        public CommandExampleAttribute(params string[] expArray)
        {
            this.expArray = expArray;
        }

        public string[] ExpArray
        {
            get { return expArray; }
        }
    }
}
