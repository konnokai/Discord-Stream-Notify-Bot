using System;


namespace Discord_Stream_Notify_Bot.Command.Attribute
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
    sealed class CommandExampleAttribute : System.Attribute
    {
        // See the attribute guidelines at 
        //  http://go.microsoft.com/fwlink/?LinkId=85236
        readonly string[] expArray;

        // This is a positional argument
        public CommandExampleAttribute(string[] expArray)
        {
            this.expArray = expArray;
        }

        public string[] ExpArray
        {
            get { return expArray; }
        }
    }
}
