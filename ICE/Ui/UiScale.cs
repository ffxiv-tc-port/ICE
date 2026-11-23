using System.Numerics;

namespace ICE.Ui;

/// <summary>
/// 寫死的設計像素依目前字級換算：值 × <c>ImGui.GetFontSize()</c> ÷ Dalamud 出廠字級。
/// <c>GetFontSize()</c> 已含全域縮放，所以不再乘 GlobalScale；出廠字級時 <c>Px(x)</c>＝x×GlobalScale。
/// 只在繪製期間呼叫（量的是當下推入的字型）；負值（-1＝填滿）與 0 不要傳進來。
/// </summary>
public static class UiScale
{
    // Dalamud 出廠字級（internal InterfaceManager.DefaultFontSizePx）；不用 UiBuilder.DefaultFontSizePx：那是使用者改過的字級，當分母會抵銷放大。
    private const float FactoryFontSizePx = 12f * 4f / 3f;

    public static float Px(float designPixels) => designPixels * ImGui.GetFontSize() / FactoryFontSizePx;

    public static Vector2 Px(float designX, float designY) => new(Px(designX), Px(designY));
}
