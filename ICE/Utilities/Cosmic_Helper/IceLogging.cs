using ECommons.GameHelpers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ICE.Utilities.Cosmic_Helper;

// 必須是 public：MissionConfigs 是 public，而它有一個型別為
// IceLogging.LogLevel 的設定屬性 —— 巢狀列舉跟著外層類別的可及性，不放寬會 CS0053。
// ⚠️ 設定屬性必須是 public，YamlDotNet 只序列化 public 屬性；改成 internal 的話
//    設定「存得下去但讀不回來」，而且不會報錯。
public static class IceLogging
{
    // 🔴 快取的理由：沒帶 prefix 的呼叫點每一次 log 都要 `new StackFrame(3).GetMethod()`（一次完整的堆疊走訪）。
    // 🔴 GetCallerPrefix() 必須維持「由 FormatMessage 直接呼叫」不可再包一層：
    //    StackFrame(3) 數的是實體堆疊層數（0=GetCallerPrefix／1=FormatMessage／
    //    2=公開的 Info/Debug/…／3=真正的呼叫端）。中間多插一層，這個 3 就指到錯的方法，
    //    而且**不會報錯，只會靜默印出別人的名字**。
    private sealed class CallSiteComparer : IEqualityComparer<(string File, int Line)>
    {
        public static readonly CallSiteComparer Instance = new();

        public bool Equals((string File, int Line) x, (string File, int Line) y)
            => x.Line == y.Line && ReferenceEquals(x.File, y.File);

        public int GetHashCode((string File, int Line) o)
            => ((o.File is null ? 0 : RuntimeHelpers.GetHashCode(o.File)) * 397) ^ o.Line;
    }

    // 上限＝沒帶 prefix 的相異呼叫點數，不會無限成長。
    private static readonly ConcurrentDictionary<(string, int), string> CallerPrefixCache = new(CallSiteComparer.Instance);

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

    // callerFile／callerLine 是編譯期填入的；
    // 帶了 prefix 的呼叫點直接短路，連快取都不查。
    private static string FormatMessage(string message, string prefix = null, string callerFile = null, int callerLine = 0)
    {
        string callerPrefix;
        if (prefix != null)
        {
            callerPrefix = prefix;
        }
        else if (callerFile != null && CallerPrefixCache.TryGetValue((callerFile, callerLine), out var cached))
        {
            callerPrefix = cached;
        }
        else
        {
            // 每個呼叫點只會走到這裡一次（callerFile 為 null 時代表是反射／動態呼叫，
            // 那種情況拿不到編譯期常數，就每次都算）。
            callerPrefix = GetCallerPrefix();
            if (callerFile != null)
                CallerPrefixCache[(callerFile, callerLine)] = callerPrefix;
        }
        return $"{callerPrefix} {message}";
    }

    // 🔴 上限鎖死在 Info：Information 是請使用者回報診斷的既定管道，所以 Info 以上**在結構上就關不掉** ——
    //    不是靠「UI 不提供那個選項」，是靠這個 setter 夾住。
    //    ⇒ 底下也只有 Verbose 與 Debug 兩個方法帶閘門，Info/Warning/Error/Chat* 一律不加。
    // 📌 預設 Verbose（全部都寫）。LogLevel 的零值就是 Verbose，所以設定檔缺這個鍵的既有使用者拿到的也是「全開」。
    private static LogLevel minimumLevel = LogLevel.Verbose;

    /// <summary>
    /// 寫入端門檻：低於這個等級的 log <b>完全不進緩衝區、不組字串、不呼叫 PluginLog</b>。
    /// 預設 <see cref="LogLevel.Verbose"/>（全部寫入＝現行行為）。
    /// </summary>
    /// <remarks>
    /// 🔴 寫入值會被夾在 <see cref="LogLevel.Verbose"/>～<see cref="LogLevel.Info"/> 之間。
    /// Information 以上是使用者回報診斷用的管道，不接受被關掉。
    /// </remarks>
    public static LogLevel MinimumLevel
    {
        get => minimumLevel;
        set => minimumLevel = value < LogLevel.Verbose ? LogLevel.Verbose
             : value > LogLevel.Info ? LogLevel.Info
             : value;
    }

    /// <summary>這個等級現在寫不寫得進去。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEnabled(LogLevel level) => level >= minimumLevel;

    // ────────────────────────────────────────────────────────────────────────
    // 內插字串處理器 —— 讓閘門擋在「求值之前」
    // ────────────────────────────────────────────────────────────────────────
    // 🔴 光有上面的 if 不夠：`IceLogging.Debug($"x={Foo()}")` 的內插**在呼叫點就求值完畢**。
    // ⚠️ 只有 Verbose/Debug 有處理器（只有這兩級關得掉）。閘門關著時，內插洞裡的副作用不會發生。
    [InterpolatedStringHandler]
    public ref struct VerboseLogHandler
    {
        private DefaultInterpolatedStringHandler inner;
        public readonly bool Enabled;

        public VerboseLogHandler(int literalLength, int formattedCount, out bool shouldAppend)
        {
            if (IsEnabled(LogLevel.Verbose))
            {
                inner = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
                Enabled = shouldAppend = true;
            }
            else
            {
                inner = default;
                Enabled = shouldAppend = false;
            }
        }

        public void AppendLiteral(string value) => inner.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => inner.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string format) => inner.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment) => inner.AppendFormatted(value, alignment);
        public void AppendFormatted<T>(T value, int alignment, string format) => inner.AppendFormatted(value, alignment, format);
        public void AppendFormatted(ReadOnlySpan<char> value) => inner.AppendFormatted(value);
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string format = null) => inner.AppendFormatted(value, alignment, format);
        public void AppendFormatted(string value) => inner.AppendFormatted(value);
        public void AppendFormatted(string value, int alignment = 0, string format = null) => inner.AppendFormatted(value, alignment, format);
        public void AppendFormatted(object value, int alignment = 0, string format = null) => inner.AppendFormatted(value, alignment, format);
        public string ToStringAndClear() => inner.ToStringAndClear();
    }

    /// <inheritdoc cref="VerboseLogHandler"/>
    [InterpolatedStringHandler]
    public ref struct DebugLogHandler
    {
        private DefaultInterpolatedStringHandler inner;
        public readonly bool Enabled;

        public DebugLogHandler(int literalLength, int formattedCount, out bool shouldAppend)
        {
            if (IsEnabled(LogLevel.Debug))
            {
                inner = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
                Enabled = shouldAppend = true;
            }
            else
            {
                inner = default;
                Enabled = shouldAppend = false;
            }
        }

        public void AppendLiteral(string value) => inner.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => inner.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string format) => inner.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment) => inner.AppendFormatted(value, alignment);
        public void AppendFormatted<T>(T value, int alignment, string format) => inner.AppendFormatted(value, alignment, format);
        public void AppendFormatted(ReadOnlySpan<char> value) => inner.AppendFormatted(value);
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string format = null) => inner.AppendFormatted(value, alignment, format);
        public void AppendFormatted(string value) => inner.AppendFormatted(value);
        public void AppendFormatted(string value, int alignment = 0, string format = null) => inner.AppendFormatted(value, alignment, format);
        public void AppendFormatted(object value, int alignment = 0, string format = null) => inner.AppendFormatted(value, alignment, format);
        public string ToStringAndClear() => inner.ToStringAndClear();
    }

    public static void Verbose(string message, string prefix = null, bool debugOnly = false,
        [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
    {
        if (!IsEnabled(LogLevel.Verbose)) return;
        var formattedMessage = FormatMessage(message, prefix, callerFile, callerLine);
        PluginLog.Verbose(formattedMessage);
        LogSystem.Log(LogLevel.Verbose, message, prefix);
    }

    /// <summary>內插字串專用多載，見 <see cref="VerboseLogHandler"/>。</summary>
    /// <remarks>
    /// 🔴 <b>不可以</b>改成轉呼叫上面的 <c>Verbose(string, …)</c>：<see cref="GetCallerPrefix"/>
    /// 用 <c>StackFrame(3)</c> 數的是<b>實體堆疊層數</b>，中間多一層就會印出 IceLogging 自己的名字，
    /// 而且會被寫進 <see cref="CallerPrefixCache"/> —— 之後每一次都錯，且不會報錯。
    /// 所以這裡刻意把本體抄一份，維持「FormatMessage 由公開方法直接呼叫」這個層數。
    /// </remarks>
    public static void Verbose(VerboseLogHandler message, string prefix = null, bool debugOnly = false,
        [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
    {
        if (!message.Enabled) return;
        var text = message.ToStringAndClear();
        var formattedMessage = FormatMessage(text, prefix, callerFile, callerLine);
        PluginLog.Verbose(formattedMessage);
        LogSystem.Log(LogLevel.Verbose, text, prefix);
    }

    public static void Debug(string message, string prefix = null, bool debugOnly = false,
        [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
    {
        if (!IsEnabled(LogLevel.Debug)) return;
        LogSystem.Log(LogLevel.Debug, message, prefix);
        if (debugOnly)
        {
#if DEBUG
            var formattedMessage = FormatMessage(message, prefix, callerFile, callerLine);
            PluginLog.Debug(formattedMessage);
#endif
        }
        else
        {
            var formattedMessage = FormatMessage(message, prefix, callerFile, callerLine);
            PluginLog.Debug(formattedMessage);
        }
    }

    /// <summary>內插字串專用多載，見 <see cref="DebugLogHandler"/>。</summary>
    /// <remarks>🔴 同 <see cref="Verbose(VerboseLogHandler, string, bool, string, int)"/>：不可轉呼叫，會弄壞 StackFrame(3)。</remarks>
    public static void Debug(DebugLogHandler message, string prefix = null, bool debugOnly = false,
        [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
    {
        if (!message.Enabled) return;
        var text = message.ToStringAndClear();
        LogSystem.Log(LogLevel.Debug, text, prefix);
        if (debugOnly)
        {
#if DEBUG
            var formattedMessage = FormatMessage(text, prefix, callerFile, callerLine);
            PluginLog.Debug(formattedMessage);
#endif
        }
        else
        {
            var formattedMessage = FormatMessage(text, prefix, callerFile, callerLine);
            PluginLog.Debug(formattedMessage);
        }
    }

    public static void Info(string message, string prefix = null, bool debugOnly = false,
        [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
    {
        LogSystem.Log(LogLevel.Info, message, prefix);
        if (debugOnly)
        {
#if DEBUG
            var formattedMessage = FormatMessage(message, prefix, callerFile, callerLine);
            PluginLog.Information(formattedMessage);
#endif
        }
        else
        {
            var formattedMessage = FormatMessage(message, prefix, callerFile, callerLine);
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

    public static void Warning(string message, string prefix = null,
        [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
    {
        LogSystem.Log(LogLevel.Warning, message, prefix);
        var formattedMessage = FormatMessage(message, prefix, callerFile, callerLine);
        PluginLog.Warning(formattedMessage);
    }

    public static void Error(string message, string prefix = null,
        [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
    {
        LogSystem.Log(LogLevel.Error, message, prefix);
        var formattedMessage = FormatMessage(message, prefix, callerFile, callerLine);
        PluginLog.Error(formattedMessage);
    }

    // ⚠️ LogLevel.Verbose 不是筆誤，是既有行為（Fatal 目前全庫零呼叫點），這裡不動它。
    public static void Fatal(string message, string prefix = null,
        [CallerFilePath] string callerFile = null, [CallerLineNumber] int callerLine = 0)
    {
        LogSystem.Log(LogLevel.Verbose, message, prefix);
        var formattedMessage = FormatMessage(message, prefix, callerFile, callerLine);
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
        // Queue 淘汰是 O(1) Dequeue（List.RemoveAt(0) 每次都要搬移整份陣列）。
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
        // Queue 淘汰是 O(1) Dequeue（List.RemoveAt(0) 每次都要搬移整份陣列）。
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
