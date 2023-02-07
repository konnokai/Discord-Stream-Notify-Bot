using System.IO;

public static class Log
{
    enum LogType { Verb, Stream, Info, Warn, Error }
    static string logPath = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".log";
    static string errorLogPath = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + "_err.log";
    static string streamLogPath = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + "_stream.log";
    private static readonly object writeLockObj = new();
    private static readonly object logLockObj = new();

    private static void WriteLogToFile(LogType type, string text)
    {
        lock (writeLockObj)
        {
            text = $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}] [{type.ToString().ToUpper()}] | {text}\r\n";
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

    public static void New(string text, bool newLine = true)
    {
        lock (logLockObj)
        {
            FormatColorWrite(text, ConsoleColor.Green, newLine);
            WriteLogToFile(LogType.Stream, text);
        }
    }

    public static void Info(string text, bool newLine = true)
    {
        lock (logLockObj)
        {
            FormatColorWrite(text, ConsoleColor.DarkYellow, newLine);
            WriteLogToFile(LogType.Info, text);
        }
    }

    public static void Warn(string text, bool newLine = true)
    {
        lock (logLockObj)
        {
            FormatColorWrite(text, ConsoleColor.DarkMagenta, newLine);
            WriteLogToFile(LogType.Warn, text);
        }
    }

    public static void Error(string text, bool newLine = true)
    {
        lock (logLockObj)
        {
            FormatColorWrite(text, ConsoleColor.DarkRed, newLine);
            WriteLogToFile(LogType.Error, text);
        }
    }

    public static void Error(Exception ex, string text, bool newLine = true)
    {
        lock (logLockObj)
        {
            FormatColorWrite(text, ConsoleColor.DarkRed, newLine);
            FormatColorWrite(ex.ToString(), ConsoleColor.DarkRed, newLine);
        }
    }

    public static void FormatColorWrite(string text, ConsoleColor consoleColor = ConsoleColor.Gray, bool newLine = true)
    {
        text = $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}] {text}";
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

        if (message.Exception != null && message.Exception is not Discord.WebSocket.GatewayReconnectException)
        {
            consoleColor = ConsoleColor.DarkRed;
            FormatColorWrite(message.Exception.GetType().FullName, consoleColor);
            FormatColorWrite(message.Exception.Message, consoleColor);
            FormatColorWrite(message.Exception.StackTrace, consoleColor);
        }

        return Task.CompletedTask;
    }
}