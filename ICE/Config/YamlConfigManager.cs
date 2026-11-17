using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public static class YamlConfig
{
    // One writer at a time, per file. Every save in the plugin funnels through
    // here, and nothing above this layer is guaranteed to serialise. Save() is now
    // debounced so this should rarely contend, but this is the layer that makes
    // "two callers, one file" safe rather than merely unlikely.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteLocks = new();

    private static SemaphoreSlim LockFor(string path) =>
        WriteLocks.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));

    // DisableAliases：省掉對整個物件圖的錨點預掃（存檔最貴的一段之一）。代價是同一個實例被引用兩次時
    // 會寫成兩份完整內容、讀回來變成兩個獨立實例；設定裡沒有任何地方依賴「兩處是同一個物件」。
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .DisableAliases()
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    // Keep synchronous Load since reading on startup is acceptable
    public static T Load<T>(string path) where T : new()
    {
        if (!File.Exists(path))
        {
            var defaultConfig = new T();
            // Use synchronous save for initial creation
            SaveSync(defaultConfig, path);
            return defaultConfig;
        }

        var yaml = File.ReadAllText(path);
        return Deserializer.Deserialize<T>(yaml) ?? new T();
    }

    /// <summary>只做序列化，一個位元組都不碰磁碟。</summary>
    /// <remarks>
    /// 🔴 它會走訪<b>整個設定物件圖</b> —— <c>MissionConfigs.MissionConfig</c> 是裸
    /// <c>Dictionary</c>、<c>GambaItemWeights</c> 之類是裸 <c>List</c>，而改動它們的是
    /// 遊戲主執行緒。所以<b>呼叫端</b>要負責在「沒有別人同時在改」的執行緒上呼叫它。
    /// 把這一步從 <see cref="SaveAsync"/> 裡切出來，就是為了讓呼叫端能自己決定在哪拍快照。
    /// </remarks>
    public static string Serialize<T>(T config) => Serializer.Serialize(config);

    /// <summary>只做寫檔：建目錄 ＋ per-path 閘門 ＋ 原子寫入（先寫 .tmp 再換名，寫到一半當掉不會留下半份檔）。</summary>
    /// <remarks>
    /// 📌 拿的是<b>已經序列化好的字串</b>，而字串是不可變的 —— 所以這一段可以安心留在
    /// 背景執行緒上，不會有任何東西在寫的途中被別人改掉。<br/>
    /// ⚠️ 寫入本身是同步的，呼叫端要自己確保不在遊戲主執行緒上呼叫。
    /// </remarks>
    public static async Task WriteAsync(string yaml, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var gate = LockFor(path);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Dalamud.Utility.FilesystemUtil.WriteAllTextSafe(path, yaml);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>序列化＋寫檔一次做完。</summary>
    /// <remarks>
    /// ⚠️ 序列化跑在<b>呼叫端的執行緒</b>上。設定物件圖可能被別的執行緒同時改動時
    /// <b>不要用這一支</b>，改成自己 <see cref="Serialize"/> 再 <see cref="WriteAsync"/>
    /// —— <c>MissionConfigs.SaveAsync</c> 就是這樣做的（它在主執行緒上拍深拷貝快照、背景序列化）。
    /// </remarks>
    public static async Task SaveAsync<T>(T config, string path)
        => await WriteAsync(Serialize(config), path).ConfigureAwait(false);

    public static void SaveSync<T>(T config, string path)
    {
        var yaml = Serializer.Serialize(config);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Blocking wait, but only against another writer of this same file, and
        // SaveSync is limited to first-time config creation, migrations and the
        // final flush on unload - it is not on any per-frame path.
        var gate = LockFor(path);
        gate.Wait();
        try
        {
            Dalamud.Utility.FilesystemUtil.WriteAllTextSafe(path, yaml);
        }
        finally
        {
            gate.Release();
        }
    }

    public static T LoadFromResource<T>(string resourceName) where T : new()
    {
        var assembly = Assembly.GetExecutingAssembly();

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            PluginLog.Warning($"Could not find embedded resource: {resourceName}");
            return new T();
        }

        using var reader = new StreamReader(stream);
        var yaml = reader.ReadToEnd();

        return Deserializer.Deserialize<T>(yaml) ?? new T();
    }
}