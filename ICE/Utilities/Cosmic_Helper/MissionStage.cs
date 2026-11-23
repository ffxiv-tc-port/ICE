using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace ICE.Utilities.Cosmic_Helper;

/// <summary>
/// 任務要等宇宙探索的建設推進到哪個階段，遊戲才會派出來。
/// </summary>
/// <remarks>
/// 資料鏈：<c>WKSMissionUnit.Unknown0</c> → <c>WKSMissionLotteryCond.Unknown0</c>（<c>WKSFunction</c> 列號）
/// → <c>WKSFunction.Unknown8～11</c>（一個星球一格，值是 <c>WKSDevGrade</c> 列號）。遊戲的 IsFunctionUnlocked
/// 拿 <c>WKSManager.DevGrade</c> 跟那一格比「≥」；派任務是伺服器做的，所以這條鏈是推論。
/// 契約：判不出來一律回 <see cref="State.Unknown"/>，不當成會出現；只在框架執行緒呼叫（快取沒有鎖）。
/// </remarks>
internal static class MissionStage
{
    internal enum State : byte
    {
        /// <summary>沒有建設階段門檻。</summary>
        NoGate,
        /// <summary>有門檻，已經達到。</summary>
        Reached,
        /// <summary>有門檻，還沒達到：遊戲不會派出這個任務。</summary>
        Locked,
        /// <summary>有門檻但讀不到能比的階段，或條件超出這裡能判斷的範圍。</summary>
        Unknown,
    }

    /// <param name="RequiredRow">門檻的 <c>WKSDevGrade</c> 列號；0＝沒有或條件判不了。</param>
    /// <param name="CurrentRow">拿來比的 <c>WKSDevGrade</c> 列號；0＝讀不到。</param>
    internal readonly record struct Result(State State, uint RequiredRow, uint CurrentRow);

    private const uint Undecidable = uint.MaxValue;

    private static Dictionary<uint, uint>? requiredRows;
    private static readonly Dictionary<uint, (uint Planet, byte Stage, string Name)> gradeInfo = new();

    // 全服建設只會往前推，所以離開星球前看到的階段可以當下限；沒到門檻時仍算不知道。
    private static readonly Dictionary<uint, uint> lastSeenByPlanet = new();

    internal static string LockedMarker => "【" + "Not offered yet".Loc() + "】";
    internal const string UnknownMarker = "【?】";

    internal static Result Check(uint missionId)
    {
        var required = RequiredRow(missionId);
        if (required == 0)
            return new(State.NoGate, 0, 0);
        if (required == Undecidable)
            return new(State.Unknown, 0, 0);

        var planet = Grade(required).Planet;
        var live = LiveRow();
        if (planet != 0 && live != 0 && Grade(live).Planet == planet)
            return new(live >= required ? State.Reached : State.Locked, required, live);

        if (planet != 0 && lastSeenByPlanet.TryGetValue(planet, out var seen))
            return new(seen >= required ? State.Reached : State.Unknown, required, seen);

        return new(State.Unknown, required, 0);
    }

    /// <summary>字串內嵌用（tooltip、清單）：還沒開放或判不出來時在名稱前加標記。</summary>
    internal static string NameWithMarker(uint missionId, string name) => Check(missionId).State switch
    {
        State.Locked => LockedMarker + name,
        State.Unknown => UnknownMarker + name,
        _ => name,
    };

    /// <summary>給 tooltip 的說明；沒有門檻時回空字串。</summary>
    internal static string Explain(Result result)
    {
        switch (result.State)
        {
            case State.Locked:
            {
                var need = Grade(result.RequiredRow);
                var now = Grade(result.CurrentRow);
                return "Needs construction stage ?? (??); this planet is at stage ?? (??). The game will not offer this mission before that."
                    .Loc(need.Stage, need.Name, now.Stage, now.Name);
            }
            case State.Unknown when result.RequiredRow == 0:
                return "This mission has an unlock condition ICE cannot evaluate, so it cannot tell whether the game offers it yet.".Loc();
            case State.Unknown:
            {
                var need = Grade(result.RequiredRow);
                return "Needs construction stage ?? (??). The current stage of that planet can only be read while you are there, so ICE cannot tell whether the game offers this mission yet."
                    .Loc(need.Stage, need.Name);
            }
            case State.Reached:
            {
                var need = Grade(result.RequiredRow);
                return "Unlocked at construction stage ?? (??), already reached.".Loc(need.Stage, need.Name);
            }
            default:
                return string.Empty;
        }
    }

    private static unsafe uint LiveRow()
    {
        // 位址解不出來時 Instance() 會擲例外；槽位在宇宙探索區外是 null。
        if (WKSManager.Addresses.Instance.Value == 0)
            return 0;
        var manager = WKSManager.Instance();
        if (manager == null)
            return 0;

        uint row = manager->DevGrade;
        var planet = row == 0 ? 0 : Grade(row).Planet;
        if (planet != 0 && (!lastSeenByPlanet.TryGetValue(planet, out var seen) || seen < row))
            lastSeenByPlanet[planet] = row;
        return planet == 0 ? 0 : row;
    }

    private static (uint Planet, byte Stage, string Name) Grade(uint row)
    {
        if (gradeInfo.TryGetValue(row, out var info))
            return info;

        // Unknown10＝WKSTerritoryInfo 列號（渴望灣 2、法恩娜 3），拿來分星球；Unknown6＝已竣工幾項。
        info = Svc.Data.GetExcelSheet<WKSDevGrade>().TryGetRow(row, out var grade)
            ? (grade.Unknown10, grade.Unknown6, grade.Stage.ExtractText())
            : (0u, (byte)0, "?");
        gradeInfo[row] = info;
        return info;
    }

    private static uint RequiredRow(uint missionId)
    {
        requiredRows ??= BuildRequiredRows();
        return requiredRows.TryGetValue(missionId, out var row) ? row : Undecidable;
    }

    private static Dictionary<uint, uint> BuildRequiredRows()
    {
        var conds = Svc.Data.GetExcelSheet<WKSMissionLotteryCond>();
        var functions = Svc.Data.GetExcelSheet<WKSFunction>();
        var result = new Dictionary<uint, uint>();
        foreach (var unit in Svc.Data.GetExcelSheet<WKSMissionUnit>())
            result[unit.RowId] = Required(unit.Unknown0, conds, functions);
        return result;
    }

    private static uint Required(byte condRow, ExcelSheet<WKSMissionLotteryCond> conds, ExcelSheet<WKSFunction> functions)
    {
        if (condRow == 0)
            return 0;
        if (!conds.TryGetRow(condRow, out var cond))
            return Undecidable;
        if (cond.Unknown0 == 0)
            return 0;
        if (!functions.TryGetRow(cond.Unknown0, out var fn))
            return Undecidable;

        // 只認「單一一格建設階段門檻」；帶任務或旗標條件的（Unknown0～7、12～19）判不了。
        if ((fn.Unknown0 | fn.Unknown1 | fn.Unknown2 | fn.Unknown3 | fn.Unknown4 | fn.Unknown5 | fn.Unknown6 | fn.Unknown7
             | fn.Unknown16 | fn.Unknown17 | fn.Unknown18 | fn.Unknown19) != 0
            || (fn.Unknown12 | fn.Unknown13 | fn.Unknown14 | fn.Unknown15) != 0)
            return Undecidable;

        uint found = 0;
        var count = 0;
        ReadOnlySpan<byte> grades = [fn.Unknown8, fn.Unknown9, fn.Unknown10, fn.Unknown11];
        foreach (var grade in grades)
        {
            if (grade == 0)
                continue;
            found = grade;
            count++;
        }
        return count == 1 ? found : Undecidable;
    }
}
