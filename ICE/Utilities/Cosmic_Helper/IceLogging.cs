using ECommons.GameHelpers;
using System.Collections.Generic;
using System.Diagnostics;

namespace ICE.Utilities.Cosmic_Helper;

internal static class IceLogging
{
    private static string GetCallerPrefix()
    {
        var stackFrame = new StackFrame(3);
        var method = stackFrame.GetMethod();
        var className = method?.DeclaringType?.Name;
        var methodName = method?.Name;

        if (className != null && methodName != null)
        {
            return $"[{className}.{methodName}]";
        }
        else if (className != null)
        {
            return $"[{className}]";
        }
        else if (methodName != null)
        {
            return $"[{methodName}]";
        }
        return string.Empty;
    }

    private static string FormatMessage(string message, string prefix = null)
    {
        var callerPrefix = prefix ?? GetCallerPrefix();
        return $"{callerPrefix} {message}";
    }

    public static void Verbose(string message, string prefix = null, bool debugOnly = false)
    {
        var formattedMessage = FormatMessage(message, prefix);
        PluginLog.Verbose(formattedMessage);
        LogSystem.Log(LogLevel.Verbose, message, prefix);
    }

    public static void Debug(string message, string prefix = null, bool debugOnly = false)
    {
        LogSystem.Log(LogLevel.Debug, message, prefix);
        if (debugOnly)
        {
#if DEBUG
            var formattedMessage = FormatMessage(message, prefix);
            PluginLog.Debug(formattedMessage);
#endif
        }
        else
        {
            var formattedMessage = FormatMessage(message, prefix);
            PluginLog.Debug(formattedMessage);
        }
    }

    public static void Info(string message, string prefix = null, bool debugOnly = false)
    {
        LogSystem.Log(LogLevel.Info, message, prefix);
        if (debugOnly)
        {
#if DEBUG
            var formattedMessage = FormatMessage(message, prefix);
            PluginLog.Information(formattedMessage);
#endif
        }
        else
        {
            var formattedMessage = FormatMessage(message, prefix);
            PluginLog.Information(formattedMessage);
        }
    }

    public static void ChatInfo(string s, string prefix = null)
    {
        LogSystem.Log(LogLevel.Info, s, prefix);
        if (prefix == null)
        {
            if (EzThrottler.Throttle($"Throttling chat message: {s}", 60000))
            {
                Svc.Chat.Print(s);
                PluginLog.Information(s);
            }
        }
        else
        {
            if (EzThrottler.Throttle($"Throttling chat message: {s}", 60000))
            {
                Svc.Chat.Print($"{prefix} {s}");
                PluginLog.Information($"{prefix} {s}");
            }
        }
    }

    public static void ChatError(string s, string prefix = null)
    {
        LogSystem.Log(LogLevel.Error, s, prefix);
        if (prefix == null)
        {
            if (EzThrottler.Throttle($"Throttling chat message: {s}", 60000))
            {
                ECommons.ChatMethods.ChatPrinter.Red($"{s}");
                PluginLog.Error(s);
            }
        }
        else
        {
            if (EzThrottler.Throttle($"Throttling chat message: {s}", 60000))
            {
                ECommons.ChatMethods.ChatPrinter.Red($"{prefix} {s}");
                PluginLog.Error($"{prefix} {s}");
            }
        }
    }

    public static void Warning(string message, string prefix = null)
    {
        LogSystem.Log(LogLevel.Warning, message, prefix);
        var formattedMessage = FormatMessage(message, prefix);
        PluginLog.Warning(formattedMessage);
    }

    public static void Error(string message, string prefix = null)
    {
        LogSystem.Log(LogLevel.Error, message, prefix);
        var formattedMessage = FormatMessage(message, prefix);
        PluginLog.Error(formattedMessage);
    }

    public static void Fatal(string message, string prefix = null)
    {
        LogSystem.Log(LogLevel.Verbose, message, prefix);
        var formattedMessage = FormatMessage(message, prefix);
        PluginLog.Fatal(formattedMessage);
    }

    public enum LogLevel
    {
        Verbose,
        Debug,
        Info,
        Warning,
        Error
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }
        public string? Category { get; set; } // Optional: for filtering by system/feature

        public LogEntry(LogLevel level, string message, string? category = null)
        {
            Timestamp = DateTime.Now;
            Level = level;
            Message = message;
            Category = category;
        }
    }

    public class DestinationEntry
    {
        public DateTime Timestamp { get; set; }
        public Vector3 PlayerStart { get; set; }
        public Vector3 PlayerDestination { get; set; }
        public float Distance { get; set; }

        public DestinationEntry(Vector3 end)
        {
            Timestamp = DateTime.Now;
            PlayerStart = Player.Position;
            PlayerDestination = end;
            Distance = Vector3.Distance(Player.Position, end);
        }
    }

    public static class DestinationLogs
    {
        // Queue 淘汰是 O(1) Dequeue；先前用 List.RemoveAt(0) 每次都要搬移整份陣列 (O(n))。
        private static readonly Queue<DestinationEntry> logs = new();
        private static int maxDestinationCount = 3000;

        public static IReadOnlyCollection<DestinationEntry> Logs => logs;
        public static void Log(Vector3 end)
        {
            logs.Enqueue(new DestinationEntry(end));
            if (logs.Count > maxDestinationCount)
                logs.Dequeue();
        }
    }

    public static class LogSystem
    {
        // Queue 淘汰是 O(1) Dequeue；先前用 List.RemoveAt(0) 每次都要搬移整份陣列 (O(n))。
        private static readonly Queue<LogEntry> logs = new();
        private static int maxLogCount = 3000; // Prevent memory bloat

        public static IReadOnlyCollection<LogEntry> Logs => logs;

        public static void Log(LogLevel level, string message, string? category = null)
        {
            logs.Enqueue(new LogEntry(level, message, category));

            // Keep only recent logs
            if (logs.Count > maxLogCount)
            {
                logs.Dequeue();
            }
        }

        public static void Verbose(string message, string? category = null) => Log(LogLevel.Verbose, message, category);

        public static void Debug(string message, string? category = null)
            => Log(LogLevel.Debug, message, category);

        public static void Info(string message, string? category = null)
            => Log(LogLevel.Info, message, category);

        public static void Warning(string message, string? category = null)
            => Log(LogLevel.Warning, message, category);

        public static void Error(string message, string? category = null)
            => Log(LogLevel.Error, message, category);

        public static void Clear() => logs.Clear();

        public static void CopyToClipboard()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var log in logs)
            {
                sb.AppendLine($"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] [{log.Level}] {(log.Category != null ? $"[{log.Category}] " : "")}{log.Message}");
            }
            ImGui.SetClipboardText(sb.ToString());
        }
    }
}
