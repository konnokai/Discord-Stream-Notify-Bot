using System.Diagnostics;
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
        if (Debugger.IsAttached)
            return;

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
            }

            File.AppendAllText(logPath, text);
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

    public static void Debug(string text, bool newLine = true)
    {
        if (!Debugger.IsAttached)
            return;

        lock (logLockObj)
        {
            FormatColorWrite(text, ConsoleColor.Cyan, newLine);
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

    public static void Error(string text, bool newLine = true, bool writeLog = true)
    {
        lock (logLockObj)
        {
            FormatColorWrite(text, ConsoleColor.DarkRed, newLine);
            if (writeLog) WriteLogToFile(LogType.Error, text);
        }
    }

    public static void Error(Exception ex, string text, bool newLine = true, bool writeLog = true)
    {
        lock (logLockObj)
        {
            FormatColorWrite(text, ConsoleColor.DarkRed, newLine);
            FormatColorWrite(ex.ToString(), ConsoleColor.DarkRed, true);

            if (writeLog)
            {
                WriteLogToFile(LogType.Error, $"{text}");
                WriteLogToFile(LogType.Error, $"{ex}");
            }
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

        if (message.Exception != null && message.Message != null && !message.Message.Contains("TYPING_START") && (message.Exception is not GatewayReconnectException || message.Exception is not TaskCanceledException))
        {
            consoleColor = ConsoleColor.DarkRed;
#if RELEASE
            FormatColorWrite(message.Message, consoleColor);
#endif
            FormatColorWrite(message.Exception.GetType().FullName, consoleColor);
            FormatColorWrite(message.Exception.Message, consoleColor);
            FormatColorWrite(message.Exception.StackTrace, consoleColor);
        }

        return Task.CompletedTask;
    }
}