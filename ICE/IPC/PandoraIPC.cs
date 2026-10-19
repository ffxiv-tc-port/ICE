using Dalamud.Plugin.Ipc;
using ECommons.EzIpcManager;

namespace ICE.IPC;

#nullable disable
public class PandoraIPC
{
    public const string Name = "PandorasBox";
    // 這條仍是國際服的庫,因為台服艦隊沒有 PandorasBox 的移植版可指。
    // 目前零呼叫;要接安裝按鈕之前必須先確認目標庫是台服版,否則會裝進 API15 的外掛。
    public const string Repo = "https://love.puni.sh/ment.json";

    // 與 EzIPC 包裝裡的訂閱者一起取得、指向同一個通道：HasFunction 為假，正是 GetFeatureEnabled 會擲 IpcNotReadyError 的情況。
    private readonly ICallGateSubscriber<string, bool?> featureEnabledGate;

    public PandoraIPC()
    {
        EzIPC.Init(this, Name, SafeWrapper.AnyException);
        featureEnabledGate = Svc.PluginInterface.GetIpcSubscriber<string, bool?>($"{Name}.{nameof(GetFeatureEnabled)}");
    }

    // 沒有提供端時 GetFeatureEnabled 會擲例外再被包裝吞成 null；先判就省掉每次的例外，回傳值相同。
    public bool? GetFeatureEnabledIfReady(string feature)
        => featureEnabledGate.HasFunction ? GetFeatureEnabled(feature) : null;

    public bool Installed => Utils.HasPlugin(Name);

    [EzIPC] public readonly Func<string, bool?> GetFeatureEnabled;
    [EzIPC] public readonly Func<string, string, bool?> GetConfigEnabled;
    [EzIPC] public readonly Action<string, bool?> SetFeatureEnabled;
    [EzIPC] public readonly Action<string, string, bool> SetConfigEnabled;
    [EzIPC] public readonly Action<string, int> PauseFeature;
}
