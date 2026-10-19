using Dalamud.Game;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using System.Runtime.InteropServices;

namespace ICE.Utilities.MechaOps;

/// <summary>
/// 機甲行動「駕駛申請書」兩個旗標 byte 在 <see cref="WKSMechaEventModule"/> 裡的位移。
/// CS 沒有具名欄位，所以從遊戲處理機甲事件資料的函式指令裡讀出來，不寫死。
/// </summary>
/// <remarks>
/// 契約：只在插件建構子的同步路徑呼叫一次 <see cref="Resolve"/>；推不出來或與 CS 版面對不上就擲例外，
/// 讓插件載入失敗——絕不帶著未驗證的位移去讀。
/// </remarks>
internal static unsafe class MechaPilotTicketLayout
{
    // cmp byte [rdx+1],1 ; mov byte [rbp+READY],1 ; sete al ; mov [rbp+HELD],al
    private const string Signature = "80 7A 01 01 C6 85 ?? ?? ?? ?? 01 0F 94 C0 88 85 ?? ?? ?? ??";
    private const int ReadyDisplacementAt = 6;
    private const int HeldDisplacementAt = 16;

    /// <summary>「伺服器資料已到」旗標 byte 的位移；-1＝尚未推出。</summary>
    public static int ReadyOffset { get; private set; } = -1;

    /// <summary>「持有駕駛申請書」旗標 byte 的位移；-1＝尚未推出。</summary>
    public static int HeldOffset { get; private set; } = -1;

    public static void Resolve(ISigScanner scanner)
    {
        if (!scanner.TryScanText(Signature, out var hit))
            throw new InvalidOperationException(
                "ICE：找不到機甲行動駕駛申請書旗標的遊戲碼（遊戲改版後版面可能變了），為避免讀錯位置，插件停止載入。");

        var ready = *(int*)(hit + ReadyDisplacementAt);
        var held = *(int*)(hit + HeldDisplacementAt);

        // 指紋：兩格都要落在 CS 版面裡 Flags 之後、結構結尾之前；不成立＝遊戲與 CS 對不上。
        int flagsEnd, moduleSize = sizeof(WKSMechaEventModule);
        var probe = (WKSMechaEventModule*)NativeMemory.AllocZeroed((nuint)moduleSize);
        try
        {
            flagsEnd = (int)((byte*)&probe->Flags - (byte*)probe) + sizeof(WKSEventModuleFlag);
        }
        finally
        {
            NativeMemory.Free(probe);
        }

        if (ready == held || !InTail(ready) || !InTail(held))
            throw new InvalidOperationException(
                $"ICE：機甲行動駕駛申請書旗標位移 0x{ready:X}／0x{held:X} 與 CS 版面"
                + $"（Flags 結尾 0x{flagsEnd:X}、結構大小 0x{moduleSize:X}）對不上，為避免讀錯位置，插件停止載入。");

        ReadyOffset = ready;
        HeldOffset = held;

        bool InTail(int offset) => offset >= flagsEnd && offset < moduleSize;
    }
}
