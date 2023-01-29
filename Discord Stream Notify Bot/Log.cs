using Discord;
using System;
using System.IO;
using System.Threading.Tasks;

public static class Log
{
    enum LogType { Verb, Stream, Info, Warn, Error }
    static string logPath = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".log";
    static string errorLogPath = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + "_err.log";
    static string streamLogPath = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + "_stream.log";
    private static object lockObj = new object();

    private static void WriteLogToFile(LogType type, string text)
    {
        lock (lockObj)
        {
            text = $"[{DateTime.Now}] [{type.ToString().ToUpper()}] | {text}\r\n";
            switch (type)
            {
                case LogType.Error:
                    File.AppendAllText(errorLogPath, text);
                    break;
                case LogType.Stream:
                    File.AppendAllText(streamLogPath, text);
                    break;
                default:
                    File.AppendAllText(logPath, text);
                    break;
            }
        }
    }

    public static void Stream(string text, bool newLine = true)
    {
        FormatColorWrite(text, ConsoleColor.Green, newLine);
        WriteLogToFile(LogType.Stream, text);
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
            FormatColorWrite(message.Exception.GetType().FullName, consoleColor);
            FormatColorWrite(message.Exception.Message, consoleColor);
            FormatColorWrite(message.Exception.StackTrace, consoleColor);
        }

        return Task.CompletedTask;
    }
}