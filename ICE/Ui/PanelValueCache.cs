using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace ICE.Ui;

/// <summary>
/// 任務面板（<c>WKSMissionInfomation</c>）某一格字串的解析結果：那一格的原始位元組沒變就沿用上次的答案，
/// 不再每幀重新解析成 SeString。
/// </summary>
/// <remarks>
/// 契約：<c>read</c> 的答案只取決於第 <c>index</c> 格的型別與 null 結尾位元組（ECommons 的分數屬性都是）。
/// 守衛不過時照舊直接呼叫 <c>read</c>，行為與改前相同。只保存位元組複本，不保存任何原生指標。
/// 只在框架執行緒（Draw）使用，不加鎖。
/// </remarks>
internal sealed unsafe class PanelValueCache<T>(int index, Func<WKSMissionInfomation, T> read)
{
    private byte[]? bytes;
    private T value = default!;

    public T Get(WKSMissionInfomation panel)
    {
        var addon = panel.Base;
        if (addon->AtkValues == null || addon->AtkValuesCount <= index
            || !addon->AtkValues[index].IsString() || addon->AtkValues[index].String.Value == null)
        {
            bytes = null;
            return read(panel);
        }

        var raw = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(addon->AtkValues[index].String.Value);
        if (bytes != null && raw.SequenceEqual(bytes))
            return value;

        value = read(panel);
        bytes = raw.ToArray();
        return value;
    }
}
