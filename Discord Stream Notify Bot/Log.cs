using Discord;
using System;
using System.Threading.Tasks;
public static class Log
{

    public static void NewStream(string text, bool newLine = true)
    {
        FormatColorWrite(text, ConsoleColor.Green, newLine);
    }

    public static void Info(string text, bool newLine = true)
    {
        FormatColorWrite(text, ConsoleColor.DarkYellow, newLine);
    }

    public static void Warn(string text, bool newLine = true)
    {
        FormatColorWrite(text, ConsoleColor.DarkMagenta, newLine);
    }

    public static void Error(string text, bool newLine = true)
    {
        FormatColorWrite(text, ConsoleColor.DarkRed, newLine);
    }

    public static void FormatColorWrite(string text, ConsoleColor consoleColor = ConsoleColor.Gray, bool newLine = true)
    {
        text = DateTime.Now.ToString() + " " + text;
        Console.ForegroundColor = consoleColor;
        if (newLine) Console.WriteLine(text);
        else Console.Write(text);
        Console.ForegroundColor = ConsoleColor.Gray;
    }

    public static Task LogMsg(LogMessage message)
    {
        ConsoleColor consoleColor = ConsoleColor.DarkCyan;

        switch (message.Severity)
        {
            case LogSeverity.Error:
                consoleColor = ConsoleColor.DarkRed;
                break;
            case LogSeverity.Warning:
                consoleColor = ConsoleColor.DarkMagenta;
                break;
            case LogSeverity.Debug:
                consoleColor = ConsoleColor.Green;
                break;
        }

        if (!string.IsNullOrEmpty(message.Message)) FormatColorWrite(message.Message, consoleColor);

        if (message.Exception != null)
        {
            consoleColor = ConsoleColor.DarkRed;
            FormatColorWrite(message.Exception.Message, consoleColor);
            FormatColorWrite(message.Exception.StackTrace, consoleColor);
        }

        return Task.CompletedTask;
    }
}