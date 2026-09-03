using ECommons.GameHelpers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ICE.Utilities.Cosmic_Helper;

// public（原本是 internal）：MissionConfigs 是 public，而它現在有一個型別為
// IceLogging.LogLevel 的設定屬性 —— 巢狀列舉跟著外層類別的可及性，不放寬會 CS0053。
// ⚠️ 設定屬性必須是 public，YamlDotNet 只序列化 public 屬性；改成 internal 的話
//    設定「存得下去但讀不回來」，而且不會報錯。
public static class IceLogging
{
    // 🔴 為什麼要快取：沒帶 prefix 的呼叫點，每一次 log 都要做一次
    //    `new StackFrame(3).GetMethod()` —— 那是一次完整的堆疊走訪加上把 metadata token
    //    解回 MethodBase，不是「取一個欄位」。
    //    離線量測（Release / net9 / 20 萬次；量測台在 scratchpad，不隨 repo 出貨）：
    //      A 現況：每次都走 StackFrame ............. 約 2540 ns/次
    //      B 快取命中（本作法）..................... 約 100~120 ns/次
    //      C 對照：本來就帶 prefix（?? 短路）....... 約 42~71 ns/次
    //    ⇒ 約 21~27 倍。
    //
    // 📌 規模（2026-08-16 全庫掃描，兩支獨立實作互相對過，數字一致；
    //    通用版工具在 ~/.claude/tools/fleet/callsite_arity.py，帶自測與校準閘門）：
    //    590 個 IceLogging 呼叫點，其中 **242 個沒帶 prefix**；
    //    這 242 個裡 **186 個外層沒有任何 Throttle 守衛**，且 **203 個位在 Scheduler/**
    //    —— 那些是 NeoTaskManager 的任務本體，只要回 false 就**每一幀再跑一次**。
    // ⚠️ 最違反直覺的一塊：Debug／Verbose 是「先組好字串再交給 PluginLog」。
    //    使用者跑 LogLevel 1 時 Dalamud 會把輸出整個丟掉，**但這段 StackFrame 成本照付**。
    //    沒帶 prefix 的 242 個裡有 137 個是 Debug。
    //
    // 🔴 為什麼 (檔案, 行號) 可以當快取鍵：兩個都是 `[Caller*]` **編譯期常數**（執行期零成本），
    //    而一個原始碼行只會屬於一個方法本體（包含編譯器替 lambda 產生的方法），
    //    所以「這個呼叫點 → GetCallerPrefix() 的結果」是固定的。
    // 🔑 值一律仍由**同一段 GetCallerPrefix()** 算出來，不是另外用 CallerMemberName 拼字串
    //    —— 所以輸出與改之前**逐字相同**，含 lambda 的 `<>c.<X>b__n_m` 這種形狀。
    //    （用 [CallerMemberName] 拼會把 lambda 變成外層方法名、把 class 名變成檔名，那是行為變更。）
    //
    // 🔴 GetCallerPrefix() 必須維持「由 FormatMessage 直接呼叫」不可再包一層：
    //    StackFrame(3) 數的是實體堆疊層數（0=GetCallerPrefix／1=FormatMessage／
    //    2=公開的 Info/Debug/…／3=真正的呼叫端）。中間多插一層，這個 3 就指到錯的方法，
    //    而且**不會報錯，只會靜默印出別人的名字**。
    //
    // 📌 比較器用參考相等：`[CallerFilePath]` 是字面值，同一個檔的所有呼叫點共用同一個
    //    interned 參考，所以雜湊可以用 O(1) 的 RuntimeHelpers.GetHashCode，
    //    而不是 O(路徑長度) 的字串雜湊（實測差約 2 倍）。
    //    萬一哪天參考不同也只是多一筆快取條目 —— 值仍然是從堆疊算出來的，不會給錯答案。
    private sealed class CallSiteComparer : IEqualityComparer<(string File, int Line)>
    {
        public static readonly CallSiteComparer Instance = new();

        public bool Equals((string File, int Line) x, (string File, int Line) y)
            => x.Line == y.Line && ReferenceEquals(x.File, y.File);

        public int GetHashCode((string File, int Line) o)
            => ((o.File is null ? 0 : RuntimeHelpers.GetHashCode(o.File)) * 397) ^ o.Line;
    }

    // 上限＝沒帶 prefix 的相異呼叫點數（目前 248），不會無限成長。
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

    // callerFile／callerLine 是編譯期填入的，呼叫端一個字都不用改；
    // 帶了 prefix 的呼叫點行為完全不變（跟以前一樣直接短路，連快取都不查）。
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
            // 那種情況拿不到編譯期常數，就維持原本每次都算的行為）。
            callerPrefix = GetCallerPrefix();
            if (callerFile != null)
                CallerPrefixCache[(callerFile, callerLine)] = callerPrefix;
        }
        return $"{callerPrefix} {message}";
    }

    // ────────────────────────────────────────────────────────────────────────
    // 寫入端等級閘門
    // ────────────────────────────────────────────────────────────────────────
    // 🔴 這裡修的是「Debug 關著就免費」這個**錯的**直覺（與 ECommons InternalLog 同形狀）：
    //    關掉 Dalamud 的記錄等級只省下**輸出端**，寫入端該付的照付 ——
    //    ① 呼叫點的內插字串已經組好（配置＋格式化），
    //    ② FormatMessage 再串一次前綴（再一次配置），
    //    ③ LogSystem.Log 進 3000 筆環形緩衝區（LogEntry 配置＋DateTime.Now），
    //    ④ ECommons PluginLog 還會 RunOnFrameworkThread 推一份進它自己的 InternalLog。
    //    使用者跑 LogLevel 1 時，②③④ 全部是為了一行**沒有人看得到的輸出**在付錢。
    //
    // 🔴 上限鎖死在 Info：使用者跑 LogLevel 1，**Information 是請他回報診斷的既定管道**
    //    （宇宙探索／釣魚／機甲那些中文診斷行）。所以 Info 以上**在結構上就關不掉** ——
    //    不是靠「UI 不提供那個選項」，是靠這個 setter 夾住。
    //    ⇒ 底下也只有 Verbose 與 Debug 兩個方法帶閘門，Info/Warning/Error/Chat* 一律不加，
    //      免得哪天有人放寬上限時，診斷管道跟著被靜默關掉。
    //
    // 📌 預設 Verbose＝**維持現行行為**（全部都寫）。LogLevel 的零值就是 Verbose，
    //    所以設定檔缺這個鍵的既有使用者拿到的也是「全開」，不會有人的行為被改掉。
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
    // 🔴 為什麼光有上面的 if 不夠：`IceLogging.Debug($"x={Foo()} y={bar.Baz}")` 的內插
    //    **在呼叫點就求值完畢**，方法裡再怎麼早 return，字串也早就組好了。
    //    C# 10 的 InterpolatedStringHandler 是唯一能在「不改任何呼叫點」的前提下
    //    把求值本身擋掉的機制：編譯器改成先建構處理器，由建構子的 out shouldAppend
    //    決定要不要繼續 —— 回 false 時**每一個洞都不會被求值**。
    // 📌 呼叫端一個字都不用改：對內插字串引數，多載解析規定處理器型別優先於 string；
    //    傳一般 string 變數的呼叫點則照舊走 string 多載。
    // ⚠️ 只有 Verbose/Debug 有處理器，因為只有這兩級關得掉（見上面的夾擠）。
    // ⚠️ 代價：閘門關著時，內插洞裡的副作用不會發生。log 引數本來就不該有副作用，
    //    而且預設全開，所以只有主動調高門檻的人會遇到。
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
