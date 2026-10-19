using Dalamud.Interface.Utility.Raii;

namespace ICE.Utilities.ImGuiTools;

public static partial class ImGui_Tools
{
    /// <summary>
    /// 給停用區段（<c>ImRaii.Disabled</c>）<b>裡面</b>的說明框用：區段把 Style.Alpha 乘上 DisabledAlpha，
    /// 不推回 1 說明框會跟著變淡。區段外的說明照舊用 <c>ImGui.SetTooltip</c>。
    /// </summary>
    public static void TooltipInDisabled(ImU8String text)
    {
        using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, 1f))
            ImGui.SetTooltip(text);
    }
}
