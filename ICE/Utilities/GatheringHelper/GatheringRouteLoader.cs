using ICE.Resources.GatheringRoutes;
using ICE.Utilities.Cosmic_Helper;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ICE.Utilities.GatheringHelper;

/// <summary>這條路線目前是從哪裡來的。</summary>
public enum GatheringRouteSource
{
    /// <summary>編譯進 DLL 的內嵌路線（<c>ICE/Resources/GatheringRoutes/</c>）。</summary>
    BuiltIn,

    /// <summary>使用者放在 <see cref="GatheringRouteLoader.CustomRoutesDirectory"/> 的自訂路線。</summary>
    Custom,
}

/// <summary>一個自訂路線檔載入失敗的紀錄。UI 要把這個畫在列上，不能只寫進 log。</summary>
public sealed class GatheringRouteLoadError
{
    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;

    /// <summary>失敗原因；YAML 語法錯誤時會帶行號與欄號。</summary>
    public string Reason { get; init; } = string.Empty;
}

public static class GatheringRouteLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// 一次載入的完整結果。
    /// </summary>
    /// <remarks>
    /// 🔑 刻意做成「整包換掉」而不是就地改：<see cref="LoadAllRoutes"/> 會被排程器每個 tick 呼叫，
    /// 而重新載入是從 UI 觸發的。建好一整份新的再一次指派上去，正在跑的那一邊拿到的
    /// 永遠是「舊的完整快照」或「新的完整快照」，不會讀到蓋到一半的字典。
    /// </remarks>
    private sealed class RouteSnapshot
    {
        public Dictionary<uint, Dictionary<Vector2, List<GathNodeInfo>>> Routes = new();
        public Dictionary<uint, Dictionary<Vector2, GatheringRouteSource>> Sources = new();
        public Dictionary<uint, Dictionary<Vector2, string>> CustomFiles = new();
        public Dictionary<uint, Dictionary<Vector2, (string ZoneName, string Job)>> Meta = new();
        public List<GatheringRouteLoadError> Errors = new();
        public int BuiltInCount;
        public int CustomCount;
        public int OverriddenCount;
    }

    private static RouteSnapshot? _snapshot;
    private static readonly object _loadLock = new();

    /// <summary>
    /// 使用者自訂路線的資料夾。放進去的 <c>.yaml</c> 會覆寫同「區域＋旗標座標」的內嵌路線。
    /// </summary>
    /// <remarks>
    /// ⚠️ 刻意<b>不</b>沿用匯出用的 <c>ExportedRoutes</c>／<c>C.CustomRoutePath</c>：
    /// 既有使用者可能早就按過「Export All Routes」，那個資料夾裡躺著一份當時版本的全量複本。
    /// 如果直接把它當自訂來源，升級後那份舊複本會<b>靜默覆蓋掉所有內建路線</b>（包含以後更新的），
    /// 而且完全沒有徵兆。改用一個全新的資料夾 ⇒ 對所有既有使用者來說一開始是空的，
    /// 行為與升級前完全一致。
    /// </remarks>
    public static string CustomRoutesDirectory
        => Path.Combine(Svc.PluginInterface.ConfigDirectory.FullName, "CustomRoutes");

    public static Dictionary<uint, Dictionary<Vector2, List<GathNodeInfo>>> LoadAllRoutes()
        => GetSnapshot().Routes;

    private static RouteSnapshot GetSnapshot()
    {
        var current = _snapshot;
        if (current != null)
            return current;

        lock (_loadLock)
        {
            // 等鎖的時候可能已經有人建好了
            if (_snapshot != null)
                return _snapshot;

            var built = BuildSnapshot();
            _snapshot = built;
            return built;
        }
    }

    private static RouteSnapshot BuildSnapshot()
    {
        var snap = new RouteSnapshot();

        LoadBuiltInRoutes(snap);
        LoadCustomRoutes(snap);

        IceLogging.Info($"採集路線載入完成：{snap.Routes.Count} 個區域、" +
                        $"內建 {snap.BuiltInCount} 條、自訂 {snap.CustomCount} 條" +
                        $"（其中 {snap.OverriddenCount} 條覆寫了內建）、" +
                        $"讀取失敗 {snap.Errors.Count} 個檔。", "[採集路線]");

        return snap;
    }

    private static void LoadBuiltInRoutes(RouteSnapshot snap)
    {
        var assembly = Assembly.GetExecutingAssembly();

        var resourceNames = assembly.GetManifestResourceNames()
            .Where(r => r.Contains("GatheringRoutes") && r.EndsWith(".yaml"))
            .ToList();

        PluginLog.Information($"Found {resourceNames.Count} gathering route resources");

        foreach (var resourceName in resourceNames)
        {
            try
            {
                var route = LoadRouteFromResource(resourceName);
                Store(snap, route, GatheringRouteSource.BuiltIn, customFilePath: null);
                snap.BuiltInCount++;

                PluginLog.Debug($"Loaded route: Zone {route.ZoneId}, Flag ({route.Flag.X}, {route.Flag.Y}), Job {route.Job}");
            }
            catch (Exception ex)
            {
                // 內嵌資源壞掉是我們自己的包裝問題，不是使用者能修的 —— 保持原本的 Error 等級。
                PluginLog.Error($"Failed to load route from {resourceName}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 讀 <see cref="CustomRoutesDirectory"/> 底下所有 <c>.yaml</c>，覆寫同「區域＋旗標」的內建路線。
    /// </summary>
    /// <remarks>
    /// 🔴 任何一個檔壞掉都<b>只影響那一個檔</b>：跳過它、保留內建的那條、把原因記進
    /// <see cref="LoadErrors"/> 並寫一行 Information（使用者跑 LogLevel 1，Debug 收得到但單檔數十萬行會淹沒）。<br/>
    /// 🔴 節點數 0 的檔<b>一律拒絕</b>而不是採用 —— 採用它等於讓排程器走一條空路線，
    /// 那會表現成「站著不動」而且完全沒有訊息，比壞檔本身更難查。
    /// </remarks>
    private static void LoadCustomRoutes(RouteSnapshot snap)
    {
        string dir;
        try
        {
            dir = CustomRoutesDirectory;
            if (!Directory.Exists(dir))
                return;
        }
        catch (Exception ex)
        {
            IceLogging.Info($"讀不到自訂採集路線資料夾，這一輪全部使用內建路線：{ex.Message}", "[採集路線]");
            return;
        }

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, "*.yaml", SearchOption.AllDirectories).ToList();
        }
        catch (Exception ex)
        {
            IceLogging.Info($"列舉自訂採集路線資料夾失敗，這一輪全部使用內建路線：{ex.Message}", "[採集路線]");
            return;
        }

        if (files.Count == 0)
            return;

        IceLogging.Info($"在 {dir} 找到 {files.Count} 個自訂採集路線檔，開始載入。", "[採集路線]");

        foreach (var file in files)
        {
            GatheringRouteFile? route = null;
            string? reason = null;

            try
            {
                var yaml = File.ReadAllText(file);
                route = Deserializer.Deserialize<GatheringRouteFile>(yaml);

                if (route == null)
                    reason = "檔案內容是空的，或不是一份採集路線。";
                else if (route.ZoneId == 0)
                    reason = "zone_id 是 0（缺少 zone_id 欄位，或寫成了 0）。";
                else if (route.Nodes == null || route.Nodes.Count == 0)
                    reason = "nodes 清單是空的 —— 採用它會讓插件走一條沒有採集點的路線，所以跳過。";
                else if (route.Nodes.All(n => n.NodeId == 0))
                    reason = $"{route.Nodes.Count} 個節點的 node_id 全部是 0，沒有任何一個採集點可以對得上。";
            }
            catch (YamlException ex)
            {
                // YamlException 帶得到出錯位置 —— 這是使用者唯一真正需要的資訊。
                reason = $"YAML 語法錯誤，位置在第 {ex.Start.Line} 行第 {ex.Start.Column} 欄：{ex.Message}";
            }
            catch (Exception ex)
            {
                reason = $"{ex.GetType().Name}：{ex.Message}";
            }

            if (reason != null || route == null)
            {
                var error = new GatheringRouteLoadError
                {
                    FileName = Path.GetFileName(file),
                    FullPath = file,
                    Reason = reason ?? "未知原因。",
                };
                snap.Errors.Add(error);

                IceLogging.Info($"自訂採集路線 {error.FileName} 載入失敗，這個旗標改用內建路線。" +
                                $"原因：{error.Reason}（完整路徑：{error.FullPath}）", "[採集路線]");
                continue;
            }

            var overrides = TryGet(snap.Sources, route.ZoneId, route.Flag, out _);
            Store(snap, route, GatheringRouteSource.Custom, file);
            snap.CustomCount++;
            if (overrides)
                snap.OverriddenCount++;

            IceLogging.Info($"套用自訂採集路線：區域 {route.ZoneId} 旗標 ({route.Flag.X}, {route.Flag.Y})、" +
                            $"{route.Nodes.Count} 個採集點、來源 {Path.GetFileName(file)}" +
                            $"{(overrides ? "（覆寫內建）" : "（內建沒有這條，新增）")}。", "[採集路線]");
        }
    }

    private static void Store(RouteSnapshot snap, GatheringRouteFile route, GatheringRouteSource source, string? customFilePath)
    {
        if (!snap.Routes.ContainsKey(route.ZoneId))
        {
            snap.Routes[route.ZoneId] = new Dictionary<Vector2, List<GathNodeInfo>>();
            snap.Sources[route.ZoneId] = new Dictionary<Vector2, GatheringRouteSource>();
            snap.CustomFiles[route.ZoneId] = new Dictionary<Vector2, string>();
            snap.Meta[route.ZoneId] = new Dictionary<Vector2, (string, string)>();
        }

        snap.Routes[route.ZoneId][route.Flag] = route.Nodes;
        snap.Sources[route.ZoneId][route.Flag] = source;
        snap.Meta[route.ZoneId][route.Flag] = (route.ZoneName, route.Job);

        if (customFilePath != null)
            snap.CustomFiles[route.ZoneId][route.Flag] = customFilePath;
        else
            snap.CustomFiles[route.ZoneId].Remove(route.Flag);
    }

    private static bool TryGet<T>(Dictionary<uint, Dictionary<Vector2, T>> outer, uint zoneId, Vector2 flag, out T value)
    {
        value = default!;
        if (outer.TryGetValue(zoneId, out var inner) && inner.TryGetValue(flag, out var found))
        {
            value = found;
            return true;
        }
        return false;
    }

    private static GatheringRouteFile LoadRouteFromResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new FileNotFoundException($"Resource not found: {resourceName}");

        using var reader = new StreamReader(stream);
        var yaml = reader.ReadToEnd();

        return Deserializer.Deserialize<GatheringRouteFile>(yaml)
            ?? throw new InvalidDataException($"Failed to deserialize {resourceName}");
    }

    // Get routes for a specific zone and flag
    public static List<GathNodeInfo>? GetRoute(uint zoneId, Vector2 flag)
    {
        var routes = LoadAllRoutes();

        if (routes.TryGetValue(zoneId, out var zoneRoutes))
        {
            if (zoneRoutes.TryGetValue(flag, out var nodes))
                return nodes;
        }

        return null;
    }

    // Clear cache if needed (for testing/reloading)
    public static void ClearCache()
    {
        lock (_loadLock)
            _snapshot = null;
    }

    /// <summary>
    /// 丟掉快取並立刻重建。改完自訂路線檔按這個就生效，不必重開遊戲。
    /// </summary>
    /// <remarks>⚠️ 偵錯視窗路線編輯器裡「還沒存檔」的修改是直接改記憶體裡的清單，重新載入會被丟掉。</remarks>
    public static void ReloadRoutes()
    {
        lock (_loadLock)
        {
            _snapshot = null;
            _snapshot = BuildSnapshot();
        }
    }

    /// <summary>這條路線現在是內建的還是自訂的；查不到這條路線時回 <c>null</c>。</summary>
    public static GatheringRouteSource? GetRouteSource(uint zoneId, Vector2 flag)
        => TryGet(GetSnapshot().Sources, zoneId, flag, out var source) ? source : null;

    /// <summary>這條路線是從哪個自訂檔載進來的；內建的話回 <c>null</c>。</summary>
    public static string? GetCustomFilePath(uint zoneId, Vector2 flag)
        => TryGet(GetSnapshot().CustomFiles, zoneId, flag, out var path) ? path : null;

    /// <summary>
    /// 這條路線的區域名與職業縮寫，只讀載入時建好的快取（不會為了兩個字串重新解析內嵌資源）。
    /// </summary>
    public static (string ZoneName, string Job) GetRouteLabel(uint zoneId, Vector2 flag)
        => TryGet(GetSnapshot().Meta, zoneId, flag, out var meta) ? meta : ("Unknown", string.Empty);

    /// <summary>載入失敗、已經被跳過的自訂路線檔。UI 必須把這個顯示出來。</summary>
    public static IReadOnlyList<GatheringRouteLoadError> LoadErrors => GetSnapshot().Errors;

    public static int BuiltInRouteCount => GetSnapshot().BuiltInCount;
    public static int CustomRouteCount => GetSnapshot().CustomCount;
    public static int OverriddenRouteCount => GetSnapshot().OverriddenCount;

    /// <summary>
    /// 把「目前記憶體裡這條路線的內容」寫進自訂資料夾，並重新載入讓它生效。
    /// </summary>
    /// <returns>寫出去的完整檔案路徑。</returns>
    public static string SaveAsCustomRoute(uint zoneId, Vector2 flag)
    {
        var dir = CustomRoutesDirectory;
        Directory.CreateDirectory(dir);
        ExportRoute(zoneId, flag, dir);
        ReloadRoutes();

        var saved = GetCustomFilePath(zoneId, flag);
        if (saved != null)
            return saved;

        // 理論上重新載入之後一定查得到；查不到就代表剛寫出去的檔立刻被判定為壞檔。
        var (zoneName, job) = GetRouteMetadata(zoneId, flag);
        return Path.Combine(dir, SanitizeFolderName($"{zoneId}_{zoneName}"), BuildFileName(job, flag));
    }

    /// <summary>
    /// 刪掉這條路線的自訂檔（＝還原成內建），並重新載入。
    /// </summary>
    /// <returns>真的刪到檔案才回 <c>true</c>。</returns>
    public static bool DeleteCustomRoute(uint zoneId, Vector2 flag)
    {
        var path = GetCustomFilePath(zoneId, flag);
        if (path == null || !File.Exists(path))
            return false;

        File.Delete(path);
        IceLogging.Info($"已刪除自訂採集路線 {Path.GetFileName(path)}，區域 {zoneId} 旗標 " +
                        $"({flag.X}, {flag.Y}) 還原成內建路線。", "[採集路線]");
        ReloadRoutes();
        return true;
    }

    private static readonly ISerializer Serializer = new SerializerBuilder()
    .WithNamingConvention(UnderscoredNamingConvention.Instance)
    .Build();

    private static string BuildFileName(string job, Vector2 flag)
        => $"{job}_Flag_{(int)flag.X}_{(int)flag.Y}.yaml";

    public static void ExportRoute(uint zoneId, Vector2 flag, string? exportPath = null)
    {
        var routes = LoadAllRoutes();

        if (!routes.TryGetValue(zoneId, out var zoneRoutes))
            throw new InvalidOperationException($"Zone {zoneId} not found");

        if (!zoneRoutes.TryGetValue(flag, out var nodes))
            throw new InvalidOperationException($"Route at flag ({flag.X}, {flag.Y}) not found");

        if (nodes == null || nodes.Count == 0)
            throw new InvalidOperationException("Route has no nodes to export");

        var (zoneName, job) = GetRouteMetadata(zoneId, flag);

        // Ensure we have valid values
        if (string.IsNullOrWhiteSpace(zoneName))
            zoneName = "Unknown";
        if (string.IsNullOrWhiteSpace(job))
            job = "BTN";

        var routeFile = new GatheringRouteFile
        {
            ZoneId = zoneId,
            ZoneName = zoneName,
            Job = job,
            Flag = flag,
            Author = string.IsNullOrWhiteSpace(C.AuthorName) ? "Ice" : C.AuthorName,
            DateModified = DateTime.UtcNow,
            Nodes = nodes
        };

        // Determine base output path
        string basePath;
        if (!string.IsNullOrEmpty(exportPath))
        {
            basePath = exportPath;
        }
        else if (!string.IsNullOrEmpty(C.CustomRoutePath))
        {
            basePath = C.CustomRoutePath;
        }
        else
        {
            basePath = GetDefaultExportPath();
        }

        // Create zone subdirectory: "ZoneId_ZoneName"
        string zoneFolderName = SanitizeFolderName($"{zoneId}_{zoneName}");
        string outputPath = Path.Combine(basePath, zoneFolderName);

        string fileName = BuildFileName(job, flag);
        string fullPath = Path.Combine(outputPath, fileName);

        try
        {
            Directory.CreateDirectory(outputPath);

            string yaml = Serializer.Serialize(routeFile);
            File.WriteAllText(fullPath, yaml);

            PluginLog.Information($"Exported route to {fullPath}");
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to write file: {ex.Message}");
            throw new InvalidOperationException($"Failed to write file to {fullPath}: {ex.Message}", ex);
        }
    }
    private static string SanitizeFolderName(string folderName)
    {
        // Remove invalid characters from folder name
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = string.Join("_", folderName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Trim();
    }

    public static void ExportAllRoutes(string? exportPath = null)
    {
        var routes = LoadAllRoutes();
        string outputPath = exportPath ?? C.CustomRoutePath ?? GetDefaultExportPath();

        int exportedCount = 0;

        foreach (var (zoneId, zoneRoutes) in routes)
        {
            foreach (var (flag, nodes) in zoneRoutes)
            {
                try
                {
                    ExportRoute(zoneId, flag, outputPath);
                    exportedCount++;
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"Failed to export route for zone {zoneId}, flag ({flag.X}, {flag.Y}): {ex.Message}");
                }
            }
        }

        PluginLog.Information($"Exported {exportedCount} routes to {outputPath}");
    }

    private static string GetDefaultExportPath()
    {
        // Get Dalamud config directory
        var configDir = Svc.PluginInterface.ConfigDirectory.FullName;
        return Path.Combine(configDir, "ExportedRoutes");
    }

    private static (string zoneName, string job) GetRouteMetadata(uint zoneId, Vector2 flag)
    {
        // 載入時就把區域名與職業記下來了 —— 這樣自訂新增的路線（內嵌資源裡根本沒有的那些）
        // 也拿得到正確的 metadata，而且不必為了兩個字串把所有內嵌資源重新解析一遍。
        if (TryGet(GetSnapshot().Meta, zoneId, flag, out var meta))
            return meta;

        // We need to parse the original resource to get the metadata
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(r => r.Contains("GatheringRoutes") && r.EndsWith(".yaml"))
            .ToList();

        foreach (var resourceName in resourceNames)
        {
            try
            {
                var route = LoadRouteFromResource(resourceName);
                if (route.ZoneId == zoneId && route.Flag == flag)
                {
                    return (route.ZoneName, route.Job);
                }
            }
            catch
            {
                // Skip invalid resources
            }
        }

        return ("Unknown", "BTN"); // Fallback
    }
}
