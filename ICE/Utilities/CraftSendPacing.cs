namespace ICE.Utilities;

/// <summary>
/// 送 Artisan 製作指令前的等待：任務實例的第一次照設定的長等待，同一實例後續改短等待；
/// 短等待之後出現失敗訊號就本場退回長等待。
/// </summary>
/// <remarks>
/// 契約：只在框架執行緒讀寫；只放記憶體，插件重載回到「沒退回」。
/// 同一任務實例出現過任何失敗訊號後，該實例剩下的送出一律長等待 ⇒ 短等待之後的失敗必是該實例第一次失敗，
/// 退回一定發生在 CraftProgressGuard 的放棄／停止門檻（≥2 次）之前。
/// 本檔刻意不引用 Dalamud／遊戲／設定型別：離線探針直接連結這個檔編譯。
/// </remarks>
internal static class CraftSendPacing
{
    private static uint _mission;
    private static bool _sentThisMission;
    private static bool _failedThisMission;
    private static bool _lastSendShort;

    /// <summary>本場已退回長等待。</summary>
    internal static bool FellBack { get; private set; }

    internal static void NoteMissionStarted(uint missionId)
    {
        _mission = missionId;
        _sentThisMission = false;
        _failedThisMission = false;
        _lastSendShort = false;
    }

    /// <summary>這次送出前要等幾毫秒。</summary>
    internal static int DelayMs(uint currentMission, int longMs, bool shortEnabled, int shortMs, out bool isShort)
    {
        isShort = shortEnabled && !FellBack && currentMission != 0 && currentMission == _mission
                  && _sentThisMission && !_failedThisMission;
        return isShort ? shortMs : longMs;
    }

    internal static void NoteSent(uint currentMission, bool wasShort)
    {
        if (currentMission != _mission)
        {
            // 沒經過 NoteMissionStarted 就換了任務：當成新實例，這次不算短等待。
            NoteMissionStarted(currentMission);
            wasShort = false;
        }
        _sentThisMission = true;
        _lastSendShort = wasShort;
    }

    /// <returns>這次讓本場退回長等待（呼叫端寫一行記錄）。</returns>
    internal static bool NoteFailure()
    {
        _failedThisMission = true;
        if (!_lastSendShort || FellBack)
            return false;
        FellBack = true;
        return true;
    }

    /// <summary>測試用：回到剛載入的狀態。</summary>
    internal static void ResetAll()
    {
        NoteMissionStarted(0);
        FellBack = false;
    }
}
