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
    // here, and nothing above this layer is guaranteed to serialise: the
    // MissionConfigs.Save() burst on 2026-07-29 produced 527 concurrent writes
    // to one path and lost every one of them to IOException. Save() is now
    // debounced so this should rarely contend, but this is the layer that makes
    // "two callers, one file" safe rather than merely unlikely.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteLocks = new();

    private static SemaphoreSlim LockFor(string path) =>
        WriteLocks.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
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

    public static async Task SaveAsync<T>(T config, string path)
    {
        var yaml = Serializer.Serialize(config);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var gate = LockFor(path);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.WriteAllTextAsync(path, yaml).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public static void SaveSync<T>(T config, string path)
    {
        var yaml = Serializer.Serialize(config);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Blocking wait, but only against another writer of this same file, and
        // SaveSync is limited to first-time config creation and migrations - it
        // is not on any per-frame path.
        var gate = LockFor(path);
        gate.Wait();
        try
        {
            File.WriteAllText(path, yaml);
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