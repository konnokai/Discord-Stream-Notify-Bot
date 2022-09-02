using Discord;
using System;
using System.IO;
using System.Threading.Tasks;

public static class Log
{
    enum LogType { Verb, NewS, Info, Warn, Error }
    static string logPath = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + "_log.txt";

    private static void WriteLogToFile(LogType type, string text)
    {
        text = $"[{DateTime.Now}] [{type.ToString().ToUpper()}] | {text}\r\n";
        File.AppendAllText(logPath, text);
    }

    public static void NewStream(string text, bool newLine = true)
    {
        FormatColorWrite(text, ConsoleColor.Green, newLine);
        WriteLogToFile(LogType.NewS, text);
    }

    public static void Info(string text, bool newLine = true)
    {
        FormatColorWrite(text, ConsoleColor.DarkYellow, newLine);
        WriteLogToFile(LogType.Info, text);
    }

    public static void Warn(string text, bool newLine = true)
    {
        FormatColorWrite(text, ConsoleColor.DarkMagenta, newLine);
        WriteLogToFile(LogType.Warn, text);
    }

    public static void Error(string text, bool newLine = true)
    {
        FormatColorWrite(text, ConsoleColor.DarkRed, newLine);
        WriteLogToFile(LogType.Error, text);
    }

    public static void FormatColorWrite(string text, ConsoleColor consoleColor = ConsoleColor.Gray, bool newLine = true)
    {
        text = $"[{DateTime.Now}] {text}";
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

#if DEBUG
        if (!string.IsNullOrEmpty(message.Message)) FormatColorWrite(message.Message, consoleColor);
#else
        WriteLogToFile(LogType.Verb, message.Message);
#endif

        if (message.Exception != null)
        {
            consoleColor = ConsoleColor.DarkRed;
            FormatColorWrite(message.Exception.Message, consoleColor);
            FormatColorWrite(message.Exception.StackTrace, consoleColor);
        }

        return Task.CompletedTask;
    }
}