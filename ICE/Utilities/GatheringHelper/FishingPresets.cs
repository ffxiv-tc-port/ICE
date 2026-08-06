using ICE.Utilities;
using ICE.Utilities.Cosmic;
using ICE.Utilities.Cosmic_Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Utilities.GatheringHelper;

public static partial class GatheringUtil
{
    public class FishingTools
    {
        public List<string> FishingPreset = new List<string>();
        public int AmountRequired = 0;
        public bool UniqueFish = false;
        public Dictionary<string, List<uint>> Baits = new();
        public Dictionary<string, List<uint>> RequiredFish = new();
    }

    /// <summary>
    /// 用資料表補上還沒手寫的 <see cref="FishingTools.AmountRequired"/>。
    /// 在 <c>DictionaryCreation()</c> 之後呼叫一次。
    /// </summary>
    /// <remarks>
    /// 來源鏈：<c>WKSMissionUnit[任務].MissionToDo[0]</c> → <c>WKSMissionToDo[todo].Unknown17</c>。
    /// <br/><br/>
    /// 🔑 <b>離線對 exd-tc/7.20 校準過</b>：全表 1073 個任務裡 <c>Unknown17 &gt; 0</c> 的只有
    /// <b>5 列，而且全部是 MissionType == 8</b>（＝數量交付型）。這 5 列對到的任務是
    /// 451／470／474／480／493；前四個我們本來就有手寫值 5／13／18／18，
    /// <b>四個全部逐字相符、零例外</b>。<br/>
    /// ⚠️ 反過來說 <c>Unknown17 == 0</c> 的意思是<b>「這張表沒有這個資訊」而不是「不需要數量」</b>：
    /// 我們手寫值 &gt; 0 的 28 個任務（生態調查那一類）在表上都是 0。
    /// 所以這裡<b>只補、不覆蓋</b>——手寫值永遠優先。
    /// <br/><br/>
    /// 🔴 <b>還有一個前置：<see cref="FishingTools.RequiredFish"/> 不能是空的。</b>
    /// <c>Task_CheckScore.MinRequirementsMet</c> 是靠列舉 RequiredFish 去數身上有幾條魚的；
    /// RequiredFish 空的時候數出來恆為 0，這時若把 AmountRequired 設成 18，
    /// <c>0 &gt;= 18</c> 永遠不成立 ⇒ <b>任務永遠不會被交出去，直接卡死</b>。
    /// <br/><br/>
    /// 📌 <b>2026-08-06 更新：這個函式不再是 no-op。</b>493 的 <c>RequiredFish</c> 已經補上
    /// （目標魚＝深月海龍 45912，來源見該筆 preset 的註解），所以啟動時它會被補成表上的
    /// <c>Unknown17 == 18</c>，交件判定改走「數到 18 條」的數量語意，不再落到
    /// <c>TimeGradedFishRequirementsMet</c> 的「沒有任何依據」那一條。
    /// 這正是當初把補值邏輯留在這裡的目的：數量的真值來源只有資料表一份，不手寫第二份。
    /// <br/><br/>
    /// ⚠️ 若 18 這個數字對 493（時限僅 240 秒）其實偏高，行為會退回**與補值前完全相同**的
    /// 「釣到逾時再放棄」，不會卡死也不會誤交件——因為逾時路徑本來就會重跑一次
    /// <c>MinRequirementsMet</c> 再決定交件或放棄。
    /// </remarks>
    public static void BackfillAmountRequiredFromSheet()
    {
        var unitSheet = ExcelHelper.MoonMissionSheet;
        var toDoSheet = ExcelHelper.ToDoSheet;
        if (unitSheet == null || toDoSheet == null)
        {
            IceLogging.Info("讀不到 WKSMissionUnit／WKSMissionToDo，跳過 AmountRequired 的資料表補值。", "[FishingPresets]");
            return;
        }

        var filled = 0;
        var skippedNoRequiredFish = new List<uint>();

        foreach (var (missionId, tools) in FishingPreset)
        {
            if (tools.AmountRequired != 0)
                continue; // 手寫值優先，絕不覆蓋

            if (!unitSheet.TryGetRow(missionId, out var unit))
                continue;

            var toDoId = unit.MissionToDo[0].RowId;
            if (toDoId == 0 || !toDoSheet.TryGetRow(toDoId, out var toDo))
                continue;

            int amount = toDo.Unknown17;
            if (amount <= 0)
                continue;

            if (tools.RequiredFish.Count == 0)
            {
                // 見上面的 remarks：填了會讓這個任務永遠交不出去。
                skippedNoRequiredFish.Add(missionId);
                continue;
            }

            tools.AmountRequired = amount;
            filled++;
            IceLogging.Info($"任務 {missionId} 的 AmountRequired 由資料表補為 {amount}（WKSMissionToDo {toDoId}）。", "[FishingPresets]");
        }

        if (skippedNoRequiredFish.Count > 0)
        {
            IceLogging.Info(
                $"這些任務資料表上有數量需求但 RequiredFish 是空的，維持 AmountRequired = 0 走分數保底："
                + string.Join("、", skippedNoRequiredFish)
                + "。（填了數量卻沒有魚種清單會讓任務永遠交不出去。）", "[FishingPresets]");
        }

        IceLogging.Info($"AmountRequired 資料表補值完成：補了 {filled} 筆、因缺 RequiredFish 跳過 {skippedNoRequiredFish.Count} 筆。", "[FishingPresets]");

        ReportTimeGradedTurninCriteria();
    }

    /// <summary>
    /// 把每個<b>時間型</b>釣魚任務的「交件依據會落在哪一層」寫進 log。
    /// </summary>
    /// <remarks>
    /// 📌 <b>Information 級、啟動時只跑一次</b>——使用者跑 LogLevel 2，Debug／Verbose 收不到，
    /// 而這是「486／493 到底修好了沒」唯一能離線回答的證據。
    /// <br/><br/>
    /// 對應 <c>Task_CheckScore.TimeGradedFishRequirementsMet</c> 的三層優先序：<br/>
    /// ① 資料表 <c>Gathering_Min</c>（來自 <c>WKSMissionToDo.RequiredItem[]</c>）<br/>
    /// ② preset 的 <c>RequiredFish</c>「至少一條目標魚」<br/>
    /// ③ 兩者皆無 ⇒ 不宣稱達標，會一路釣到逾時再放棄。<br/>
    /// 另外 <c>AmountRequired != 0</c> 的任務根本不會走進那個函式，直接走數量語意，
    /// 所以也一併標出來，免得看 log 的人以為它落在第 ③ 層。
    /// <br/><br/>
    /// ⚠️ 只列時間型（<c>IsTimeGraded</c>）：評價型的交件依據是分數，不走這條優先序。<br/>
    /// ⚠️ 也<b>跳過 <see cref="UnsupportedMissions.Ids"/> 上的任務</b>：494 是時間型、在
    /// <c>FishingPreset</c> 裡有一筆空的 <c>FishingTools</c>、而且確實三層都沒有依據，
    /// 但它已經因為「上游只給 AHFOLDER_ 整包匯出」被停用，ICE 根本不會接。
    /// 不濾掉的話每次啟動都會對一個永遠不會發生的情境報警。
    /// </remarks>
    private static void ReportTimeGradedTurninCriteria()
    {
        var noCriteria = new List<uint>();
        var lines = new List<string>();

        foreach (var (missionId, tools) in FishingPreset)
        {
            if (!CosmicHelper.SheetMissionDict.TryGetValue(missionId, out var entry))
                continue; // 空名佔位列／非本服任務，DictionaryCreation 就沒收進來
            if (!entry.IsTimeGraded)
                continue;
            if (UnsupportedMissions.Ids.Contains(missionId))
                continue; // 停用中的任務不會被接，報警只會製造雜訊

            string basis;
            if (tools.AmountRequired != 0)
                basis = $"數量語意（AmountRequired = {tools.AmountRequired}"
                      + $"{(tools.UniqueFish ? "，計不重複魚種" : "，計總條數")}"
                      + $"、魚種 {tools.RequiredFish.Count} 種）";
            else if (entry.Gathering_Min.Count > 0)
                basis = $"① 資料表 RequiredItem（{entry.Gathering_Min.Count} 項）";
            else if (tools.RequiredFish.Count > 0)
                basis = $"② preset RequiredFish 至少一條（{tools.RequiredFish.Count} 種）";
            else
            {
                basis = "③ 無任何依據 ⇒ 會釣到逾時才放棄";
                noCriteria.Add(missionId);
            }

            lines.Add($"{missionId}＝{basis}");
        }

        IceLogging.Info(
            $"時間型釣魚任務的交件依據（共 {lines.Count} 個）：" + string.Join("｜", lines),
            "[FishingPresets]");

        if (noCriteria.Count > 0)
            IceLogging.Info(
                "⚠️ 這些時間型釣魚任務仍然沒有任何交件依據，接到的話會白白釣到逾時："
                + string.Join("、", noCriteria)
                + "。（要修：補該筆 preset 的 RequiredFish，或確認資料表上有 RequiredItem[]／Unknown17。）",
                "[FishingPresets]");
        else
            IceLogging.Info("✅ 所有時間型釣魚任務都有可用的交件依據。", "[FishingPresets]");
    }

    public static Dictionary<uint, FishingTools> FishingPreset = new()
    {
        // D Rank
        // Export for Mission [451] -  Lunch Emergency
        [451] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1WW2/bOgz+K4WebcD321uWk3YFsraoO+yh2INi07EQR/IkuV1Okf9+INtK7FzadSiGPZw3gSI/fqRIii9o0kg2xUKKabFEyQuaUbyoYFJVKJG8AQOpyzmhsL/M9dV1jhInig10xwnjRG5QYhvoWsx+ZlWTQ74XK/1th/WFsaxUYO3BUacWJ4gMdFU/lBxEyaocJbZljZBfh24x4nBkYb1JZlo26zOBebblvcFIg7CqgkzqSDzbsodqztssGM8JrjRAYHsjAK9XuySinG1ADBz5Bwx9f8Qw0DnHK0hLUshPmLQ8lUBoQSpxthIo8fssBtEx7hA17lHvsCRAMxjwCQ7tgnHGHG3Kyb8wxbKrBO310No5yLfbWz+UuCJ4JS7xE+MKYCTQ4bjGWH4PGXsCjhJbJUn79EYedMI+keUVXreRTeiyAi40qnrNHCVuaHlHdEdQ0XZroNlPyfGos3Y1pjL/wNJnXF9T2RBJGL3ChOp8mLaB5g2HLyAEXgJKEDLQTcsJ3TAKqEfY1IASlZgTeHMm5G/j3XEQcJohMtGZ+85je7/nk9aQSY6racM5UPlBUR6gflisJ9keRXzSe6t1yXgGbVs941o/divMlbRtFD/2QqOvrFSyWnU2octUQt2O0H2UffVN+McEN4Q7jumZrBeYyEtSVeKV+/uGittGI3yl5EcDihkKQ7DBCrBpFzgyvSKMTLywPdPGiygMi9CNXAdtDTQnQt4WiqVAyeNLy1elYDdIuvyci3LKxJrJ8uKuqbGCu2F8javPjK0UgJ5K3wCv9m2nbgWM3qQXdYny7FCNNW2cSs7o8j3mljswn8MSaI755t0I/7BmUe24jzScIN4p7PmdVRlxOKH1wEl9zlPoO+5O5ZyvkdIr3no9VeeTQgKf4mZZyjlZq6/I7i4OG6BdOhre/XXqMBjq3fj14+Pf+ZWPVm0IekjpSrmHHw3hkKcSy0b9f2oFOSyfX6uSXy6G/9/8j765HllT1lA5tDPQLa02XwV8K4HesHYXnTxhUqle1Q06GG2F5dt+DK6Z+Vlhei7OzYUV+Wbgge1CkPlBZKHtdz3b+iX3cSfoxtvjCxrOuXeP8rP5vM6BSpLhSiVRwXcKk7WKe6jWDtfDxcUdb42R8tTwAmeQVmpc7aay/8aC5m8N9Nfs92mNORRN9RnTXLNQa5t/iseoUPqEuaMt3hptH7//hM+4VpLWUfsowy+2/1PVsRPv1U61zKBAXScLncCLzdh3semFhWXGGc5Mv7AdJw6K3HGitkA73J7io+fb3y/mDc3Ki9ka+BJothl/6rGH3cz3MhMWbmx6ee6YkYVD0/Icxw78RQAFRtv/AHsSQGtJDgAA",
            },
            AmountRequired = 5,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Astacus Lamentorum"] = new List<uint>()
                {
                    45693,
                    45705,
                    45715,
                    45727,
                    45744,
                    45826,
                    45847,
                },
                ["Lunar Blue Guppy"] = new List<uint>()
                {
                    45695,
                },
                ["Lunar Tilapia"] = new List<uint>()
                {
                    45694,
                },
            },
        },
        // Export for Mission [452] -  Large Aquatic Specimens
        [452] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACs1VyW7jMAz9lYJnG7BjSV5uaZAWBdJOMemcih4Um46FOpYryV2myL8P5NiTOMsEKHqYm0CRj49PJPUJ48bICddGT/IlJJ8wrfiixHFZQmJUgw7Yy5mocHuZ9Vc3GSSjKHbgXgmphPmAxHfgRk/f07LJMNuarf96g3UrZVpYsPYwsqcWh0UOXNcPhUJdyDKDxPe8AfK/oVuMOBxEeGfJTIpm1TMgvkfOUOijZFlianYC/V230fm0UmWClyck9UeMDUQlXdiV0MX0A/VOYrrHmNIBY9aLzp9xXojcXHLR8rYG3RvmhqfPGhLayciiQ9xd1LhDvedGYJXiDh+2H8eGCo76UCV+44SbTSsc6ysWHYCN9p4j6MAeCl4K/qyv+KtUFm9g6KsLnKH9J6byFRUkvtXsBAUySNjLeSmW13zV1j2uliUq3Sexb59BEoQeOWA/gIrWawem70bxweD9JWDf5UHO33h9U5lGGCGray6qXmrXd2DWKLxFrfkSIQFw4K7lBHeyQugQPmqExOp0BG8mtfky3r1CjccZggsn7jcZ2/stn3mNqVG8nDRKYWW+qco91G+r9Sjbg4qPZm+9rqRKsR26N173j90aM2ttx4jGJHS6zpobWdu5F9VybrBuN+y2yq77xup7ituFa9n+qsRLgxYXqMc9jj5zfQyIS7IodKOYEjeOWB57NOIhD2DtwExo8yO3OTQkj0+9oVv7W4MtCpLHT9gcug1CWcxOF3CLJa/SQpaCD+qwi9kKNc4NqglvloWZiZXddPbTyLAyIuWlnVybaOMwXsmmGri1yu9PbTBcqJHN1Kicpzgv7QMeXV6ExvTM7qJrB/6br3DbUF9uo/kbr61lYlVtBd1trK6d7HFj3roda/CdtguoH1MSLVxklLskCIgbpTFzozjnfkTRj1gK66c+XUfxkdDR08WMqyVejF8abkR6YWdSrLDSw7728yBE3ycuC3zqkiyP3cWChG4cxKM4CkLEjMH6D56DAd8pCQAA",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [453] -  Western Water Inspection
        [453] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACtVXWW/bOBD+KwGfJUAHRR1vrtdJDbjZoMoiD0EfKGlkE5FFlaSaZgP/9wV12JKP2CmCRfsmDznffDOcy69oUis+pVLJab5E0SualTQpYFIUKFKiBgPpwwUrYXeY9UfzDEVOEBroTjAumHpBkW2guZz9TIs6g2wn1vc3LdYXztOVBms+HP3V4JDAQDfV/UqAXPEiQ5FtWSPkt6EbjNAfaVhnyUxX9bpngG0Ln6HQa/GigFSdiAi2LXuo5ZxnwUXGaHECz3YIGcUYd2rXTK5mLyAHDnh7DnjeyAHSvwF9gnjFcvWJssYNLZC9IFY0fZIo8rqokuAQd4gadqh3VDEoUxjwIft6ZBxQp1cV7F+YUtVmxrE0I8EBmLP3Om4Hdr+iBaNP8pr+4ELjjQS9d64xln+FlP8AgSJbx+wEBTwy2IfzE1ve0HXj96RcFiBkb0S/fYYi17fwAfsRVLDZGGj2Uwk6qsMtAf0u9zx+ptW8VDVTjJc3lJV9qE3bQItawBeQki4BRQgZ6LbhhG55CahDeKkARTpOR/AWXKpfxrsTIOE4Q2SiE+etxeZ8xyeuIFWCFtNaCCjVB3m5h/phvh5le+DxUevNrWsuUmiK7plW/WM3wkxLmzLyQuwbXWbFile67lm5jBVUTcPdedll30R8jHNDuIbtPyX7XoPGRbYT+mEOxIQMchNnATETQqmZ5zS1Ej/zIAjRxkALJtXfubYhUfT42ljTDmybROvdKY5TLtdcra7u6opquFsu1rT4zPmTBug7zgPQ5reWS1Db2slpIaEv5u5wGOhO1HqPbV93sh4zVoKXg1I8r265A/UFLKHMqHj5AF4N8F+8Topzno4UHRJu9XbenLxyCeMjyveCVe/k5XuOu9U8xWx06f3cOnVdL5NcgZjSerlSC7bWA89uD/YLqVl1atFOVP0xmBVtG/fCwxXhjfGu95K+2fU5+xW+10xAFiuqaj1l9eJzYSJflq+XKb87W/+HpLxY83fJ1Xfo/rkpPOj6JHQTF/LAzP3QM7FFAjOAIDHtxA3tAFPL9gFtvvVtv9v1H7eCtvM/vqLxCPD1xnx6BCS0UFczKEbDyn4rNPMMSsVSWuh4aDvthcma1+XoWjOA9lczd7w1B9pSLXKaQlzoJr2dXN6ZjdTbGOi3+b+zWxN+eTmIn2mlJVMdxiaCw3WhWxL0ZyveXTuWqoO0wiE4TmgFpuc7nolzTM0QAs90KUlyy3bClJAmrVrcjuIj9txvVw8gFYjy6oEqEFfzUupdi/FyvK5kWZBjz8VmRiA0sZdgM0l8MJ2U4twlYDkBQZv/APBzqN0PDwAA",
            },
            AmountRequired = 2,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Cobalt Eel"] = new List<uint>()
                {
                    45701,
                },
            },
        },
        // Export for Mission [454] -  Aquatic Foodstuffs
        [454] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1WS2+jMBD+K5XPIEEwz1uaTbqV0m60SbWHqgcHhmCFYGqbttkq/31lwEnIo49Vtae9wcz4m2/G489+Rf1KsgERUgzSBYpe0bAg8xz6eY4iySswkHKOaQE7Z6Jd1wmKekFooAmnjFO5RpFtoGsxfInzKoFkZ1bxmwbrhrE4U2D1R0991TheYKCrcpZxEBnLExTZltVBfhu6xgj9zgrrXTKDrFqdKQzbFn6HkQZheQ6x1JVg27L3w3rvs2A8oSTXAJ6NOwC4DRtRkQ3XIPYSuQcMXbfD0NM9J0uYZjSVl4TWPJVBaMNUkngpUOS2XfSCY9x91LBFnRBJoYhhj493uM7rdqynl3L6GwZENpNwaqy84Aisd9B+pwWbZSSnZClG5Ilxhdcx6Ooco2v/CTF7Ao4iW/XsDAXcSajbeUkXV2RV190vFjlwoZOovU5Q5PgWPmLfgQo2GwMNXyQnnXO3JaD2Zcamz6S8LmRFJWXFFaGFbrVpG2hccbgBIcgCUISQgW5rTuiWFYBahHUJKFJ9OoE3ZkL+Nd6Eg4DTDJGJzvibjLV/x2daQiw5yQcV51DIL6ryAPXLaj3J9qjik9nrqBHjMdSH7pmUerNrY6Ks9TFyQ+wb7WRNJSvVuafFYiqhrAV2V2U7fX3+NcXtw9Vs7wr6WIHCRcTziOv5junPA8fEcWiZ8zl4ZooDSKzQn3tOD20MNKZC/khVDoGi+9c6mypgKxJNdec4DphYMZldTKqSKLhbxlck/87YUgFoxfkFZLk7NMoroNPR1tSUiW1fSZZePJWcFYvPLLecveVjWECREL7+NMI3Vs3zLfdORM8LtwE7fmdDOhxORM04Lc9l8t2esw05l6sT9Ea2Nk5NaT+VwAekWmRyTFfqmrEbx+H41g+Kijf3mPrYU+hGPN3w+OZ94xJVt7+WGD0pP+GxohySqSSyUnebel4cjs/HpuTDw/B/z//pnt/txClxHYC565lJgmMT23FszsMYTIKDNPEtwEGI0eZBq1P7BL3fGhqBUv+NHLZidI9d/HDRf6yIpPHFiLFEyCpNRVcZXce3wtDxTPDBMbELthmAG5i+5Qahb4ep56Ro8wdsUj8tagsAAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [455] -  Weeping Pool Ecological Survey
        [455] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1Xy27bOhD9FYNrERBlinrsHN8kDeCkQZ2LLoIuKGlkE5FFl6TSpoH//YJ62JZjJXGRRYHenTCcOTwzPByOntGkMnLKtdHTfIHiZ3Re8qSASVGg2KgKHGQXZ6KE3WLWLV1lKPbCyEG3SkglzBOKiYOu9PnPtKgyyHZm679psK6lTJcWrP7w7FeNw0IHXa7vlgr0UhYZionr9pBfh64xoqAX4b5JZrqsVgOJUeLSNxh1ILIoIDXDOGQ/ynublFSZ4MUAHiO0h0fbqAuhl+dPoLuCUuL6B/x9v8efdSfCH2C+FLk546LOwhp0Z5gbnj5oFPttjVn4EncfNWpRb7kRUKawx4cdxrF+Pb0uVIlfMOWm0ckx0bHwBZh3cDjjFuxuyQvBH/QFf5TK4vUMXXZjp2//Aql8BIViYms2QIH2NuzKeSYWl3xV5z0pFwUo3W1ijz5D8Thw6Qv2Pahws3HQ+U+jeO9WbgnYc7mT8x98fVWaShghy0suyq7UmDhoVim4Bq35AlCMkINuak7oRpaAWoSnNaDY1ukI3kxq89t4two0HGeIMBpYb3as13d85mtIjeLFtFIKSvNBWR6gfliuR9m+yPjo7rXXhVQp1JfuB193h10bM2utr5Ef0dBplTU3cm3vvSgXcwPruv3usmzVN1Efk9w+XM3231J8r8DiIkKTPKeMY0qDANM0yHFCPMDEJVEYuAxcH9DGQTOhzefc7qFRfP9c72YT2DaJJrshjtdSlqMzXhQW60aqFS8+Sflgo7t28xX4w+7G2FUNvXK2piZHSgLbr7rguVGyXJwS7o73wmewgDLj6ulkhH9klRRb7j0Pj0Vbhx2/1mXbF3Je6EPs/cgetfcH3ymxHuIV+N5463Iis17s6dzacCv/SW5ATXm1WJqZWNn3izQLh/einmMq1TyQ9mOv9Tdd2Y9ePvivPNZ26Oh6V6fCL/C9EgqyueGmso+mnWoOpfmeFAeF+b7gYVmeEH9UlO9W3J+h1RNk+HdKeK+Jh6HnjnkYYsgJYErzAEfczTELKAkzjwU0jNDmW9fF20H+fmtoGvn9M+p3dBaNhzv6RBueVno04ysojVTVqvcGkddKdJVBaUTKC1sXu1/jMFnJquy51e/K4cQ17g/Dod2pUjlPYV7YZn18mPcj/42509846I/5x9kNA789Athga5naqtYF3R8K2lHAfjbmndsxBe+pzXU9PwpSin02DjClSYhDyD3sURZ5bsgZhDnaOIdqCtxgOIFZVXI1+gQrIbNK/6+kv0NJOaM0zL0AJ4RRTHnm4SjPCQ48whI3JB7Jk7pvNbgtxXvq+99GXwHWolyMbqUsRuepLOTC6mA0r9QjPPWHXOp6fsL8CKchpJj6xMVJ4CeYkjQgLE8SD1K0+Q9F3lgEUxEAAA==",
            },
            AmountRequired = 2,
            UniqueFish = true,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Astacus Lamentorum"] = new List<uint>()
                {
                    45693,
                    45705,
                    45715,
                    45727,
                    45744,
                    45826,
                    45847,
                },
                ["Lunar Hemiodus"] = new List<uint>()
                {
                    45707,
                },
                ["Lunar Peacock Bass"] = new List<uint>()
                {
                    45706,
                },
            },
        },
        // Export for Mission [456] -  Assorted Alchemical Materials
        [456] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACq2US0/jMBDHvwqacyIlqfO8haogpMKiLXtCHAZn0liEOGs7PBb1u6+cJrQpZZFW3JwZz2/+83DeIO+MnKM2el6uIXuDRYP3NeV1DZlRHTlgnUvR0M5ZjK6LArIgSR24VkIqYV4h8x240IsXXncFFTuzvb/Zsi6l5JWF9YfAnnpOlDhw3t5UinQl6wIy3/Mm5H+je0YaTyK8L8XMq+5xVMB8j30hYYySdU3c7AX6+9eCr9NKVQisP2mpH0TRpKlsCDsTulq8kt5LHB4oDsOJ4mhsOj7QqhKlOUXR67YGPRpWBvmDhiwc2hglH7n71HSgXqMR1PDPVoP5XnSIiaYNDUaSEn9ojma7GaOIw+jgYByzIfqmwlrggz7DJ6ksYGIYq5s5U/tP4vKJFGS+7dmx1Y4SuxF7Ccd2nor1OT72defNuialxyR29gVks9hjH9RPUMlm48DixSicPLx3AXYuN3L1jO1FYzphhGzOUTRje1zfgWWn6JK0xjVBBuDAVa8JrmRDMBBeW4LM9ukIbym1+W/etSJNxxWCC5/4txl7/07PqiVuFNbzTilqzDdVeUD9tlqPqv1Q8dHs/a0zqTj1j+4Z23HYvbGw1v7dhClLnGGzVka29t2LZr0y1PZ/2F2Vw/bl6nuKy/dwvdpfjfjdkeVCQkGByGK3LIi7zPfIRc65W5TePc4CVnpeABsHlkKbH6XNoSG7vRsNw29/Z7BFbb+3CgaNtyyM7k5yraUyVJzkNa/oUXCsTy7RkBJY66kuxLjgjKHre7PQZUFMbpqWsRvxMKYojsKUlbD5C4vnvqjpBgAA",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },

        // C Rank

        // Export for Mission [457] -  Big Fish
        [457] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACq2VTW+jMBCG/0o1Z5AwAfNxS6O0qpR2q033VPXgmCFYpZi1TT+2yn9f2YEmpMlWWvUGM/Yz7zsewztMOyNnTBs9K9eQv8O8Yasap3UNuVEdemCTC9HgLlkMqasC8jDNPLhVQiph3iAnHlzp+SuvuwKLXdiu32xZ11LyysLcQ2ifHIemHly2d5VCXcm6gJwEwYj8b7RjZMloR/ClmFnVPZ0wFpEg+kLRAJF1jdwMTiISkP1l4dcqpCoEq08IISGlox5H/bYLoav5G+q9wvGB4jgeKabDGbBHXFaiNOdMON02oIfA0jD+qCGP+67S9DN3n5r11FtmBDb81KREJKCHGDpuaDiQlPiDM2a2gzKIONwdHhzHpN99V7FasEd9wZ6lsoBRYHA38cbxn8jlMyrIie3ZsUmnqZ2IvYJDO8/F+pI9Od/TZl2j0kMRe/YF5JMkiD6pH6HSzcaD+atRbHQPPwTYc7mTyxfWXjWmE0bI5pKJZmiPTzxYdAqvUWu2RsgBPLhxmuBGNgg94a1FyG2fjvAWUpv/5t0q1HhcIfhwIr+t6PI7PcsWuVGsnnVKYWO+yeUB9du8HlX7yfHR6m7VhVQc3aV7Ye1w2C5Y2Ki7N3EWZV4/WUsjW3vvRbNeGmzdB3fnsp++qfoec9M9nFP7qxG/O7RcyFZpUWLMfeQ48SNGAz9LS+6XhFPELKF0xWDjwUJo86O0NTTk9w9DoP8L7ALW1PZ9q6DXeB/FycPZuVifuQUjCUmxymJWcp+SlPlREJd+ltGJzwMSJiQiCeMFbP4C1oBtKeMGAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [458] -  Environmental Inspection
        [458] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACq1Uy27bMBD8lWDPEiDJ1PPmGE5gwEmDOj0FOdDUxiKikCpJ5dHC/16QFmvLcRqgyI3a5c7OzC71G6a9kTOqjZ49bKD6DXNB1y1O2xYqo3oMwCaXXOA+WfvUooYqKcoAbhSXips3qOIAFnr+ytq+xnoftve3O6wrKVljwdwhsSeHkxUBXHa3jULdyLaGKo6iEfK/oR1GmY8qok/JzJr+6QNhJI7IJ4w8iGxbZMYrIXEUH15LPmchVc1p+wGROMmykcdkKLvgupm/oT5onB4xTtMR48zPgD7iquEP5pxyx9sGtA+sDGWPGqp0cDUr3uMeopYD6g01HAXDAz7ZcV02djDxpYr/whk1u83wXY+rkyP/J0P1bUNbTh/1BX2WygKMAl7OJBjHvyOTz6igiq1Jp1Y7K+wKHDT0/p3zzSV9ckKnYtOi0r6JHXYN1SSPyDv2I6hiuw1g/moUHT28vwTsIG7l6oV2C2F6brgUl5QLb08YB7DsFV6h1nSDUAEEcO04wbUUCAPCW4dQWZ9O4C2lNv+Nd6NQ42mGEMIH+V1Hl9/zWXXIjKLtrFcKhfkilUeoX6b1JNt3ik92d7cupGLoXtkL7fywXbC2Ufdu0pKUwbBZKyM7+9C52KwMdu4Pu1c5bN9UfY246QGcY/tD8J89WlyICMNJktchYZMiJDUpwzWp8zBLWUlKlqU5EtgGsOTafHuwPTRUd/c+MPz29wErave9YzBwvCNpcX82F89cSfGEwtD2bCG09ZNLcUQpy5KkjpIwTdk6JJTmIS3qJIyifJ2QtCjjNIXtH76wCNnkBgAA",
            },
            AmountRequired = 2,
            UniqueFish = true,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Lunar Eel"] = new List<uint>()
                {
                    45718,
                },
                ["Weepingeye"] = new List<uint>()
                {
                    45717,
                },
            },
        },
        // Export for Mission [459] -  Polarized Dyes
        [459] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACs1VTW/bMAz9KwXPNuBvW76lWVoUSLtg7k5FD4pMx0IdK5PkfqzIfx/k2IudJgtQ9LCbQJGPj08k9Q6TRospVVpNixWk7zCr6bLCSVVBqmWDFpjLOa9xf5n3Vzc5pF5CLFhILiTXb5C6Ftyo2Surmhzzvdn4b3dYt0Kw0oC1B8+cWpwoseB6c19KVKWockhdxxkh/xu6xSDxKMI5S2ZaNuueQeA6wRkKfZSoKmR6EOgO3bzzaYXMOa1OSOp6UTQSNejCrrgqZ2+oBonDA8ZhOGIc9aLTJ8xKXuhLylvexqB6Q6Ype1KQhp2MUfIRd4hKOtQF1Rxrdqo1AteJDmGisaBejyT5b5xSveuMnsRhtHfwHH4XfV/SitMndUWfhTQAI0NfnW+N7T+QiWeUkLpGs2OtHSWmIwYJezkv+eqartu6J/WqQqn6JObtc0j92Ak+sB9BJdutBbNXLelo8P4SMO9yL7IXurmpdcM1F/U15XUvj+1aMG8k3qJSdIWQAlhw13KCO1EjdAhvG4TU6HQEby6U/jTeQqLC4wzBhhP3u4zt/Z5PtkGmJa2mjZRY6y+q8gD1y2o9yvZDxUezt15XQjJsh+6FbvrHbo25sbZzE5KAWF1nZVpszNzzepVp3LQbdl9l130T+TXFDeFatj9r/qtBgwtB7niOF3u27zvEDkhR2Mug8O04cl0nIiSkSQhbC+Zc6e+FyaEgfXjsDd3a3xtMUZA+vMPu0G20MPa80wXMm5rKiwnqEmUlGjWuxqxnI9ek0CintFmVes7XZt+ZryPHWnNGKzO/Jt3OYbIWTT1wO7bGQnI4yv54yyYmcSMLyjCrzKv2xZDwzAYLtxb8Nx/ivq0+3UzZC90Yy9So2go6bK+uqcxxZ967HWvzQfNRwljuF77tIFnawTIkNmHL2Ma4cAjmSeh5EWwf+3QdxYcgJI8XC1FR873kF9/M3znCdf3YcZfMscmycO3AQ2ZTc0K3YAGhJIkdCts/1YWfqiYJAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [460] -  Southeast Well Ecological Survey
        [460] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XTW/bOBD9KwbPJkBJpCT65nqTbAA3G1RZ9BD0QEkjmzAtuhSVNhv4vy+oD9ty7KQtclhsczOGM2/eDJ+G4yc0ra2eicpWs2KBJk/oohSpgqlSaGJNDWPkDueyhP1h3h9d52jix3yMbo3URtpHNPHG6Lq6+J6pOod8b3b+2xbro9bZ0oE1P3z3q8EJ4zG62twtDVRLrXI08QgZIL8M3WDwaBBBXiUzW9brM4VRj9BXGPUgWinIbF8J9Yh36Oa/zkKbXAp1hojnh+Ggx7QLu5TV8uIRqoPE7IgxYwPGYX8HYgXJUhb2g5ANb2eoekNiRbaq0IR1XQ3j57iHqLxDvRVWQpnBAZ/wOC4cdtDvQ438B2bCtsrosx5H+0f9D7rou6VQUqyqS/GgjQMYGPpygvHQ/gky/QAGTTzXpFPSDmMngYOEff8+yMWVWDeFTsuFAlP1Sdxl52gSRIQ+Yz+AirfbMbr4bo0YfHg7Au4i7nTyTWyuS1tLK3V5JWTZtwd7YzSvDXyEqhILQBOExuim4YRudAmoQ3jcAJq4Pp3Am+vK/jLerYEKTjNEGJ05bzM253s+yQYya4Sa1cZAad+oyiPUN6v1JNtnFZ/M3nhdapNB85V9E5v+shtj7qzNd8M45eNOWYnVG/ehy3KRWNg0E3ZfZae+qXmb4g7hGrZ/l/JrDQ4XZX4UMJ6HGLI4wJSFAeZ5FGAKkEWe8H0CBdqO0VxW9q/C5ajQ5P6pyeYK2BHk/DzDqVKjNvSY5o02a6H+1HrlgPpR8xnEav/xuNMKBp3tTC0O9SI3q/rgxBpdLn4mnAQH4XNYQJkL8/jTCH/oOlU77gMPP+Q7hz2/sy4DDie87ozcnMsUMT/YuZzLNXB6IVvn59Q6LSyYmagXSzuXa/e+eO3BsYybzaI27QPmfhxM6naIMn78BLtRffY1dWtAP2p6pXyCr7U0kCdW2No9am7POJbPj6nkh8Xwfudveucv7nHb4ZDKCASZIJjRHDD1WI7TwhO4EB7nURiS1I/R9ks/pbpd9H5naAfV/RM6nFiURcQ/P7MSK8zoVkGmB0PLe6k11zmUVmZCuX64PK3DdK3rcuDWvAbHm0Qw3Opil6k2hcggUW7ynN5nGWev7FNsO0b/mfV8/8j98tPmgp1l5rraNPTwseueOPezNe/dTin3QGUiCoFEoYcpcIppFBKcBmGI4zwVcZB5jPgUbcfPVOTT8wXcynK11roczWS2VDJ/19LvoSWacipCiLAXpRRTiAOckpzglDDwGWSU+v5JLbHzBczrUphRotWwjHcV/W9VlKY8ogUNcAE8xpSTDHMoCE4z4kVRWpDAg5MqCl9416R6ADNKbG0WoMt3Kf0eUooCj9GQAy5yn2FKfI5FzAX2RZiywksjylizQrW4HcV7GpIvo0TXdgmisqPPoNToItNKL5wSRkltHuBx+I8yZpDmMWWYRoGPKS0o5pxGOBMQFoIEnHkF2v4LFSMleKMUAAA=",
            },
            AmountRequired = 2,
            UniqueFish = true,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Lunar Sole"] = new List<uint>()
                {
                    45725,
                },
                ["Pinkmoon Cichlid"] = new List<uint>()
                {
                    45724,
                },
                ["Silver Sturgeon"] = new List<uint>()
                {
                    45726,
                },
                ["Star Pleco"] = new List<uint>()
                {
                    45702,
                    45711,
                    45723,
                    45769,
                    45820,
                    45841,
                    45913,
                },
            },
        },
        // Export for Mission [461] -  Fish Sauce Ingredients
        [461] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACq2V226jMBCGX6Waa5CAcL5Lo7SKlHarpXtV9cIxQ7BKbdY2PWyVd1+Z4CbksJVWvYMZ+5v/H4/hA6adFjOitJpVa8g/YM7JqsFp00CuZYcOmOSScdwlS5talJAHaebAnWRCMv0Oue/AQs3faNOVWO7CZv1my7oRgtYG1j8E5qnnxKkD1+19LVHVoikh9z1vRP43umdkyWiH96WYWd09nzEW+l74hSILEU2DVFsnoe/5+8uCr1UIWTLSnBHiB3E86nE4bLtiqp6/o9orHB0ojqKR4tieAXnComaVviSs120CygYKTeiTgjwauhqnx9x9ajZQ74hmyOm5SQl9Lz7ExOOGBpYk2R+cEb0dFCvicHdwcByTYfd9TRpGntQVeRHSAEYB627ijOM/kYoXlJD7pmenJj1OzUTsFbTtvGTra/Lc+57ydYNS2SLm7EvIJ4kXHqkfodLNxoH5m5ZkdA8/BZhzuRfFK2kXXHdMM8GvCeO2Pa7vwLKTeINKkTVCDuDAba8JbgVHGAjvLUJu+nSCtxRK/zfvTqLC0wrBhTP5bcU+v9NTtEi1JM2skxK5/iaXB9Rv83pS7ZHjk9X7VVdCUuwv3Stp7WH3wdJE+3sTZWYit5NVaNGae8/4utDY9h/cncth+qbye8xN93C92l+c/e7QcGFSeVXmU88lXhy7IU0SNw2j2M3CsEySSUnJagUbB5ZM6R+VqaEgf3i0geEvsAsYU9v3rYJB40MY+48XJnlRkI7ixYKvJZYMuVZjQbSqsjRMYtdfpUaQ57mkXFEXJ9kKsywKkqCCzV+y46n58QYAAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [462] -  Aquatic Samples
        [462] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACq2V227iMBCGX6Wa60TK0TncUQRVJdqtNt2rqhcmGYjVNE5tp4etePeVTbwQCltpxV2YyXzz/+Mx+YRJr/iUSiWnqzXknzBr6bLBSdNArkSPDujkgrW4S1Y2dV1BHqSZA3eCccHUB+S+A9dy9l42fYXVLqzf32xZN5yXtYaZh0A/GQ5JHbjq7muBsuZNBbnveSPyv9GGkSWjCu9bMdO6f7YKIt+LvpFgq3jTYKlOTCTyPX+/KvheBRcVo80Jnh8QMppxNJTNmaxnHyj3DMQHBuJ4ZIDYM6BPWNRspS4pMzZ0QNpAoWj5JCGPh6mS9Ct3n5oN1DuqGLblqU2JfI8cYsh4voElCfYbp1RtF8WKOKwODk4nHKrva9ow+iTn9JULDRgFrLvQGcd/YslfUUDu65kd23SS6gXZa2jHecnWV/TZ+J606waFtE302VeQh4kXfVE/QqWbjQOzdyXo6B7+FaDP5Z4Xb7S7blXPFOPtFWWtHY/rO7DoBd6glHSNkAM4cGs0wS1vEQbCR4eQ6zkd4S24VP/NuxMo8bhCcOFEftvR5Hd6ig5LJWgz7YXAVp3J5QH1bF6Pqv3i+Gh389acixLNpXujnT1sE6x01NybOIsyZ9isQvFO33vWrguFnfnD3bkctm8izmNusoczan+17KVHzQVMcOl7fuDSNArdKExDl8ar0iVelSUr4q1ISmDjwIJJ9WOle0jIHx5tYPgK7ALa1Pb3VsGg8SEiwePF5KWnipUXBX3uGpRjJcuAkNJLEpemaexGAfHdNIsjN4y9qkwSEnppAps/ch9WWuoGAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [463] -  Northwestern Water Inspection
        [463] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1WTW/jNhD9KwbPIqAPSiJ187pJGsBJgzpFD0EOFDm0iciiS1GbTQP/9wVlq7b8sQEWCZAWvRFDzpv3ho8jvaJx68yEN66ZqDkqXtFFzcsKxlWFCmdbCJDfnOoadpuy37qWqIgpC9Cd1cZq94KKKEDXzcU3UbUS5C7sz683WDfGiIUH6xaxX3U4GQ3Q1ep+YaFZmEqiIgrDAfKPoTsMlg8ywjfJTBbt8owwEoXkDUY9iKkqEK5XQqIw2j8Wv83CWKl5dYZIFGfZoMdkm3apm8XFCzR7hdMDxmk6YJz1d8CfYLbQyn3huuPtA00fmDkunhpUpNuuZvQYdx+VbVHvuNNQC9jjkx3mZcMOxn2q1X/DhLuNM/qqh9nxQf+Tbfb9gleaPzWX/KuxHmAQ6OUkwTD+OwjzFSwqIt+kU9bOqLfAXsG+f1/0/IovO6Hjel6Bbfoi/rIlKpI8JEfsB1B0vQ7QxTdn+eDh/UPAX8S9mT3z1XXtWu20qa+4rvv24ChA09bCDTQNnwMqEArQbccJ3Zoa0BbhZQWo8H06gTc1jftpvDsLDZxmiDA6s7+p2O3v+MxWIJzl1aS1Fmr3TioPUN9N60m2R4pPVu9OXRoroHtlz3zVX3YXlD7avZuUERZsnTVzZuUfuq7nMwerbsLuVG7dN7bvI24frmP7R63/asHjIhpKzvIUcM7KFBPKBC4jLnHEhYpVzGjJKVoHaKob95vyNRpUPDz2ge3Y3wW8KFQ8vKLNYjsy0jxJzwu4gYrXYmEqzQc6/CT2jRorB3bC2/nCTfXSjzb/0ZBQOy145V+uL7Q5MF6ath4c6zp/+GqT4QSlvlJrFRcwq/wFnv52pCx9Y3al6wB9mk/hzlA/bSOf7CMT39WuofvG2trJLzfh3bFTBt+zXUxyKWNCMC1VikmaAKYsjbHMkzSJRZYoFqN1cGyj/LyAaVtzO/pFN6JtPo+P/o3G6W/9+Ku1ryg8fxUT0yzNs7FL9IEeoiUFkAxwJEWICSkTXBJGsIhYSAVLGU3oSQ9l54n/alYrkNjUoylwpXzip3HS/xPpIydSpLIyJILhMgkTTECEmJNI4YxESkS5THhWnnQTPS9gZipuR5cVt/C5rPSfHUr+D+tHQ0mL0cTyGkaX1ctHzqac0TQMlcKciByTPCoxDSHCiSQqVAkvOc3R+rEvt2X4QLLkcXRrrFs8Q+PA1qM/uQM7uq4b/+upTT38e0uAKCVFhlUKISZKAuZRSTGTZc5YQhSLQrT+Dk+y1dQPEAAA",
            },
            AmountRequired = 4,
            UniqueFish = true,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Hopped-on Leaffish"] = new List<uint>()
                {
                    45736,
                },
                ["Lunar Discus"] = new List<uint>()
                {
                    45737,
                },
                ["Melancholia"] = new List<uint>()
                {
                    45696,
                    45708,
                    45735,
                    45754,
                    45778,
                    45853,
                    45938,
                },
                ["Solar Flarefish"] = new List<uint>()
                {
                    45738,
                },
            },
        },

        // B Rank

        // Export for Mission [464] -  Hollow Harbor Water Inspection
        [464] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACt1Xy27bOhD9FYNrCZAo6rlzffMC3DSoU2QRdEFLI5uwLLoklTQN/O8FJdGWFCVxiywuujOGM2fOjOblZzStFJ9RqeQsX6HkGZ2VdFnAtChQokQFFtKPc1bC8TEzT1cZSnAUW+hGMC6YekKJa6ErefYzLaoMsqNY6+8brM+cp2sNVv/A+leNE0QWutjdrgXINS8ylLiO00N+G7rGiMOehfMumdm62r4SGHEd8g4jA8KLAlJlIiGu43bV8PssuMgYLQxA4JIeAGnVzplcnz2B7DjyBwx9v8cwMDmnG1isWa4+UVbz1AJpBAtF041Eid9mMYhe4nZR4xb1hioGZQodPsHQLuhnDBtTwX7BjKqmEozXoTUe5NtrrW/XtGB0I8/pAxcaoCcw4XhWX/4VUv4AAiWuTpLxSXoeTMI+sdUF3daRTctVAUIaVP01M5R4oUNe0O1BRfu9hc5+KkF7nXWoMZ35W754pLurUlVMMV5eUFaafNiuheaVgM8gJV0BShCy0HXNCV3zElCL8LQDlOjEjODNuVR/jXcjQMI4Q2SjV94bj/X7kc9iB6kStJhVQkCpPijKAeqHxTrK9kXEo95rrXMuUqjb6pHuzMeuhZmW1o3ixz622spaKL7Tnc3K1ULBrh6hxyjb6puKjwmuC1ez/VayHxVoXBQtU0zS2LNDP4ptEuPAjkiI7dSPlsEShz72AO0tNGdSfcm1D4mS++famw7gQDCOX2c4LYpJYzqkec3FlhaXnG80kJktd0A3x+bRrxJ6mW1FDQ5xQz2cjPFCCV7WvddqHVowp4UE62RUx+ugzmEFZUbF00cBf5PwH69afaPYSEz4X8ri6W4N5TRV7AGuMigVS/XGGEHFgc5BY/9uBl61PCXKEeNbwXZH2n0FXUEHlRfMxpTGSPT1dPdMcwViRqvVWs3ZVi84t3kYtlV9ylSi2aD6R2dVjFwAXujHw0X45k2hzxAzCU0hf4UfFROQLRRVlV6y+s4ZVvcfFfHJRdlT7NfT6QVzWmX8EyVgvjn5w2/emaFu6EXg4MgOI5rbBOe+TbEPdhws89Qn4GZA0P67GaLtLXx/EDRz9P4ZdQcq8UPivT5SbwSTW6pYOqmNunPVfSs9hymic6J9NQrTLa/KjtrYaezHw9vH6x+ekXZciZymsCj09DORvGio4Y3n7y30v/mLcNzDf719F490pyUzndU6od193G5h/bMRH9XGqrdTaUvPz1yXeDbJ48AmnhvYUeqAHcWOkwVujP3YryutwW0p3pOAfJ9c8qLgj5NLKpZcTO6oAjG5KqU+aRgv+1cBdhy8DD3Hdr1oaZMUMptiTG28JLlHAxrnNED73xGOGoBIDgAA",
            },
            AmountRequired = 3,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Prismatic Fish"] = new List<uint>()
                {
                    45743,
                },
            },
        },
        // Export for Mission [465] -  Preserved Foodstuffs
        [465] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACs1VTU/jMBD9K2jOiZTvOLmVqkVIhUVb9oQ4TJNJYxHirO3Asqj/feU02Tal3UqIw96s8fjNe+Pn8TtMWi2mqLSaFmtI32FW46qiSVVBqmVLFpjNBa9pt5kPW9c5pB5LLLiTXEiu3yB1LbhWs19Z1eaU78Imf7PFuhEiKw1Yt/DMqsOJmAVXzX0pSZWiyiF1HWeE/G/oDiOJRyecs2SmZft8QljgOsEZRgOIqCrK9KAkcB13P807z0LInGN1gojrRdGox0F/bM5VOXsjtVc4PGAchiPG0XAH+ETLkhf6EnnH2wTUEFhqzJ4UpGHf1Yh9xN1HTXrUO9Sc6uyUUwLXiQ5honFDvQFJ8t80Rb01ykDi8LR3cB1+f/q+xIrjk5rji5AGYBQY1PnWOP6dMvFCElLX9OyY0yNmHLFXcGjnJV9f4XOne1KvK5JqKGLuPofUj53gA/sRFNtsLJj90hJH7/AvAXMv92L5is11rVuuuaivkNdDe2zXgkUr6YaUwjVBCmDBbccJbkVN0CO8NQSp6dMRvIVQ+tN4d5IUHWcINpzY31bs9nd8lg1lWmI1baWkWn+RygPUL9N6lO0HxUerd1lzITPqHt0rNsNld8HcRLt3EyahZ/XOWmrRmHfP6/VSU9MN3J3K3n0T+TXi9uE6tj9q/rMlgwt54FFY+JEdx5FjBxnLbOZHhb2Ks8TJMUDPd2FjwYIr/a0wNRSkD49DoP8FdgEjCtKHd9gu+okWxgE7LWDR1igvpoIqzLDW5UiMmc6mW5NCk5xiuy71gj+bcWc+kpxqzTOszPM11bYJk2fR1ntpx6ZYmBy+ZH88ZJkp3MoCM1pW5lIHLUl4ZoCFGwv+m+9x56pPe2n5io2JTE1Xu4buu6v3lFluw7u0Yy7f857rMgfJITtZFY4dOC6zMY58G1mx8rzc8YMkgc3jUK6n+BBE4eNFF5IvlF/MhciVbotCjZ29YlGMOfPsmLmJHTBiNovj0PYwCoIQCzdOXNj8AX/IXB46CQAA",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [466] -  Hollow Harbor Ecological Survey
        [466] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1YTW/bOBD9KwbPJkBKpCT65nqTNoDbBnUWewh6GJGULUQWXYpqmy383wvqI5E/lGAXwW53kZs8HL55M3yaofUDzWtnFlC5apGt0ewHuighLfS8KNDM2VpPkV9c5qV+XFT90pVCsyARU3Rtc2Nzd49mdIquqovvsqiVVo9m779vsd4bIzcerHkI/FODEyVT9HZ3s7G62phCoRkl5AD5aegGQ8QHO8izZBabejuSGKOEPcOoBzFFoaXrM2GU0KFb8DwLY1UOxQgRGkTRQY1Zt+0yrzYX97oaBOZHjDk/YBz1ZwB3erXJM/cG8oa3N1S9YeVA3lVoxruqRskp7hBVdKjX4HJdSj3gEx3viw4rGPRbbf6nXoBrldFHPd4dHNU/7HbfbKDI4a66hK/GeoADQ59OOD20f9LSfNUWzagv0jlpR4mXwCBgX783+fotbJtE5+W60Lbqg/jDVmgWxoSdsD+ASvb7Kbr47ix0L56v/I1ZfYPdVenq3OWmfAt52dcD0yla1la/11UFa41mCE3Rh4YE+mBKjaYtwv1Oo5kvzBm8panc38a7trrS5xkijEbW24jN+iOf1U5LZ6FY1Nbq0r1QlkeoL5brWbYnGZ+N3ni1Alk5s/Pva16uV07vmkb5yL0T0dy+DOUhXMPh9zL/UmuPi3QaEqYyhdNQUcxUHOEkJRSnEDIiYsXjiKD9FC3zyn3MfIwKzW5befoEHggKMc5wXhSTdusxzQ/GbqF4Z8ydB+o7xh8amt/e7rNo3BmNfWfpfVbOmnJ9xouEA6+lXutSgb0fc/zN1GlxPmAQiQeHkWhDl/FQrdeNzXdjkWIehA8uY7EOnJ6I1vl5ic0zp+0C6vXGLfOt7+20XTjWXjPVa9sOD/8w6JJtA+PidPw9Mcn8CO7f+v54P+kvdW61WjlwtR8ofsa/nvn/6cwHnQWAUBryAPMskZjRgGIhOMFhRnmgSMZikGj/uW8t3T3w9sHQdpfbH2jYZhiP+ROtcFmXYCcLSM02hYNmQ5+qzpXSpcslFL4kPlTrMN+aujxw8wTE8SAPDy9ViY9U2wykXhWwe2Qu+DP3F76fol/mOuwHR3sdbG9Aj9NpmBEbP4uFqba5nNyAhdLVBaAB6MIXthVmZ/nkx1VnbgMOJ1g3t/xjax4AnFH2QIWEJ2EoSYhZFmSYgWQY0izDJKBSBkpKEodoPz1RGSPjmb03plznRfEqsFeBIQiDlLIIcJoSgZmigJMEAhyJOOVZRrUm4pzAeDCe2ccdFJML/Sqwf0BgfPwYLj3Vb+C0ncyt21hT1Fb/OyrL4iBmIosxZTHFjGUaAyQaa65lyOMgCnh6VmXhc8PyOrdga1m/au1Va63WIuABCwXghCuCmUwoTiPFsYjCFEiapIE+PzLF0yPTGnk3WUCp7n8drZ370Pcfk96p1P7yx4LVicZeRkmKZJrFIsFCCoZZHAksJM2w5kwIJZJMd38BWtyO4i2Los+Td6YozLfJO7CpsZMLaQqz9kKYrGr7VXsJDb9iJCEkioZYUQmYaQhwEpMQhwFnStMAlGJo/xM2YS3m3hYAAA==",
            },
            AmountRequired = 3,
            UniqueFish = true,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Lunar Cabomba"] = new List<uint>()
                {
                    45751,
                },
                ["Lunar Pirarucu"] = new List<uint>()
                {
                    45753,
                },
                ["Moongill"] = new List<uint>()
                {
                    45740,
                    45750,
                    45790,
                    45800,
                    45878,
                    45930,
                },
                ["Moonrock Candy"] = new List<uint>()
                {
                    45739,
                    45749,
                    45789,
                    45799,
                    45871,
                    45877,
                    45929,
                },
                ["Opal Eel"] = new List<uint>()
                {
                    45752,
                },
            },
        },
        // Export for Mission [467] -  Absolute Specimen
        [467] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XS2/bOBD+KwbPJiBRpCT65niTbAAnDeos9hD0QIkjm4gsuhSVNhv4vy+oh235ERdBDkWRmzyc+ebBjzPjVzSurJ6I0paTbI5Gr+iyEEkO4zxHI2sqGCJ3OFUFbA9ld3Qj0YjEfIjujdJG2Rc08ofoprz8meaVBLkVO/11g3WrdbpwYPUHcV81ThgP0fXqYWGgXOhcopHveT3kt6FrDB71LLyzwUwW1fJEYtT36JmIOhCd55Da0zj+rhU5H5Q2Uon8BJ5PwrBXctqaXalycfkCZVdR6ntsLwHGegmE3ZWIJ5gtVGYvhKrTcIKyE8ysSJ9KNGJtkcP4EHcXlbeo98IqKFLYiSfctwv7BSWdqVH/wUTYhijHWBfGB2Bk73aCFuxhIXIlnsor8ayNw+sJuuyCYV/+FVL9DAaNfFezEyHQnsOunBdqfi2Wdd7jYp6DKTsn7u4lGgWRRw+i70HF6/UQXf60RvSe5SYAdy8PevZDrG4KWymrdHEtVNGVGvtDNK0M3EJZijmgEUJDdFfHhO50AahFeFkBGrk6HcGb6tK+G+/eQAnHI0QYnThvPNbn23hmK0itEfmkMgYK+0FZ7qF+WK5Hoz3I+Kj3WutKmxTqR/dDrLrLroXSSetnxDgLhi2zZlav3LtXxXxmYVX3322WLfvG5mOS24Wro/2nUN8rcLiIxiTJZCpxlGQcU8ESLEjs4YCHkERMCj/jaD1EU1XaL5nzUaLR42vtzSWwaRJNdqdinFaFMIOpMM/Cod1psxT531o/Ofuu4fwLov7t5CXYzdPJRF5C95bbw906t6ImeepHrpF1mDNrdDH/AFQv2EGdwhwKKczL9on/IsJfukry/UwbDRLyjcJB2IcqvRiOaD0YtTrlKWIk2Kic8tVTesNbq+coPc4smImo5gs7VUs3k/zmYJ/r9XJSmWbouY+ddn6kZwcR44dD/Y2B7BaLrj11NPsK3ytlQM6ssJWbi25zOcG9X+PSeW58UuBdFHjvne80tszzAiJlggNOBaaCSJyk1MNZ5ntBmskw4xKtv3Wdrd1uHzeCprk9vqJ+l4tYfK7LXah0oUyvI/tvFedGQmFVKnJXEeepURgvdVX01Oouu79/BP3VMHaeKpOJFGa5a0Xb9nxm7WLrIfptdvztLHz3BHTGTjJxZawruDsT0QiNk1LnlYWBG+tqCQVBjVWjt7U7xt4+05I4kx6mWSIxZczHCWMEe4HPIIn8iMYJWg8PmES90xndal3MVZ5/suh3ZxF+H2kYAy6In2AWiRTTLCA45jHFIXAphKScU+8YaRg5ncCXlcgHl/BJmj+VNFEig9QHgTkkHNM0oZjLFDAECYkJDwkXRzvN+c38XhlhqrT6pM4fSp2QxyEjjGAmEsA0iCPMU8GxECBIFmZ+GPF6HWpw2xAfaRh9GxwMygEeXGkt+/8kgcQipUGKI8kIpjT0cML9GGckC4UMEkZJiNb/Aw5f6V25FAAA",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [468] -  Aetherochemical Creatures
        [468] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1WS2/jOAz+K4HONmDZsvy4ZbKdboFMt2iy2EMxB9miY6GOlZHl6XSL/PeB/EjsNG52ih7msDebIj9+pCiSL2hea7lgla4W2QbFL+iqZEkB86JAsVY1WMgcLkUJx0PeH91wFLthZKE7JaQS+hnF2EI31dWPtKg58KPY6O9brC9SprkBaz5c89Xg0NBC17t1rqDKZcFRjB1nhPw2dIMRBSML5yKZRV5vewYEO+QChd5KFgWkeiIjBDt4aOVeZiEVF6yYwMMupaMck87ss6jyq2eoBgH4JwH4/igA2t8Be4RVLjL9iYkmDCOoesFKs/SxQrHfZZWGr3GHqFGHese0gDKdqhSCHXoKQ8f5dXskJf6FBdNtofQkTq3dk9vxOut1zgrBHqvP7LtUBmAk6KPzrLH8HlL5HRSKscnZuUqnoSmQgcM+nZ/E5pptm7jn5aYAVfVOzN1zFHuBQ16xH0GF+72Frn5oxbp3aC5iLVdPbHdT6lpoIctrJso+Hza20LJW8AWqim0AxQhZ6LYhgW5lCchqEZ53gGKTmDN4S1npd+PdKajgPENko4nz1mNzfuSz2kGqFSsWtVJQ6g+K8gT1w2I9y/ZVxGe9N1ptgay03JnnK8rNSsOu6ZtH7l0RzdXHUB7CNRz+LsW3GgwuiigjLAqx7WZOZpOUhnbIA88mqUOJw1lKI472FlqKSv+VGR8Vih/a8jQBHAhG0TTDeVHMWtNTmrdSbVnxp5SPBqhvIP8Aa/6NvAJ9eIsZKyro32Z3aALsX2knauEJDkxj6jFXWslyMOEumzvewHwJGyg5U8+/jPCHrJPiNKRWw6XRQeHIb1JlxOGM1lqJ3ZSnwHe9g8qUr5HSG946PVPE80yDWrB6k+ul2JphgtuD0+pu1ohatdPKfAz68Jlm6wV+9HoavzFJzQrQt5m+nu7hWy0U8JVmujYDzewYE0V2oWj+c238XwLvKoH33vmglTkh8bMA+7abRoFNiJPYjETETljmpwSo40YE7b/2vazbQx8OgradPbygYV8jfkC96c52z55my7pkaraWtWllZlEeNjn8VpJuOJRapKwwmTEeW4X5VtblQO3cLuVHp/uEN171QuO4VhlLYVWYDtXHE/kX1ih/b6HfZkk/DsV3j8LVE9sZycJktUnocDh2I9F8tuKj2rkaHtSbRzDHicvthPHAJl7m2QmPsE29gCYOdqmDw6beWtyO4gOh4dfZHHQOSqY5bM39zxYKmG6exsgF+OCRhGc2D2lkE8apHeHEs33m0IBgL8Suj/Y/AaLaHNTFDQAA",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [469] -  Westward Ecological Survey
        [469] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1YTW/bOBD9KwbP5kKkSEryLfGm3QBuN6iz6CEoFhQ5soXIoktRabOF//uCkhVb/ojTIFi03dxkcubNm9HTcOhv6Kx2ZiwrV42zGRp9QxelTAs4Kwo0craGIfKbk7yEzabuti41GtE4GaIrmxubu3s0IkN0WV18VUWtQW+Wvf2qxXpnjJp7sOaB+qcGR8RD9HZ5PbdQzU2h0YgEQQ/5cegGI4l6HsFJMuN5vTiSGCMBO8GoAzFFAcp1mTASkG0zepqFsTqXxREihArRqzFbu73Jq/nFPVRbgfkOY857jEX3DuQtTOd55s5l3vD2C1W3MHVS3VZoxNdVFfE+7jZqska9ki6HUsEWH7HrJ/oVpJ2rzf+BsXStMrqou950p/7h2vt6Lotc3lZv5J2xHqC30KUTDvvrH0CZO7BoRHyRDklbxF4CWwG7+p3ns7dy0SR6Vs4KsFUXxL9sjUZhFLA99j2oeLUaoouvzsr1h+crf22mX+TysnR17nJTvpV52dUDkyGa1BbeQVXJGaARQkP0viGB3psS0LBFuF8CGvnCHMCbmMo9G+/KQgWHGSKMjuy3EZv9DZ/pEpSzshjX1kLpXijLHdQXy/Ug272MD0ZvrFqBTJ1Z+u81L2dTB8umUW64r0V0Zl+G8jZcw+GvMv9cg8dFJGWpjpXCQgcKM84FliJNsQKZZZQTQuMErYZoklfuz8zHqNDoppWnT+Dh4+YJp8c5jk21MIW0d9KDvTd2IYs/jLn17l2f+Aiy+d1+en63Auf5dx/heqlNkpHIN5rOeeqsKWff4x6EW+4TmEGppb3/boTfTZ0WD9x7FlQkDwYbfkdNehwOWF3bfHksUsRp+GByLFbP6JFoazuv0bPMgR3LejZ3k3zhDwfSbuyKtxkLatuePv5hq822HZAn++fnI0ehP8O7ttEp5QN8rnMLeuqkq/2J5IeEXfk8TSVPFsPrO/9P3/lWa4pVymSgAsx1EGOWZClOmUhxTCnlGRNEJhSthod7UXi8F03qUtrB5GnN6FVNP7OaXjvI/7qDKEGIikiG4yiWmKWaYklJhGUaJ4KmImaUoNWnbrpZX0VvHhbapnLzDfW7SyTEqe5ybk35DwyuClCmN5uRx0p0qaF0uZKFr4uP1xqcLUxd9syaHrd7oQj7l7vYR6ptJhVMCz/DHL7W8oSfuFbx1RD9MLd0P461t9Q2h83Q/KxJtIMb+wo3xd0enNEIydKUf98wkWDyafARKvdFWj24UKYwM/+mBtPa3sE9aqFa5w3YIc1v6TNkLBEsI5iGhGMWQIolpwJLHUcqUFEWBX743tdffPJ0q8tZ5p1etffzau+43H4jzxOcJDLJ0oDhJAwVZpD5pyTAOhRKMCCh4uKg4NjxpM6LGhbGlIOJkepVcC8puH2Bfff/ACdFhp+npEiRWBEVYkF0hFmoOE7TOMKJppApIDqg9KCS+COtC+QyL2c/mpB+3Vb19EvSvoy6lQ9bsmljvYzAqEoSGQcJjsMgxIwmHCdacawh1EGU6YiS8KDAouNJTZ209n4wdXk5s9If2q8S+xWbUxqliQ5khMM4AcyopjgNCcGcCtBK8igI02bub3HXFP2Ud2LG2/53gtNMk1RjoQAwi4IYS64YJjTmQUYlialGq38BoSs69lEbAAA=",
            },
            AmountRequired = 4,
            UniqueFish = true,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Bluemoon Loach"] = new List<uint>()
                {
                    45699,
                    45719,
                    45731,
                    45764,
                    45784,
                    45810,
                    45918,
                    45935,
                },
                ["Leaping Loach"] = new List<uint>()
                {
                    45765,
                },
                ["Lunar Bronze Pleco"] = new List<uint>()
                {
                    45766,
                },
                ["Lunar Lungfish"] = new List<uint>()
                {
                    45768,
                },
                ["Starry Stingray"] = new List<uint>()
                {
                    45767,
                },
            },
        },
        // Export for Mission [470] -  Supper Emergency
        [470] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1W227bOBD9FYPPEqArJerN9SbZAE42qFPsQ9CHETWyiciSS1Jts4H/vaAutuRL0hQB9oJ9k4czZ86MD4fzTKa1rmagtJrlS5I8k4sS0gKnRUESLWu0iDmcixL3h1l/dJ2RxIuZRe6kqKTQTyRxLXKtLr7zos4w25uN/7bFuqkqvjJgzYdnvhocGlvkanO/kqhWVZGRxHWcEfLL0A0Gi0YRzqtkZqt6faawwHWCVxj1IFVRINd9JYHruEM373UWlcwEFGeIuB6lox4HXdilUKuLJ1SDxOEB4zAcMab9fwCPuFiJXH8A0fA2BtUbFhr4oyJJ2HWVxse4Q1TWod6BFlhyHPChh3F03EGvD5XiL5yBbpXRZz2M9g7673fR9ysoBDyqS/haSQMwMvTl+NbY/hF59RUlSVzTpFPSprGRwCBh378PYnkF66bQabksUKo+ifmzM5L4kRMcsR9BxdutRS6+awndxTOdv68W32BzXepaaFGVVyDKvh+2a5F5LfEGlYIlkoQQi9w2JMhtVSKxWoSnDZLENOYE3rxS+pfx7iQqPM2Q2OTMeZuxOd/zWWyQawnFrJYSS/1OVR6gvlutJ9keVXwye+PVCmShq425r6JcLjRumkG5596JaCrfh/IQruHwqRRfajS4BMKQ5a6X2z7Q0A5YFtsMXdeGgKNH48D30CNbi8yF0n/kJociyUMrT1PA7nKHLAzPc7w0sv8GGuVkKvVKVkUt0eDeVnINxe9V9WiQ+pHxJ0Lzu72F5lShNqX097EztfUGbmRmTh+80LIql28Jd/xB+ByXWGYgnwxC57ibBjkUCq23Af9W1WmxK2nk4VG2c9jTPutyitrQ65PCeyk2LbOeUms5SH9Y0BgsCj1DvI084vWG2BcId37mIkxzjXIG9XKl52JtXiC3PTi8Ic3uUcv2iTMfg1nejtmQHT/SL7y3ZlHoZ1OvwY/4pRYSs4UGXZtnz2wih8L8Of29VWZ/k2xOKuSnpPAv/c8H84+DH4DvODbFNLODGMEGLwPbj5DxPA4Y8xjZfu4HYLetPuwM7Qx8eCbjYRhFzvlhOK9LkJMrCUpNZiA3o+HtvtSg6wxLLTgUpismW+swXVd1OXJrBvLhxuGPt7/YZKplDhwXhRlh+0n+yqIVbi3yj9nb98/mLz+WJthYZqaNTQeHz2f3aJrP1rx3OyXYgbiCEF2f5mBzCDw74Dm1GdDYdmiYug5jwMKUbK1j8bxQwA1wWaUS+ErU60kjJaH+V9B/VEEs4pQzmtop454dpLlvQx47NlDq5L7P3MiHZjy1uB3FhyByPk8W9WaDcnKxRrnEkj+NFz+fUSfzqWdHGYvtIHJzOw0Daruh5+VOipRHnGx/APkQ6LAREAAA",
            },
            AmountRequired = 13,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Ataxite"] = new List<uint>()
                {
                    45772,
                },
                ["Lunar Grass Carp"] = new List<uint>()
                {
                    45712,
                    45770,
                    45821,
                    45842,
                    45914,
                },
                ["Macrobrachium Lunaris"] = new List<uint>()
                {
                    45771,
                },
                ["Star Pleco"] = new List<uint>()
                {
                    45702,
                    45711,
                    45723,
                    45769,
                    45820,
                    45841,
                    45913,
                },
            },
        },
        // Export for Mission [471] -  Alchemical Resources
        [471] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1WS2+jMBD+K5XPIJn345Zm026ktFuVrHqoenBgACsEp7bptrvKf1+ZRwJJaKWqhz3sDcYz33wzHr7hD5pUkk2JkGKaZij8g2YlWRUwKQoUSl6BhtThgpZwOEy6o3mCQtMPNHTHKeNUvqHQ0NBczF7jokogOZiV/67BumEszhVY/WCqpxrH9TV0vV3mHETOigSFBsYD5Peha4zAG0TgD8lM82rTMbANbH9AoYtiRQGxHOmIbWCjH2V+zILxhJJiBM8wXXfQY7sNu6Iin72B6BXgHBXgOIMC3O4OyBqinKbyktC6DGUQnSGSJF4LFDptV13/FLePGrSod0RSKOOxSbEN7B7DuMP+mh0Sp79hSmQzKB2J42jz6HasNnqZk4KStbgiL4wrgIGhq87ShvZ7iNkLcBQaqmfnJt311YD0EnbtvKTZNdnUdU/KrAAuuiTq7hMUWh62T9gPoPzdTkOzV8lJ+x2qi1iy6BfZzktZUUlZeU1o2fVDNzS0qDjcgBAkAxQipKHbmgS6ZSUgrUF42wIKVWPO4C2YkJ/Gu+Mg4DxDpKOR8yZjfX7gE20hlpwU04pzKOUXVXmE+mW1nmV7UvHZ7LVXMyCRZFv1+dIyiyRsa908cG+HaMK/hnIfrubws6TPFShclPoe+IZDdC/Brm5bgaOvAnOl4wTAsoMgwaaHdhpaUCF/pCqHQOFjM56qgL32OIHjjnO8LCq4eGB8o7BuGd+Q4jtjaxXdqcYDkPpd2RX1uhTb8JS6dD6R5KzMznhhq+e1gAzKhPC3McdvrFoV5xOabrB3GMnWdxlP1XgtOd2OZfIc09q7jOUaOL2TrfVTczVJJfApqbJcLuhG6bvRHBwPXL3ZK94sEPXQk8Yz+md5TnC6IN9Zbmord19+d9v38FxRDkkkiazUjlFr//8I/IsjMP/knffUBQKMSexg3fXSVLeJ4+u+7xM9cFM/DZzAsGxAu6dOXtpfw8e9oVEY9d7oWasmj7ZnPF1MijiHDY1JcXEPglU8BjEUN4x9w0/TWI9d39Ztw0r0lWWmumfFCeDYJwHEaPcXOhT6MwQLAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },

        #region A Rank | Sequence | Timed | Weather

        // Export for Mission [472] - Northward Ecological Survey
        [472] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XTW/bOBD9KwbPEiBKlCjp5njTbAA3DeoUOQQ9UNLIIkKLLkWl9Qb+7wX1EUv+iIMgi+1hb/Rw5vHN6HGGfkbTWssZq3Q1y5cofkaXJUsETIVAsVY1WMhsznkJu82s37rOUOyGkYVuFZeK6w2KsYWuq8tfqagzyHZm479tsT5LmRYGrFm4ZtXgBKGFrtZ3hYKqkCJDMXacEfLr0A1GREcRzlkys6JenUiMYIecYdSDSCEg1X0mBDt46OaeZyFVxpk4QQS7QTCqMenCPvGquNxANTjY32Ps+yPGQf8N2CMsCp7rC8Yb3sZQ9YaFZuljhWK/q2oQHuIOUaMO9ZZpDmUKAz7BflwwrqDbhyr+D8yYbpXRn7of7e7V3+ui7womOHusPrEnqQzAyNCn41lj+1dI5RMoFGNTpGPSDkIjgcGBff0u+PKKrZpEp+VSgKr6Q8zHzlDsUYccsB9BhduthS5/acW6i2cqfycXP9n6utQ111yWV4yXfT1sbKF5reAzVBVbAooRstBNQwLdyBKQ1SJs1oBiU5gjeHNZ6Xfj3Sqo4DhDZKMT++2Jzf6Oz2INqVZMzGqloNQflOUe6oflepTtQcZHT2+8WoEstFyb+8rL5ULDummUO+6diKbqYygP4RoO30r+owaDi3IaMuL5jg2UODYJKbGjhCS2yyAKM8ogcxjaWmjOK/0lN2dUKH5o5WkSeCEYRacZToWYtKH7NG+kWjHxt5SPBqjvGPfAmt/GXoF+uYs5ExX0d7PbNAn2t7QztfAEU9OJesyFVrJcfgCq4w1Q57CEMmNqs2vWb0T4S9aJ2M+09XCD6MXhgPahy4jDEa9vFdwpvm6Z9ZRay5lCj8Go7xribeTZcr4SOyL8pRSb+wLKG6mnqeZPsBAnCteDmLszzTWoGauXhZ7zlRlauN3Yv1TNc6VW7VQ0i0H7P9LjPepHh2P+lYltnhp9d+tl/BV+1FxBttBM12ZwmrfMCW2/TavntfffSuyomt4km7P6+Hcl8N5vPuigNMfYITiyo8TLbYKJZ0ep59vgUYgCBxInx2j7vW+h3Xv34cXQdtGHZzRsp8SnoXu6oc7rkqnJvRR5boKGXRW/Vp7rDErNUyZMTcxZrcN0Jety5GYYRPsvFm/8egzNSbXKWdrd2ePvZj/yz7zb/K2F/pi/Absp/O7Za4KNZWaq2hR0OI27GWyWrXnndky9A6WBGxLPdTLbcz0zqwPfDnEGNqWR77oOjpLQzOpDJXmnE7gVTPOyXk0ueFpw9edI6X/tlB+pHS8LApr4ge0zj9okibAdUurYDGOfhRl1c482XarF7Sg+EOp+n0zjyY1UuvjJVDa5TKWQS/PhJ4taPcFm/J5kKbh5kiQ2oYlnEydgduTl1CZOxCiDJMEQoO1v5TMqZmgQAAA=",
            },
            AmountRequired = 1,
            UniqueFish = true,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Platinum Bichir"] = new List<uint>()
                {
                    45783,
                },
            },
        },
        // Export for Mission [473] - Rare Aquatic Specimens
        [473] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XTW/bOBD9KwbPJqAPUh++ud40G8DNBnGKPQQ9jMiRTUSWHIrqNlv4vy8oWbFkyzEaGNgecpPJ4Zs3w8eZ8U8yrUwxg9KUs3RJJj/JVQ5JhtMsIxOjKxwTuzlXOe43Zbt1I8nEi+IxudOq0Mq8kIk7Jjfl1Q+RVRLlftnabxusL0UhVhas/vDsV40TRGNyvXlYaSxXRSbJxHWcHvLb0DVGHPZOOGfJzFbV+kRgzHXYGUYtSJFlKMxpHLd7yjtPqtBSQXYCL3BZD4/tTn1W5erqBcs2ocx1+AF/znv8g/ZG4AkXK5WaT6DqKOxC2S4sDIinkkz4LsdBdIzbRY13qHdgFOYCO3yCw3NBP59ee1Srf3EGptHJkOiC6AjMO7gcfwf2sIJMwVP5Gb4X2uL1Ftro/HF//R5F8R01mbg2ZycosJ7DNp2f1PIa1nXc03yZoS5bJ/bqJZn4ocOO2Pegou12TK5+GA27V2kv4qFY/AObm9xUyqgivwaVt7ml7pjMK41fsCxhiWRCyJjc1iTIbZEjGTcILxskE5uYAbx5UZp3491pLHGYIaHkxH7jsd7f81lsUBgN2azSGnNzoSgPUC8W6yDbo4gHvddWjUAWptjY56vy5cLgpq6ie+47EU31ZSh34WoOX3P1XKHFJZ7vYcJlTFNHJpQ5EdA4ki4FBoHjSJFI4ZDtmMxVaf5KrY+STB4bedoAXgnG8WmG0ywbNUcPad4Weg3Zn0XxZIHaAvI3Qv3brpdoXt9iClmJ7dvcbdoA21e6W2rgmRvawtRiLowu8k6/O3/c8TvH57jEXIJ+uQCvGviPokqyw0gbCy+IXw32tE+aDFHrWj1otTnlKeSe/2pyylfP6A1vOzur7WlqUM+gWq7MXK1tj3GbjUPR17NGpZsmZj865XmgBvshj4979Bv91c4JbfVpZXaPz5XSKBcGTGX7nB1ETmjvjJZ+VTIfEvg1Cbz3zjsVDnjgcB461HNjjzLhJRSCxKMIvkQv9RkGAdl+a0vcblh9fF1oqtzjT9Itd4yHUXi64M2rHPRoKkDiWgkFea/wuW9l6EZibpSAzKbFumsMpuuiyntmlkR8OFT4/Xkvsp4qnYLARWbrUcs+5mdmKb4dk99mbt93xnf3Q3vYrsxsGusMdjvkri/az2Z5bzak2I66uIhd9CSnPECfMpcjjSSTNE3AZdIHJ0gDsh0fqyc6HcDiSW02Kl+OFgb0xZUz9IflQ0j/u5BciSC5K2ji8oSyKAUaecKjoRPFcSSDNAIYEpLN2Ntl6B4UrKH8fUrQsAI/hHQZIQkGDveTkMrQTvSYSprEUUJj6frM4RHErjNYkfiHkD6E9HUvJAeYK9xI0JQnjDLOJE2kQMqZkBIAHPSwHpwa3B3FRxb630bTyegeNI6mzxUYJUb2/7BaY27F03GR+kzEYRRT4bsxZT5ENEZgNOZJysETvogCsv0PwsUE2bMUAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [474] - Foodstuff Emergency
        [474] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH6_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",
            },
            AmountRequired = 18,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Darkside Bass"] = new List<uint>()
                {
                    45791,
                    45801,
                    45879,
                },
                ["Grand Crowntail Betta"] = new List<uint>()
                {
                    45793,
                },
                ["Lunar Sisterscale"] = new List<uint>()
                {
                    45792,
                },
                ["Moongill"] = new List<uint>()
                {
                    45740,
                    45750,
                    45790,
                    45800,
                    45878,
                    45930,
                },
                ["Moonrock Candy"] = new List<uint>()
                {
                    45739,
                    45749,
                    45789,
                    45799,
                    45871,
                    45877,
                    45929,
                },
            },
        },
        // Export for Mission [475] - Precise Water Survey
        [475] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1Wz3ObOhD+Vzw6oxnAIBA31y9NM+OmnpI3PWR6ENJia0LAFSKtm/H//kb8iMGGZJrx4R16g9Xq07erT7v7jBaVLpas1OUy3aDoGV3lLMlgkWUo0qoCC5nFlczhuCi6pRuBIjekFlorWSip9yhyLHRTXv3iWSVAHM3G/9BgfS4KvjVg9YdrvmocElroene3VVBui0ygyLHtAfLr0DUGDQY77DfJLLfVY8fAc2zvDQrdriLLgOuJjHiO7fR3uW+zKJSQLJvAc1xCBjn22m0fZbm92kPZC8A/CcD3BwGQ7g7YA8RbmeoPTNZhGEPZGWLN+EOJIr/NKgnPcfuotEVdMy0h59DjQ073kWFC3W6rkr9hyXSjjDGZkfAMzD25nXkLdrdlmWQP5Uf2VCiDNzB00c2tof0r8OIJFIock7MJCt7gwC6dH+Tmmj3WcS/yTQaq7A4xdy9QNA9s74z9ACo8HCx09Usr1r5DcxF3RfyT7W5yXUkti/yaybzLLXYstKoUfIayZBtAEUIWuq1JoNsiB2Q1CPsdoMgkZgRvVZT63XhrBSWMM0QYTaw3J9brRz7xDrhWLFtWSkGuLxTlCerFYh1lexbx6Om1VyOQWBc783xlvok17Oq6eeTeimihLkO5D1dz+DeXPyowuChMGCSu42JGHYI9QjycCCpwEgYpCX2HOMRGBwutZKm/pOaMEkX3jTxNAC8EKZ1muMiyWbP1lOZtoR5Z9qkoHgxQV0C+Aav/jb0E/fIWU5aV0L3NdtEE2L3S1tTAe44bhCYTLWisVZH3Wtyf7l/BBnLB1P4CzGwT+z9FlWSnsTYeLqEvDkfeky5j1Pped0rupk4KfHf+4jJ11sDpldNaP6PuRapBLVm12eqVfDRdxmkWTmVfzxeVatqY+egV6JEqPA98et6mX2mxZjbo6k8ntK/wo5IKRKyZrkynM8PHhPq6OwvoqJjGbvY1yfyVwJ9J4L133qtxvkd95kCKgaeAPRAEMxI6mNkkDEM6Fy4AOnzvilw7oN6/GJo6d/+M+gXP8wMaTpe8a5YxriWffWK/h9XZeS07NwJyLTnLTErMUY3D4rGo8p7b2NTp09MJYz4c/kwxiyuVMg5xZkpTFwj13xis/IOF/jdj+7FNvrs5ms3GsjRZrRPab5dtkzSfjfnoNibentACzgn3Q4KBhSn23LmDQ09wHIJtBzwkNrVTdLDOhUSmA1gX2X5XlbOFKiGXXF5cS+8Wz7gI/2ppfREteTbzIaAUz5PEw14gXBwyEeBAMMLTRKQupXXRanBbivde4H+fLaLZWgGXJcy+MQ1qFlfqCfbDyc9PuO3ZJMXC8R3sCRcwJWmARZKEnm/TOU84OvwHPvCR4iEQAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [476] - Fine-grade Aquatic Processing Materials
        [476] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XS2/bOBD+K1pe9iIt9KApyTfHTbIB7GxQZ9FD0MNIGtmEZdGhqKZp4P++oB62JD+6LXLooTd5OPzmm+G8/EYmpRJTKFQxTZdk/Eauc4gynGQZGStZokn04YzneDhM2qO7hIzdIDTJg+RCcvVKxo5J7orrr3FWJpgcxFp/V2PNhYhXGqz6cPVXhcMCk9xuH1cSi5XIEjJ2bLuHfBm6wgj93g37u2Smq3LTMqCOTQcUwgGF9pbIMozVmYhQx3a6t9zvsxAy4ZCdwXNcxnoxps21G16srl+x6DgwGjgwGvUcYO0bwBoXK56qK+CVG1pQtIKFgnhdkPGoiSoLjnG7qGGD+gCKYx5jhw8b3mP9gLrtVcm/4RRUnRmn0owFR2Du4HW8BuxxBRmHdXEDX4TUeD1B651n9uUfMRZfUJKxo2N2hgLtGWzDecWXt7Cp/J7kywxl0RpxTfKJq9Uig20LdQLZ82165FzPUrDbmeT6q5LQlKl+p0exeIHtXa5KrrjIb4HnbegtxySzUuIciwKWSMaEmOS+4kjuRY7ErBFet0jGOm4n8GaiUD+N9yCxwNMMiUXOnNcWq/MDn8UWYyUhm5ZSYq7eycsB6rv5epLtkccnrVdadf4slNjq6ub5cqFwW7XVA/cmxybyfSh34SoO/+b8uUSNS9wgDilz0IoS8C3qpKkV2RFaHnVSN0KWQJyQnUlmvFD/pNpGQcZPdXpqB/YEw/A8w0mWGfXVIc17ITeQ/S3EWgO1/eUTQvW7riR9WqDSnrQ1Nee5lj7yja5nl/5lm2QOX7uyQMt09+/rOt5e3tN3Kv3GVM2POq4f6FA2rBZKiryqzUZtX+gpZMW+8E/QHcDaXgd1hkvME5Cv7wX8QZRRtg9hT8Nl4V7hyJtjlVPUulqPkm/PWfJHrrdXOWerp3TBWqOni2aSKpRTKJcrNeMbPduc+mBYTdVWU8p6eOqPzlg42aFH4XAGXlwv9EbStrU2fz/ic8klJgsFqtTzVa88w6QevJkfXkqx/50yv1Pgx1KgfXP6g2/eaZ3M9cMo9Bwr9m3Pon7KLGCQWm4Uh9SLvdDBiOw+t72zWYuf9oK6fT69kW4fpaPA9oaddGJsYJnzlMeYKyPluRFJhHiFhaFWaLyAQvlnYRSlTCHGPw6N9wPItTGV4iVXwDPjCpWCXg92LsX0LsFc8RgyHUhNsFaYbESZ99Q07XC433j9zVR30kVNsF6WWn+Pym649Y12Jvll/lMchvRPj2Z9WUumOoxVBLvDuhnR+rMWH9RO5XhvlEOAkAYWiwAsSmNmRaPAsTwKDvMg8Dw6IjtzmG/+pck9FyKXIl4bU8iT118ndU79K/udSeY7ZVISIfgRhFaMmFp0FPlW4HuRxZI0cRl1HYS46mw1bkPxifrsszEZGzc8R+tWQoLG5LkExWPjQYoYi4LnS2OuuxWHTC+DHZtBFMXMZp4VQZJYlNrUimLGLJuxMB2FqR3YCdn9BzzfoCjPEAAA",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [477] - EX: Large Aquatic Specimens
        [477] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1WS2/jNhD+KwZPLSAWEkU9b16vNxvASYM43W0R7IEiRzYRWXIoaps08H8vqEcs+YkNcijQvVHDmW++GQ1n5gWNK11MWKnLSbpA8Qua5izJYJxlKNaqAguZy5nMYXspuqtLgWISRha6UbJQUj+j2LHQZTl94lklQGzFRn/TYF0VBV8asPpAzKnG8UMLXazvlgrKZZEJFDu2PUA+DV1jRMHAwj5LZrKsVh0D6tj0DIXOqsgy4Lpn6PTVyHm3hRKSZUdS6hDfHySVtmafZLmcPkPZc+ztMPa8AWO/Szp7gPlSpvoDkzVvIyg7wVwz/lCi2GvT6If7uH3UqEW9YVpCzqHHx9+184cZJJ2pkv/AhOmmFA7VlR/ugZGd3+G2YHdLlkn2UH5i3wtl8AaCLjrXGspvgRffQaHYMTk7QoEOHHbp/CAXF2xVxz3OFxmosnNi/r1AsRvYdI/9ACrcbCw0fdKKtQ/P/Ii7Yv43W1/mupJaFvkFk3mXW+xYaFYpuIKyZAtAMUIWuq5JoOsiB2Q1CM9rQLFJzAG8WVHqN+PdKCjhMEOE0ZH7xmN9v+UzXwPXimWTSinI9TtFuYP6brEeZLsX8UHvtVZTIHNdrM3zlflirmFdN8ot97aIxup9KPfhag5/5PKxAoOLnDDhLhcRdsLExZQkAifMITixCfG4F0WCA9pYaCZL/XtqfJQovm/K0wTwSjCKjjMcZ9moMd2leV2oFcs+F8WDAeoayFdg9beRl6Bf32LKshK6t9lemgC7V9qKGnjqBKYxdZhzrYq8N9IOmF/J3Ejv5Mq0Adf+zbbQFXvqywIj23Fjuz03M1hALph6fgf+NfDHokqy3Yw0GsSPXhW24R1VOUStr3Wn5PqYp8Aj7qvKMV8DpRPeWj3zBsapBjVh1WKpZ3JlZpHTXOw+jnrtqFQz7Myh18YP9Go38KL96X1iEJuVoetSXTnewmMlFYi5Zroy89DsJEdq9EzN/WjJ/CyBHyuBt/7zXidMXZumzAmx7UYBpqEvcMQJwTbnwmcRScME0OZb1wrbvfX+VdB0w/sX1G+L1AvJicb4kamH0YSp9aAxOqcycykg15KzzKTDuGkUxquiygdqxnm0u3S4w30wNJ4qlTIO88z0oYO7F/Ui78zq5W0s9J/Z5LeD9M3j0xgbycRktU5of6C2Y9QcG/FW7VDh9sctDXwhfA97zE4wdX2BmU19zFMe+pQ5vhc5aGPtF1FwPIC5ZoorCepnEf0/iojzJCLgBtiHhGPKicCJlwps88T3vYjwJE3qTtXgthTvaRB8G03/jEczphYwGj9WTEs+MquqXEFejn75PB1/+Wt0e33x63BJJG4KwFOBwU4opgl4OPQiF4eR54aE0sj2Q7T5F4QLwRM9EAAA",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [478] - EX: Assorted Alchemical Materials
        [478] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1WS2/bOBD+KwZPu4AE6EFJlG6q100DONkgyr4Q9EBJI5uILLok1TQN/N8X1MOWFNtpgxz2sDeKHH7zzfDTzDyjuFZ8TqWS82KFome0qGhaQlyWKFKiBgPpwyWr4HCY90eXOYocEhroRjAumHpCkW2gS7n4lpV1DvlhW9vvWqwrzrO1BmsWjl41OD4x0MX2bi1ArnmZo8i2rBHyeegGIwxGN6xXyczX9eZEYNi28ISRM2HUg/CyhEz1kWDbsodmzussuMgZLU8QsR3fH+UYd9c+MrlePIEcOPYmjD1vxNjv34A+QLJmhfpAWcNbb8h+I1E0e5Ao8rqs+uQl7hA17FBvqGJQZaeUgm3Ln8L444Q6PZJg32FOVSuUnoT/ynO43e27NS0ZfZAf6VcuNMBoo4/ONcb7t5DxryBQZOucHVO6T7QiBg77dH5gqwu6aeKOq1UJQvZO9NvnKHIDC79gP4Iiu52BFt+UoN1/qB/ijiePdHtZqZopxqsLyqo+H6ZtoGUt4AqkpCtAEUIGum5IoGteATJahKctoEgn5gjekkv1ZrwbARKOM0QmOnHeemzOD3ySLWRK0HJeCwGVeqcoJ6jvFutRti8iPuq9sWoFkii+1b8vq1aJgm1TNw/cOxHF4n0oD+EaDn9U7EsNGhdlaWDTkPqmR1PPxJRmJnED2wQo0iL3fS/NLbQz0JJJ9XuhfUgU3bfy1AHsCYbhaYZxWc7aq1Oa11xsaPmJ8wcN1BeQv4A23+1PqE8lKB1J/zt2Wy0OtgNdgfrLiRK8Wv3MdcsdXF/CCqqciqefRviN12m55z6ycPxwb3Dgd9JkxOGI1Z1g21OeAs9x9yanfI2Mznjr7LRa40KBmNN6tVZLttFdw24PpjJu5oVatG1JLwYF90hVdQMvnPbZ8Fzj1r2+rye9cG7hS80E5ImiqtadSw8TUzX9mGh+WBv/S+BNEjj15meHtd2oZnmZhVMny0zwiG9i2yNmaGWh6eOcZB7FjgUB2n3ui1Y3cN7vN9q6df+MhgUMe8TBp0vYsq6okI9c5LOk0qobVjL7XIIuc6gUy2ips6K9tQbxhtfVwOzYwOSF06HBHc9zRDuuRUEzSEpdnfpYQu+VWcnbGeg/M4kfOt+b+13ySLd6Z66z2iR02AG7vqeX7fbB7Jh+B1qjxHedwMOmC0Vu4gICMwxS3ySFR/wisFOaFo3WWtyO4j0OyOfZ4u9oFkvJhYJ8FpfZGjZaB7MrqkAwWsrZL58W8Z//zG6vL34dt2VCCmz7hWUWAVATh8Q2Cckcs3DSIsDEczKw0O5fW+14Ib4NAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [479] - 【高難】協助月底魔泉瀑布的技術實驗
        // 來源：上游 Ices-Cosmic-Exploration Main-Branch @ 81d9961 的 Fishing_Sinus.cs。
        // ⚠️ AH6_ 格式，需要同一波出貨的 AutoHook 才吃得下（舊版會靜默把匯入丟掉）。
        [479] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH6_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",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [480] - EX: Foodstuff Emergency
        [480] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACs1X247iOBD9FeTnZJWLc3HeGJbubQl6WwOjXak1DyapgEWIGduZabbFv4+cCySBpmHUI+2bKbtOnarUjVc0LBQfUankKF2i6BWNc7rIYJhlKFKiAAPpywnL4XiZNFcPCYqckBjoSTAumNqhyDbQgxy/xFmRQHIU6/f7CmvKebzSYOXB0acSxw8NdL+drwTIFc8SFNmW1UG+DF1ikKCjYb1LZrQqNg0DbFv4HQqNFs8yiFVL0W4/c943y0XCaPZGSG3H9ztBxbXaHZOr8Q5ky7DXY+x5HcZ+E3S6htmKpeoTZSVvLZCNYKZovJYo8uow+uEpbhuV1KhPVDHIY2jx8ft6fjeCTqMq2H8woqpKhcZqX9vpxd+ttecrmjG6lnf0OxcaoCNo3HGNrvwzxPw7CBTZOkjnctkPdQq0DDbx+8SW93RTOjrMlxkI2RjRHztBkRtY+IR9Byrc7w00flGC1pWmIz/nsx90+5CrginG83vK8iYepm2gSSFgClLSJaAIIQM9liTQI88BGRXCbgso0oE5gzfhUv0y3pMACecZIhO9cV9ZLO+PfGZbiJWg2agQAnL1QV72UD/M17NsTzw+a718VSXITPGtrleWL2cKtmVnPHKvk2goPoZyG67k8CVn3wrQuCiObd+3PWJalheaGDA1aRjHZhJQxyY49T0CaG+gCZPq71TbkCh6rtJTO3AgSMjbDIdZNqhU+zQfudjQ7C/O1xqo6Rj/AC1/V0WobyUo7UlTjrWowsF2oFtOozxTgufLW9Qtt6U+gSXkCRW7mxH+5MUiO3DXL+ZsA6LXSaYsP1zpuv/DMtCUvrRkrq1lHQuOTw4Gjv7VTw4GUppJuKDZce165S8S5oJtqzg0blSS3+Ns4Dk6npWJG93t6N7ucK2uy3OYKhAjWixXasI2ei7a1UW/bssVqBDV4NWH1oQ5M0bcwCP9+XlxF9HrS9NAm0r5DN8KJiCZKaoKPZv1ftQvn+uq5OpiuC7nr8vZ65Kz/eo04a5NmGsz4zelQPPN8Y3fvNWkrYUV4sTFJo7BNnHsBiYhbmim4Ftx6i88qpv016ZL1zv080FQNernV9Tu2NgLwgs9e8p5Lni8Hoxonuw6jdu+FJ6HBHLFYprpmGhb1YPhhhd555lmQPpLkdtdUENtqRApjWGW6e57diPGpwXVXw29vYH+N38tjoP+l8f77AfdaslIR7UMaHvg12NeHyvx8dm57G1lWuASTDycms7CJyamLjFpbKUm9iwvxC4mgReXmVbh1hSfcWh9HYz/jQZ3nCdSFWk6GG9ALCGPdeq0DNgJ8VxMXdPCVmLihQ1muCDYdCzb9lwSegkO0P4n1PWsUnkOAAA=",
            },
            AmountRequired = 18,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Arsenic Axolotl"] = new List<uint>()
                {
                    45836,
                    45883,
                },
                ["Etheirys Croppie"] = new List<uint>()
                {
                    45839,
                },
                ["Moon Mora"] = new List<uint>()
                {
                    45840,
                },
                ["Sunny Jellyfish"] = new List<uint>()
                {
                    45837,
                    45884,
                },
                ["Universal Darkfin"] = new List<uint>()
                {
                    45838,
                    45885,
                },
            },
        },
        // Export for Mission [481] - 【高難+】砷海的生態調查
        // 來源：上游 Ices-Cosmic-Exploration Main-Branch @ 81d9961 的 Fishing_Sinus.cs。
        // ⚠️ AH6_ 格式，需要同一波出貨的 AutoHook 才吃得下（舊版會靜默把匯入丟掉）。
        [481] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH6_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",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [482] - 【高難+】大型水產品的捕獲調查
        // 來源：上游 Ices-Cosmic-Exploration Main-Branch @ 81d9961 的 Fishing_Sinus.cs。
        // ⚠️ AH6_ 格式，需要同一波出貨的 AutoHook 才吃得下（舊版會靜默把匯入丟掉）。
        [482] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH6_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",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [483] - Aquatic Inspection I
        // 🔴 這裡的兩筆**不是死重，不要壓成一筆**。
        //    ImportPresetsSequentially 會對每一筆呼叫 AutoHook 的 CreateAndSelectAnonymousPreset，
        //    而那個 IPC 是「**每筆都 AddNewPreset、但 SelectedPreset 只留最後一筆**」——
        //    也就是說多筆只有最後一筆被「選中」，其餘的仍然**存在於 AutoHook 的 preset 清單裡**。
        //    這兩筆正是靠這點互相切換的：[483-2] 的 PresetToSwap 指向 "anon_[483-1] …"、
        //    [483-1] 又指回 "anon_[483-2] …"（anon_ 前綴是 CreateAndSelectAnonymousPreset 自己加的）。
        //    砍掉任何一筆，另一筆的 PresetToSwap 就會指向不存在的 preset。
        [483] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XTW/bOBD9KwbPJiBKpET65nrTrAEnG9RZ9BAUC0oa2URk0aGottnA/72gZMWSv9IGAXYXm5tMzrx5M3qaGT+hcWX1RJa2nGQLNHpCF4WMcxjnORpZU8EQucuZKmB3mbZX0xSNfC6G6MYobZR9RCMyRNPy4nuSVymku2Nnv2mwrrROlg6sfvDdU40T8iG6XN8uDZRLnadoRDyvh3weusYQUc/De5HMZFmtTiRGiUdfYNSC6DyHxLaZUOKRrpn/MgttUiXzE0SIH4a9GtOt20dVLi8eoewEZnuMGesxDtt3IO9hvlSZ/SBVzdsdlO3B3MrkvkQjtq1qyA9xu6hii3ojrYIigQ6fcN8v7FfQb12N+hsm0jbKaKPue/t79Q+23rdLmSt5X36UX7VxAL2DNp1g2D//BIn+CgaNiCvSMWmH3EmgE7Ct3we1uJSrOtFxscjBlG0Q97JTNAoijx6w70HxzWaILr5bI7cfnqv8rZ5/k+tpYStllS4upSraemAyRLPKwBWUpVwAGiE0RNc1CXStC0DDBuFxDWjkCnMEb6ZL+2q8GwMlHGeIMDpx30Ss73d85mtIrJH5pDIGCvtGWe6hvlmuR9keZHw0em3VCGRu9dp9r6pYzC2s60a5474V0di8DeUuXM3hz0I9VOBwUeZJ4UcEMAAnmPIgwZwHAeaMhZCxEDwKaDNEM1XaPzIXo0Sju0aeLoFngkKcZjjO80Hjuk/zWpuVzH/X+t4BtR3jM8j6d/MRutsSrMuk/Ry3Rw0OJZFrOa3z3BpdLH7F3Qs67jNYQJFK8/jLCL/pKs6fufcs/FA8G+z4nTTpcThidWvU+lSkiPnBs8mpWD2jM9G2dk6t48yCmchqsbQztXJjgjQX+zKuF4TKNHPIPXQa7pGuGkRMHA7WMzPSDfe2n7TC+QQPlTKQzq20lRtVbnvYV9PPieantfEugVdJ4LXvvNOzIkFTmQqOPS+IMI1ogmOPZ5iKzONcstijEdp8aZvWdsO8ez5o+tbdE+o2MMoit4ycamFXWhfflmrd62DkXGGmKRRWJTJ31XBRGoPxSldFz8zFFvvbQdDf1LiLVJlMJjDPXRs6vqMywV7YkdhmiP41K/du4r16zjlndzJxVa0L2p1823nnHpvjndkx3XY0BgEnkDGGSRbHmDIZ4lgQhlkALGIyECxO0GZ4oCF2ZgzOqkKawTzRZq108a6k/4eSJJOM0DDBEY1DTCHiWLAwxSISCQ88xhLqHVFS/XfnvJLGBaz2M/lHhfRfVE772lv1HyrJlcI//S7mFvJcmsFnbVboqI52oinbKHvSkoUu/rqjPMDky2A8GowfKmlVMpgWpfsjoXQxmL5OfYQIwZI0wYxkKaaMRTgGn2AGaeLL1PfiWB5V35k+dqPzx3VVDuZVnlVG9bf6dwG+CxD1BmmWCc4llmlGMU0yhmUoGY5JxgNCgfNM1Mtag7vNqabin6PS7bCCM0ZYgn1CE0wFz7DkAJiIKBUhAZ/GEdr8AA7htI3cFAAA",
                "AH4_H4sIAAAAAAAACu1XTW/jNhD9KwbPJiCJ1OfN62ZTA04axCl6CBYFSY1sIrLoUNTupoH/e0HJiiVbdnaDHLZobjI58+bN8HGGfkaTyqgpK005zZYoeUYXBeM5TPIcJUZXMEZ2cy4L2G+m7dYsRYkXxWN0o6XS0jyhxB2jWXnxXeRVCul+2dpvG6wrpcTKgtUfnv2qcYJojC43dysN5UrlKUpcx+khn4euMeKw5+G8Sma6qtYnEqOuQ19h1IKoPAdh2kyo67hdM+91FkqnkuUniLheEPRqTHdun2W5uniCshPYP2Ds+z3GQXsG7AEWK5mZT0zWvO1C2S4sDBMPJUr8XVWD6Bi3ixrvUG+YkVAI6PAJDv2CfgW91lXLf2DKTKOMNuqht3dQf7LzvluxXLKH8jP7qrQF6C206ZBxf/0WhPoKGiWuLdKQtIPISqATsK3fJ7m8ZOs60UmxzEGXbRB72ClKSOjQI/Y9qGi7HaOL70az3cWzlb9Ti29sMytMJY1UxSWTRVsP7I7RvNJwBWXJloAShMbouiaBrlUBaNwgPG0AJbYwA3hzVZo3491oKGGYIcLoxH4Tsd7f81lsQBjN8mmlNRTmnbI8QH23XAfZHmU8GL22agSyMGpj76sslgsDm7pR7rnvRDTR70O5C1dz+LOQjxVYXETCkJOURthJBcMUgOHISTkOY9/lWRwR4jloO0ZzWZo/MhujRMl9I0+bwAvBOD7NcJLno8b1kOa10muW/67UgwVqO8ZfwOrfzSW0uyUYm0l7HXdLDQ51Q9tyWueF0apY/oy7Qzruc1hCkTL99NMIv6mK5y/cexZeEL8Y7PmdNOlxGLC603JzKlLoe+TF5FSsntGZaDs7q9ZJZkBPWbVcmblc2zHhNhuHMq4fCJVu5pD96DTcga5KQj8+HqxnZqQd7m0/aYVzC4+V1JAuDDOVHVX29XCoph8TzQ9r40MCb5LAW8+807PCIGNeCB6OiRthChHHUcZTzDlljGcBESFD2y9t09q9MO9fFpq+df+Mug2M+qF9jJxqYVdKFd9WctPrYO65wsxSKIwULLfVsFEag8laVUXPzMaOD18HpP9Si2ykSmdMwCK3bWj4jerH/itvJH87Rr/Mk3s/8d4856yzXZnaqtYF7U6+3byzn83y3mxItx2N+VQQwoIAE0o4pq5HcJSmgP1QpL4TCQHMRdvxkYb8M2NwXhVMjxZC6Y1UxYeS/h9Kin3iCN+JMSPgY8qcELMgDDAn4IjAc1nq8gElRU54OoFplRvQo4kuoZBC/jpS+i9qpz34Vv/HWrKloGdOQ5VrKUZzALFCg0ray6ZsoxyIixWq+PueRgR7X0aTZDR5rJiRYjQrSvtXQqpiNHub/nhEwU5M7BPuYZpRjiPKHRxHns8dQokb0EH9RWc6GVtDYZSu1qNbWCqjZNm/UB8i/BAh6oiQAfjC5Sn2WSgwFVmAY8h8HFBOmRMLEUZu/WRrcHc51VTcc1S6EzvmnHCPYCBRjCkXHo49AJwSIUSa8Yz6Htr+C4GLjjLiFAAA",
            },
            AmountRequired = 4,
            UniqueFish = true,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Culter Arsenici"] = new List<uint>()
                {
                    45807,
                },
                ["Lamentorum Regotoise"] = new List<uint>()
                {
                    45808,
                },
                ["Lunar Anemone"] = new List<uint>()
                {
                    45806,
                },
                ["Lunar Scorpion"] = new List<uint>()
                {
                    45759,
                    45773,
                    45794,
                    45804,
                    45815,
                    45865,
                    45895,
                    45923,
                },
                ["Moonwhip"] = new List<uint>()
                {
                    45760,
                    45774,
                    45795,
                    45805,
                    45816,
                    45866,
                    45924,
                },
                ["Polypus Sulfuris"] = new List<uint>()
                {
                    45809,
                },
            },
        },
        // Export for Mission [484] - 【高難】檢驗釣場的多樣性
        // 來源：上游 Ices-Cosmic-Exploration Main-Branch @ 81d9961 的 Fishing_Sinus.cs。
        // ⚠️ AH6_ 格式，需要同一波出貨的 AutoHook 才吃得下（舊版會靜默把匯入丟掉）。
        [484] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH6_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",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [485] - Fine-grade Water Filter Materials I
        [485] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1WTW/bOBD9KwbPEiDqW7q53iQ14KRBnUUPQQ9jaSQTYUSXpLrNFv7vC0qibfkjKYIc9yRqZvjmDTl85G8ybbWYgdJqVtUk/02uGlhxnHJOci1bdIhxLliDe2dpXfOS5H6aOeReMiGZfiE5dchcXf0qeFtiuTeb+G2PdStEsTZg3cA3ow4nTh1ys3lYS1RrwUuSU88bIb8O3WFkyWiG9yaZ2bp9vlBYSL3wDUYWRHCOhbaVhNSjh2H+2yyELBnwC0SoH8ejNQ6HaddMra9eUB0kjo4YR9GIcWz3AJ5wuWaV/gSs420MyhqWGoonRfJoWNU4PcU9RM0G1HvQDJsCD/jEx/Pi8Qr6dqpk/+IMdN8Z59osTk/A/KPtCAawhzVwBk/qGn4KafBGBltd4IztX7EQP1GSnJo1u0AhHCW0y/mJ1Tfw3NU9bWqOUtkkZu9LkgeJF56wH0Gl261Drn5pCcM5NBvxIJb/wGbe6JZpJpobYI1dW5c6ZNFKvEWloEaSE+KQu44EuRMNEqdHeNkgyc3CnMFbCKXfjXcvUeF5hsQlF/x9xs6/57PcYKEl8FkrJTb6g6o8Qv2wWs+yPan4bPYuqm+QpRYbc3xZUy81bjrd3HMfmmgqP4byIVzH4e+G/WjR4JKy8gB9im4R08INYVW4K0jRharK4sz30ioJyNYhC6b0l8rkUCR/7NvTFLAjmGWXGU45n/RTj2neCfkM/LMQTwbICsg3hO7f2BXq3VmsgCu0Z3NwmgLtKR1MPXxIEyNMFnOppWjqD0D1ggPUBdbYlCBf9rL1hwh/iXbFjyvtI/w42wWc0D4NGXE4E/Ug2eZSpiTyg13IpVyjoFeyDXGmt6eVRjmDtl7rBXs2dwztHcdN370uWtlfYmZwIM9nNDhIouz0Vn7lgjUvA6s+ts2+4o+WSSyXGnRr7jnz9LjQe3/WS2/3xv8t8K4WmL9zzw8UjhYxLYuocjNagBuuMHRTHzPX9yEokjgJE5qR7XcrccPz9HFn6FXO/PeaOmjaY5hG3yfTfHLNGnRrCSVOvoFGOblm3HxuzQ8DribzseSmUeAX2SpwKa4CN/So50IZlG4aVckqDMMyrYBs/wPTujovmgsAAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [486] - 【高難】採集精密淨水裝置所需的材料
        // 來源：上游 Ices-Cosmic-Exploration Main-Branch @ 81d9961 的 Fishing_Sinus.cs。
        // ⚠️ AH6_ 格式，需要同一波出貨的 AutoHook 才吃得下（舊版會靜默把匯入丟掉）。
        [486] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH6_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",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            // 🔑 2026-08-06 補上：486 是**表上完全沒有數量資訊**的時間型任務
            //    （WKSMissionToDo[43]：RequiredItem[] 全 0、Unknown17 也是 0），
            //    在此之前 Task_CheckScore.TimeGradedFishRequirementsMet 的三層依據會**全部落空**
            //    ⇒ 不宣稱達標 ⇒ 一路釣到逾時才放棄，整段任務時間全部浪費。
            //    補了 RequiredFish 之後走第 ② 層「至少一條目標魚在身上」。
            //
            //    魚種來源＝**上面那串 AH6_ preset 自己的 ListOfFish**（離線解碼，
            //    ~/.claude/tools/ahpreset/ahpreset.py）。這條來源對得起來的證據有三條：
            //    ① 校準：對 469 解出來的 5 個 ListOfFish 物種，與下方 469 既有的 5 個
            //       RequiredFish 鍵**逐一相同**；463（4 種）同樣全同。
            //    ② 486 的 PlaceName 是 5206「儲淚池」，而解出來的魚正是
            //       淚滴刀背魚／淚蟹／淚鯧 這一組「淚」字系 —— 地點與魚名互證。
            //    ③ 45847..45851 是連續 id 區塊，正是同一個釣點的魚群。
            //    道具 id 已用 tools/ahpreset/tc_item_exists.py 驗過：台服 5 個全部有名字，
            //    不是 47680/47703 那種「列存在但 Name 空」的未實裝佔位列。
            //
            //    ⚠️ 刻意**不收** 45851（淚鯧）：它在 preset 裡是 "Enabled": false。
            //    第 ② 層是「任一條就算達標」，多收一種只會讓交件**更早**觸發；
            //    少收一種最多只是多釣幾竿。方向取保守的那邊。
            //    ⚠️ 另外 45847（嘆息螯蝦）與同在儲淚池的 451／455 目標魚重疊 ——
            //    身上若有那兩個任務留下的存貨，486 會在還沒釣到之前就判定可交件。
            //    這是 ICE 全域「用背包當進度代理」的既有限制，不是這次新增的類別，
            //    但 486 走的是「任一條」規則所以敏感度較高，先記在這裡。
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Astacus Lamentorum"] = new List<uint>()
                {
                    45847,
                },
                ["Teardrop Knifefish"] = new List<uint>()
                {
                    45848,
                },
                ["Weeping Crab"] = new List<uint>()
                {
                    45849,
                },
                ["Silvermoon Tilapia"] = new List<uint>()
                {
                    45850,
                },
            },
        },
        // Export for Mission [487] - 【高難+】採集精密淨水裝置所需的材料
        // 來源：上游 Ices-Cosmic-Exploration Main-Branch @ 81d9961 的 Fishing_Sinus.cs。
        // ⚠️ AH6_ 格式，需要同一波出貨的 AutoHook 才吃得下（舊版會靜默把匯入丟掉）。
        [487] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH6_H4sIAAAAAAAACu1cW2+jvhL/KpVfD6yAAIG8pel2T6TtdtWk2iNVfTAwJFYJ5m9Muz1Vv/uRuSTh1s026fX4qc0Ymxn7NxczHj+gccbpBKc8nYQLNHpAX2PsRTCOIjTiLAMFndCYT3DsQ3RGqb+syBfg45SPY7LCnNC4eAKNQhyloKB5xuIJjSLw+XkYrsmTZbbarcsvwpc0y8dvPCeY/U5iEMxOFzFlUOOr4D+ofk4DNDIcV0HfkvmSQbqkUYBGWq9YPxmhjPB7NNIVNE2//vajLIBgQy4e2xpt7NFbqOgTGgdECDcDLhhc5e9aoNFV9b+PRlfXCsJFj8drBQEaxVkUPT4WspXsPKD8H2OzJsF6CnKhbKchlK7tJNYB5MrZVbrZcofPmWvtYEyJKRQw65s3U9fM501cN4vl0C8IiFIpnhBIf8aUGwed8VmMFwsSL55gUnsGk4PDwoKygOAoNxzxLbCK0FrMwqzMyQp+kTigd+uGDuOiG7a9u3k5vwXm46TN4rbU5gGQtiX2KUmXX+8hbRnMplD19bIaQlnWLitmH5b3M3wDsyUJ+TEmuQYIQloRZhz7NykaWT22yHbaUuwgg/tWmv4TcwKxn7u2CwjFW75iFt0LJOYj9CyV3RTS3smgGW8mJyP/hQnmhZ/r8ti20xLK2M1KD95KqPkSRwTfpKf4ljIxRo1QQXWg1OkX4NNbYGikC/XqQ3HTY+00E/ZbzcQxWXzDArEPaBwvImBpJb3RaUIHQ81srfYuEjovKOED4miEZhzzLB37nNzC5AQpKBE9SJCi0ZXuaOb1Yy59PhHKn7sYQ0fb7vJ0IJhFnCwpvel1qoZmNV2PvsO8HS7Y2rjI7gDxN2e4tq9Yr/sFpMAnNIs5sJ9M/Jjd4WQt3illPuQmvkUNBFmIryn55uXcBxznPq4xRbXGcRTNOE3S7tZZQjuHFPJ10fdREgXNGVksgAlAXCvoMib/ZPlbUBDqoevqmur5A1BNy7VV1/GwCq4zAMtxtaHjo0exeuOYxvcrmm3k+U5Sfh6KuRHjtuZbNAjO8yBAYMdybV1B3zMGZ5CmeAFohJCCfuSKi04FBO4wB3Y0ZnzJaJQxQMUw8/tEeKNHBf2gbIWjf5cQvYB/MsIgKBQgn4XKof0CnD8iHk2BN3grfpZtxYIXDSWpeKGpG0NHQZcp5IqRFD1EW3qce0hWdbtMYcOZeGDdXs5VvfWMxGikfdFadPy7pF+m8JOBT1JC49aYlXY12vNRdUd0b7fVRqZ3wMKsl9lm+4bdVsv2sDMOUYRZD7uN1nxMwxBdmy3rMffDfDVeV+Rbn/bu2Lgxg/sx05y4rlc25mFPt1gpwowzWuyS9lMFbfAHTSjB87qqUCG2Uxe0d6cK5ahduqAdQBWKcOCY8HwnyTaxABORgFA27csO0UCf7vwt6PfWsvKhfXWn0sFSI77DAuIAs/supajtUKVWfAateBMHcVjoXqZwQrMSkZsADIovLamPk672gvTXkVDZu2b/DfHFSZp/CfQXBnoB2WfELBK00jq/LWifDCskbj9brP05ooo5qzZz3VFFR3tBOkxUMbQMua2UUH95qBegPVRcIWH7nix0gYIPB8YDxgsSj+8Jj584YhATRTO+JfkyW7WIlylMspTTVZGXqUUP+Vm6jBVnQ8Q/W2nqIoU45hxWCd/EIxmDOWYLwYbWk8213OapC3G+6zXSkq3F1nf4FOt+0RRLfIpVEIlvq2RuM61bze3fde5NCJTL1LX0W6vYuezTmGc5sS/xZomjgc9OvXXZNFMfyi9O78akfTgXu0f2qxuNMv0l0fiymScJyA/7jejDmUeZT5LHCT4wfGWW6LOebPmgUJS5H4nG94BGmdGRBwU/tDmVeZrPe2r1g4JR5mkkHt8JHt9R9mWrKEykX9oF7q9eFfbM3Iao0RqHHNimPmzL4tFEnIUh8WLGIcmTO7M7svIw4YWtEvMoLGdJ3Ex056vKp9bplL/q/YNyEt6fx7PM9yHNV7B1at1f0skS83V91foSDMzn8FsklZCCTkiaRPheFEzOKU43r11TWs/m1JwB4uc3aWxOA9WfP41wupzj9MbDbOpvPXfMSJzXaJ5SBgtGs3jD9jFAsiVXTi1XpmtFt8rXfBdCwxloqu0MDdV0hrqKNdNWQfMD3/Y8jD2MRB6sqFUrcXi1JhT1ae3atXrdmuMY/XVrJwDJitL4aII9uvJwrWRN/wPApgHEnPg4EorZU5ks6uaeVRj/krWqvVnGWcZC7MMsEqmUniLSdrZ2x0pr6y0kkrej7GWaZwlmIJwfFhrfjQhRjt1O4PdDQqjnNJjTyRL8m7WGbt00or0JUN5/9XRumWiR5yysm/pESe4PGtcrcAe5E8OJoHRYs6KquhofqWIkuAVWv/yjy6kWl4TsW20m/WMRSpYr1Blm3uGkWKY/eFbT0nEwAKwahuOqpuHaquPbtuq5EICj6WEY2kicVGkCveE6h8N+fJ1RGjPq3xxNcBzcH8hx1h1N03MOpOeUnvOD3Cv2Op5TKKj0nNJzyp3lgTynr2sm6IGpmqGGVRPrA9XVbU21DMvUbYyHWLN28ZyO1u85zxMcHX3LkuSzec3K9MldpLxj85V9YfNOxINsIkv25dZQbg0/jYOz7EHgWTqotu/pqmkHmuo5EKqW7hiBbZuDgbnb1tB5emu4IFEk3Zv8SPp/cIW0dG/yy6f88vk+3JvvhXYQGrYKruepphf6qovBVcHHmuH7lq5pbp5TFK4qWA9W3q05Tb9F1BOJ4dpH8dKtXZnO8Pro63/+NTo6JTGoC4YDOPqV33p5SiLx50z8IDhKj6bTKapx5pkAgad5qqObA9U0DVt1bHegYtsaWgE2AtPy0OP/AI86kYFhYQAA",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [488] - EX: Coexisting Species I
        [488] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XbW+jRhD+K9Z+aiU4sbALLN98rpOL5KTROb1rFd2HBQZ7FQy+Zbkmjfzfq+XFBgyJekqlSr1veHb2mWdm583PaF6qfMELVSySDQqe0TLjYQrzNEWBkiUYSB+uRAanw7g9uopRYPvMQLdS5FKoJxRgA10Vy8coLWOIT2Ktf6ixrvM82mqw6sPWXxWO6xvocn+3lVBs8zRGAbasHvLL0BUG83o3rFfJLLblbsIxgi0yYOTSPqMWJE9TiFTrCcEW7qrZr7PIZSx4OkEE267bizFprl2IYrt8gqJjmA4Y0z5jt30D/gDrrUjUey4q3lpQtIK14tFDgQLaRNX1z3G7qKxBveVKQBZBh487vOf2I2i3V6X4CxZc1ZkxlmaufwZmDxLEacDutjwV/KG44N9yqfF6gtY7x+jLP0KUfwOJAqxjNkGB9Ay24XwvNpd8V/k9zzYpyKI1ot8+RoHjWeSMfQ/KPxwMtHxUkjd1qB/iLl//yfdXmSqFEnl2yUXWxtbEBlqVEq6hKPgGUICQgW4qEugmzwAZNcLTHlCgAzOCt8oL9d14txIKGGeITDRxXluszk981nuIlOTpopQSMvVGXg5Q38zXUbZnHo9ar7TqBFmrfK/LV2SbtYJ91TdP3Jskmsu3odyFqzj8lomvJWhcxGhCkxA7ZhRGxCQec02OOZi2w3CSELCSOEYHA61EoX5NtI0CBfd1emoHjgQZm2Y4T9NZfXVI8yaXO55+yPMHDdQ2kM/Aq991EerTApT2pC1HLboTO5CDMr0W2fFI1+c7y0DX/LEjsy0t021/7L6W9zHYO7cR92CwrWEaZrU7BNueryPfOLFWMs82b+BGTXngBv1nbtR8R/ygr/qxgg1kMZdPr7pyBvFLXobp8TV7KrbLjgqnSE2q9EiMaN1JsZ+y5FHbOapM2eopvWCt0dP1O08UyAUvN1u1Ejs9R3F9MCzsaoMqZT2o9UdnBNXTgbLhqvHi7qLXnbaltrXzEb6WQkK8VlyVenjrfWpYUINX8thovg60LGcqG8YUf7z5v/bmnbYdxZELmLmmSwg3SYKZGZLQMhm2fJvaLlg4RIcvbd9udu77o6Bu3ffPqNvDCfWpP93FF3nIUzWrbnT7OH4pNlcxZEpEPNUB0YZqhfkuL7OO2tj+TdlwZXL626xuMetSJjyCdapbUesGo69sivRgoP/M/5DT3P/uaa8va8lCR7UKaHf+N1Nff9bik9pY6nbSDEPoEtvxTDv2mUmAWSbziGd6DvMJtyNwsYUOxnkaudMOXPKUR0pEs4uUS0h+ZNP/Jpsii3oWppbpQxibhNHEDD2KTQ7EtbDj+B71qqZV4zYU74nvf5ktfw9mixweRaFEtpnp/VpAMbua/fRhOf/0x0xmm5/7m63HwaWcENPjPDIJptxkCaEmZlYcWzEAxC46/A13tRnrAREAAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [489] - 【高難+】調查共生物種的生態
        // 來源：上游 Ices-Cosmic-Exploration Main-Branch @ 81d9961 的 Fishing_Sinus.cs。
        // ⚠️ AH6_ 格式，需要同一波出貨的 AutoHook 才吃得下（舊版會靜默把匯入丟掉）。
        [489] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH6_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",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [490] - 採集魔泉環境的樣本
        // 來源：上游 Ices-Cosmic-Exploration Main-Branch @ 81d9961 的 Fishing_Sinus.cs。
        // ⚠️ AH6_ 格式，需要同一波出貨的 AutoHook 才吃得下（舊版會靜默把匯入丟掉）。
        [490] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH6_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",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [491] - 【高難】採集魔泉環境的樣本
        // 來源：上游 Ices-Cosmic-Exploration Main-Branch @ 81d9961 的 Fishing_Sinus.cs。
        // ⚠️ AH6_ 格式，需要同一波出貨的 AutoHook 才吃得下（舊版會靜默把匯入丟掉）。
        [491] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH6_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",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [492] - 【高難+】採集魔泉環境的樣本
        // 來源：上游 Ices-Cosmic-Exploration Main-Branch @ 81d9961 的 Fishing_Sinus.cs。
        // ⚠️ AH6_ 格式，需要同一波出貨的 AutoHook 才吃得下（舊版會靜默把匯入丟掉）。
        [492] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH6_H4sIAAAAAAAACu1dW2/bOhL+Kwb3caUD3S/Gvjhu2jWQtEXtoAsUeaCokU1EFn0oKmlOkf++oCRfZMuJ4ziJk/AtHl40Q87MN+SQzB/UKwTr41zk/WSMun/QaYajFHppirqCF6ChTywTfZwRSM8ZI5M5+QcQnIteRqdYUJZVNVA3wWkOGhoVPOuzNAUiviXJgtyfFNPdmvykYsKKsv+1epLZM5qBZHYwzhiHBl8V//H85yBGXSsINfRlNppwyCcsjVHX2CrWd04Zp+IWdU0NDfLT3yQtYoiX5KraSm+9iF3DnN5nWUylcEMQksFp+a0x6v6a/01Q99elhnDV4u5SQ4C6WZGmd3eVbDU7f1D5h7Wck3gxBKVQXrAmlGnsJNYB5CrZ1drZCv19xtp4pcGWCtkY4aXWOKbh7DfC7bLUg/ScwlTWs01jHNMw95gb62D6InkcZng8ptn4HiaNPZi0D8pkn/GY4rT0MNk18DlhYzIr/zOiU/hJs5jdLApa9Mm0PG93P/TtGjjBs00WV6UOD6BpK2J/pvnk9BbyDc+6LlRzvtw1oVz3KSa/L/Pn+AqGE5qIE0xLE5CEfE4YCkyuctR1t3gtL9gU4wnO9Plt/TsWFDJSouAPSORXTjFPb6Uulj1smSxvXUpvp8myXk1OTv+BPhYVJLaBuxdsCGXt5qft1xJqNMEpxVf5Z3zNuOyjQZjrqq016T+AsGvgqGtKA9umxuuYtdNIeK81Eid0/AVLjf2Detk4BZ7PpbdanajtG87GbO8iYfCMEv5BAnXRUGBR5D0i6DX0PyENzWQLGueo+8sMDPvyrpS+HAjt4SaWHxirTe6PGYtU0AljV1th1TLcdfAxdxi3A8RltY9egmR7LPlbcNxYgizm/QfkIPqsyATw71z+GN7g2UK8z4wTKH38BjWWZCm+oZXrnG8EcFai3NoQNQp7aToUbJa3lw5nrLVLKV8b/SlGoqERp+MxcKkQlxq6yOjfRfkVFBsRtmNIdCO0Qt1JzFgPPdvRE8cPTM8MIXBcdCdnr5ex7HbKiqU8ZzQX3xI5NrLfjfGWBZLzMgyQuuOGnqOhs4LDOeQ5HgPqIqShr6Xhoj7Lp5R0zgDIBFWtR7czCUJ3GvrK+BSn/6018wf8XVAOcaX3pfBzHPsJuKwiq+Yg1liqftZl1TxXBTWp+qBj+qGGLnIozWFWNZBF+UmJi3wxAhc5LDmTNdYrNEvPaYa6xl/GBh3/rukXOXznQGhOWbatz40Ky243ixo9sxvgSbGV2fXylX7XS1a7HQpIU8y39bpWvOx0vWDR5+O9ZeXsN12fH1xqaEqzORiYGmIz1EX/QY9zoqXf1RDNrudtHvCnlUZJsZ5mt009aY/i16a8tdLa/LXVWZuOVu86t7Kh4Kxaez3Nzgxb2dlbsjNlHbtYxxmMIYsxv1UG8u6AyFwxEHPVQE6oKDdw+BI7uMQg5y9DM/5qhOA14Mg9w5YWpr3ZYgUeNGWb+yDXRQ6fWFGD0jKChWqzKid41lZekR4dU9atG2BnyU07FVMekSk/Igy7L246shCtUtk9AjSltG8Bf96z0u4XNym9VXr7Wnp7kcOIz3eI2qOKlvKKdJiowncttYZWLvr5Vb1S2kPFFUptVWTxgmp7wMhCaa7S3JfRXDoFVoiV1UC9X9QgXuTQL3LBptUGbSPOKE84Frw6iCP/WDkRUGVre0LAdCaWkUvBYYT5WLKxLXHuhpvH2V4mA7yxAfeSefMHNu3kZLhy1649S7TDB33PPuCW30GE3bKnaZoHEfbBfJq2R7rvmfN6S7tsM+sVC2016UEmipK4LZHtysO4e6ey2/BK5bKPCa4qNXlDGzNPSPi2a6PK+CptfLI2Hi6cVwqp3OPT9v9UVlGdVHur6K5yhe/10OQbVUWVAVTaeAzaqPJ66gz6m3anKlv3fi9EvFFlVDk4pY9Hoo+vnFnbcuv2NVNrzZHZJ7chrzr2EgF8ec1yxeOxmTwRRbPxUMCsTOINb+g0wlRUvkqOo/ScNXE50K2fqmst0imPav2VCZrcfsuGBSGQlzO4PhunZML6EywW1xQXz85gMYLfMquENPSJ5rMU38p7xyOG8+VnF5SNuiW1ZICS8u2a5ZmwZv3PKc4nI5xfRZgPyEq9E06z8qrzZ8ZhzFmRLdk+AZityFVS65lpm9GVW6CRQRKLxIbuBG6gO0bk6zjxQcdBaJiR4/skCZHMg1VXPms9/LUgVNc8V6+Azq/PNu5/BvIBna33PwVkbDZhHHfOigxzmjdugZoP6NgghkxQglNpm1uvL7vh+q1ve6dXJp7z2vfWTOOw4AkmMEzl7vVWgdz9Hi1wX0Mi9SbRk9zzcIY5SADE0urbNUK+bOA+4r0haaGDeMT6EyBXCyNdebbHeBVFOf6HCErXxKrUUuXgdHO7d/vKMmj4M7sEMjyTlBZ3Vj1QMO8f/XJC67Jz+r9/dzs9EBPgjExgKt1dZ4insxTyzmAw6OgdS34SroE3n9xpQ+DqaZ6n3k9WYFrFnfVUtsakN3hWzecDMAyeZZLQ8HVseLHuxFGo4wBj3Y8sDLEZEDOKkDzXspbsXEPZ0Niuh985E2x6ewWdf/mWqRC2HWFXXm87SoB9/GEoBcmv/Uzgy4C3tH0F3gq8FXi/NHgTz8PYN13d8gNPd3zP00PPDHUCsRdiA/w4Ik3wbl8jh/dEkT0+ZhkuxHtdIs/XOWrhqx7jffmFr7S8F8LOOoCrNyxLM11GcNdyP9RePFWlITr3DDsFdGrprJbOHw59DWISw48c3TOxpTtGEusB8Sw98bDlGqYd2F64y9LZ3g6+54xlnXOcCfyxUPfYF8NqafumQdc+ctCVG7UKdNV+9ftK/h4GdF3sx67l65ZnBLqDDayHEQl0MEwwYiOIksDcBXTveRX4K44p7/SmUZEyAvKS5IfCXpXq/Wj/fuZlwdc5cvB1FPiqZPG7O3l1EPC1nTi2YtfWfRsnumP6nh552NMdEoZeEvgQucku4Gvds91cHjWg8pDBDePxx4LeY1/2qhzwB8Nq6+BYXfOvdoybO8a6OkP1dmExJLEXJomnm2Fi6w5OQj2KSaS7ke3YgRmH2HLKo8wS4+JFZ/V/xhnkX1IWyWi0cQ6vxsPdTuGhJkyb2IodYuoYe67u+FGgYyPxdcP2I9MJfTBDC939H5KfNSlAdQAA",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [493] - 【高難+】調查未知水生生物
        // 來源：上游 Ices-Cosmic-Exploration Main-Branch @ 81d9961 的 Fishing_Sinus.cs。
        // ⚠️ AH6_ 格式，需要同一波出貨的 AutoHook 才吃得下（舊版會靜默把匯入丟掉）。
        [493] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH6_H4sIAAAAAAAACu1c22+jOhP/VyK/Hlhxy/UtTds9kdrtqqTaT6r2wcCQWCU2a5t2e6r+759syAUCu2mbs70c+pSOzTDj+XlmYDw8oHEm2QQLKSbxHI0e0AnFQQLjJEEjyTMw0DGjcoJpCMk5Y+FiRb6EEAs5pmSJJWE0n4FGMU4EGGiWcTphSQKhvIjjNXmyyJb7XfKNyAXLNP/KPCXsGaGghJ3OKeNQkiuXP1r9O43QyBkMDfQ5nS04iAVLIjSyGtX6ygnjRN6jkW2gqTj5GSZZBNGGnE/b4jYO2C2s6BNGI6KU80EqAZf6XnM0ul79DtHo+ruBcH7F43cDARrRLEkeH3PdCnEekP7hbGwSrZdAK9UbVJSyrb3UOoBeWlyjXqxh/zlrbR1MKLWECmalddtgwbMt73nrVi9hofrL8PCAJBqhUyIWE5ZROTlGBkrV9FucoJHdMxBRoneHtvOo8bKCTjOKip3UhB7Ptuxn2Mk5qJl8iudzQue/ENJ6hpDuYbHEeESUGR7QlN4CXxF2IJD7ohlZwjdCI3a3HqhBoe30evv7pItb4CFOd0Xc1to7AD631FZgPLkHseNlq0qV7dWtKNXt7mOx3mFlP8c34C9ILI8w0TtAEcSK4Esc3gg06jY4sN5gV4s9dBj+i/7hlzv9K5YEaKjj4SXE6i4nmCf3ComaQ4OpelUle3u5QefV9OTkH5hgmQfHujDfG+wo5ezn29234dwNROhtsQB7+vnZAicE34hTfMu44loirLDuGmX6JYTsFjga2Wp/NqxlNU7utZK914LHEZl/xgrxD2hM5wlwsVLeqd8Cbt/yduCyj4qD11LxPEskWTB20xgwHatbDSv2HiodLvvahL/6jPGn5Lj0oLFR4BIESL1FgH/l6h//Dqfr4VPGQ9D+e4caKbLS3zL048xFCJjqAFa5RWlwnCS+ZKmoH/VTVstSKVhHrwvOM07mc+Ait+sVJT8yfS2C2ItC241NGw890+t5oTkchD0TcD+2ejgIPBujR2WUMWX0fsmyjZRnRMiLWGms+O44QjWg5NFxO/crPddAZxmHcxACzwGNEDLQF71X0ISJJQk7kwVZAsedb4wvUc5kdp+q8PFooC+ML3Hyd4G7S/iREQ6RL7FUYlkGWkWgb4D1FDVVgKxIlv9bjOVGzAcKUn5Dz+4PDXQlQIM9zS9QQ+JIRzS+XogrARvJ1IzqhPLoOaFoZH2yduj4Z0G/EvCVQ0gEYbSJ586EDdvdoRJndgc8zhqFrY5v8a2ObLP1JSQJ5k1cK8MbptWBNc8//8CSA0BJ8RIXWTVrfbJcsVDtpMpy182prF6tq1ttCl9ylj/ivGxbWG67Ldpt8WG2xRnMgUaY37c7ow0YbcB41M79mGVFLFgFgTPI38SIEKel4SLA56SmxKv0rqYUYoqRUoxx1CupNvN625nX03Ko3C+/sRCQY7Y5L2pR++GeFz4Map+XtrTe9j3g1m56zj0iUtd1+OY5l6PRtd395A23/waG9cn6Xv/Aa+yyVVXKGraDT5ZhO42M3uv+uRIw46sXOrXpzfbw6kyAJh0ovel3nfYZ+m1tuY8ZKnLUHizBaXH75kLFR8btAVOcFrlvDrltkvOv7iCyBJbJLQsUOV6JeCVgkgnJlnkNrZTx6NONGc8P3qgfWyX8vIY7lhKWqdwUBDMOM8znSgyntpjv9rvD6pEWdeLuT9SFf11B+pxWSkd919pKe43GOtPWkYlNyanm7ITx6wxemaOrkvYnnrwoTFoHky2L10JkSmWmiU0F1a462PnskmqdH25rqm/JDecweUdpwQsqmfVobEuZLRpfqYDYAvKtv4p7d+6xUrdbZWUvKty1b5LfawXk3cH3d9W4FootFNsSW3uU9D/nGCsFq7q4/uSKVfv69P2ebXh3AP5dGaoFYwvGtrbUNor8N53jK1dqGvpyValm9/sEf7yF75m1DdVPN44l8E0v31b4ZanKnAid+xJSXYn078gywETm2FDrqMJ4QdwsdO2tilnrcsqTrv7CJInvL6ifhSEIbcGd0xnhgk0WWK675tafMMFyBj9VVQkZ6JiINMH3qvN0xrDY3HZN2ZmrqVoAEurvoGxyx/L80wSLxQyLmwDzabg174gTqptdTxmHOWcZ3Yh9BJBu6aWphWXqLLrVlIh7oYVDwKYbRIHpDYLAxNiNTWy5g0HfdqxoOESqDpZ3IBY4vF4T8q7D3Y7ESjeiKtk1dSMeA6RLxmjHBxxxPGe01Ixo/wZi0wioJCFO1NZs7JHtDqtdv+5eHyl4lbZfP+MxDsFP1MvrRoW6z+t6776GRu3nbV7knf0Uc1DxD6tN/9DY2b5b72+GhNqh02jGJgsIb9abdOurL9arAOXtd7tr18TyylLu4Ey72bt9YRRK/szVcQynilLjzvIm+BV/dO0N3e+dk//9NepcUaJdXUwg6ox/ZFiSsOOnEJIlUNGZdsyOo+4Lt8DLn22pi8L5511e2nzbBtRM556FPWvz0juc5kb9TSh2IbaDIHTM2B4OTQ97PRPHw8jsxoN+1wmdIBgEOhQrZEVrZsWHBqbic8IClU+VgFSg8AkwQiWhurEziHE/NrHjBKbneLaJ3V5gBkHseA64tue56PH/vQQ9qlFOAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            // 🔑 2026-08-06 補上：493 是 Unknown17 > 0 的 5 個任務裡**唯一**先前沒有
            //    RequiredFish 的，所以 BackfillAmountRequiredFromSheet() 一直把它跳過
            //    （見該函式的 remarks），AmountRequired 維持 0；而它表上又沒有 RequiredItem[]
            //    ⇒ TimeGradedFishRequirementsMet 的三層依據全部落空，只能釣到逾時再放棄。
            //    填上目標魚之後，那個補值函式會在啟動時自動把 AmountRequired 設成表上的
            //    Unknown17（＝18），交件判定改走「數到 18 條」的數量語意。
            //    **這裡刻意維持 0**：數量的真值來源是資料表，不要手寫第二份。
            //
            //    魚種來源＝上面那串 AH6_ preset 的 ListOfFish（唯一一筆，"Enabled": true），
            //    離線解碼；校準方式與 486 同一套（對 469／463 逐一相符）。
            //    深月海龍在 WKSItemInfo 子分類 2 裡是**獨一無二的名稱**（只有 45912 這一個 id），
            //    所以不存在同名多 id 要不要展開的問題。
            //    tc_item_exists.py 已驗：45912 在台服有名字「深月海龍」，非空佔位列。
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Deepmoon Seadragon"] = new List<uint>()
                {
                    45912,
                },
            },
        },
        [494] = new FishingTools { },
        [495] = new FishingTools { },
        // Export for Mission [508] - EX: Processed Aquatic Metals
        [508] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACtVXXXObOBT9Kx49ww7CAgNvjpukmbHTTHFmHzr7IOCCNcHIkUQaN+P/viM+bMB2s+142u6budI9OudIV1d+Q9NS8RmVSs7SDAVv6LqgUQ7TPEeBEiUYSA/OWQF68C4ruIAF5/EKBSnNJRhNQtJOv0tQYHu+gR4E44KpLQqwge7k9Wuclwkkh7Cev6vxG8Q3VP2wq6U0jusZ6HazXAmQK54nKMCW1UP+PnSF4U96Gda7ZGarcn1woieMYIsMGHkDRi0Iz3OI1Xkc3M2y3yfFRcJofgYP267bs5w0aTdMrq63IFtHCbacgQDH6Qlw2y2hTxCuWKquKKtk6IBsA6Gi8ZNEgdOY7HrHuF1Uv0F9oIpBEUOHjzvMc/uG2m2qYN9gRlV9UE6dOtc7AvO8Pti4AVuuaM7ok7yhL1xovF6gVTc2+vHPEPMXECjA2rMzFEhvwdbOK5bd0nWle1pkOQjZLqL3PkHBeGKRIXu7B+Xtdga6flWC9kp1T0Dvy5KHX+nmrlAlU4wXt5QVrdUmNtC8FLAAKWkGKEDIQPcVJ3TPC0ANwnYDKNA+ncCbc6l+Gu9BgITTDJGJzozXK1bjBz7hBmIlaD4rhYBCXUjlAPViWk+yPVJ8cvVq1g0XMVRF95Vu2s2uD1Go+EaXOCuyUMGmumoPgpqDNhWX0dGFq4g9Fuy5BI2LiGUR4sXETBIHTOLB2PTiyDNh4tru2Ce+gwnaGWjOpPqU6jUkCr68VatpAXuCvn+e4TTPR3XqkOY9F2uaf+T8SQO1l8zfQKtvHZeg9uXSa1zNYNfbJlTDEzzRl1eLGSrBi+wCqNa4gzqHDIqEiu2hrM8gLNkaxKDwF6zYD+kL8y/LQLqJnZqr4/35rp5+itwHXkb5eyb2Em3X3+e9a9TZzJ4ZZ5IX9LUr+UhCjfcoYSnYpjazdaCO/JCsiWNrQ+rMHxTWyz2S9iu2s6WgL4tpqkDMaJmt1JytdWPH9cDwFqlefaWoXw76R6cn1u3K8YdPoe++rfRzrL3U20L9DM8lE5CEiqpSvyb0e+9/Xr2XAP6TK+9E8h9VUf81/bdXw+Ohe44jgiMX2yaJ49QkjhWZUWRhM3U8O47tyCZWgnb/tO2z+bf0ZR+oO6j+rvt10y+vwsXIHD0IHoOUkIymzyVVLB4tQNFcjszRR6Av29FU0VemdO/vUMIxjhI7dkyM08gkvodNL0rATCI7Sid4TFPsod2/sM5WQT8OAAA=",
            },
            AmountRequired = 0,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },

        // Export for Mission [509] - EX: Refined Moon Gel
        [509] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1Xy27bOhD9FYNrERD1lneub5oEcNIiTtELBF2MxJFNhBZdimqbW/jfL6hHLDl2ggZZdJGdRA7PnBkdzox+k1lt1BwqU82LFZn+JmclZBJnUpKp0TU6xG4uRIn7Td5vXXIy9ZLUIZ+1UFqYBzJlDrmszn7lsubI98vWftdiXSmVry1Y8+DZpwYnShxyvr1da6zWSnIyZa47Qn4eusFI49EJ90Uy83W9ORFYwNzgBUY9iJISc9NHEjCXDc28l1kozQXIE0SYF0WjHAfdsY+iWp89YDVwHB4wDsMR46j/BnCPy7UozAcQDW+7UPULSwP5fUWmYZfVKHmKO0RNO9TPYASWOQ74RIfnonEGvf6oFv/hHEyrjN7r4WnvIP9+d/p2DVLAffURfihtAUYLfTi+M16/wVz9QE2mzCbpmLSjxEpg4LDP3wexOodNE+isXEnUVe/EfmxOpn7sBk/Yj6CS3c4hZ7+Mhu7i2czfquVP2F6WphZGqPIcRNnngzKHLGqNV1hVsEIyJcQh1w0Jcq1KJE6L8LBFMrWJOYK3UJV5Nd5njRUeZ0goObHfemz293yWW8yNBjmvtcbSvFGUB6hvFutRtk8iPuq9sWoFsjRqa++rKFdLg9umUO65dyKa6behPIR7yvSn2GQgzEchZfXM/k1dVp/qHuFLKb7XaJkR8HI3izGiSRJwGiSY0yTjjMZJxsKwCNENM7JzyEJU5lNhWVZketcK3KbgsTyEqa0Gp6JcGpQS9GQJcqPKyY1CC3qt9AbkhVL3FqavOF8Rmne7XqF5vMsFyAr7u91t2hj7W94ttVkMWGwrWY+5NFqVg5b48nHXHxxf4ApLDvrhDXg1wP+oOpOHkbYWXpQ+GuxpnzQ5Rm1odavF9pSnOPT8R5NTvkZGz3jr7OzdmBUG9Rzq1dosxMY2JdZuHF6aZhypddv17MOgvB+p4X4cpk/b+DMd2Y4SffXqZXaD32uhkS8NmNo2RjurnNDeC1r6U8m8S+DPJPDab95XvbmqSzM855BPpXz4UuHXNZbXqhlaZz9ASCu1XmmD6pj5jCc8cGmWcKBB7iJNwWeUFVkYgMtYxDOy+9aXx24avntcaCvk3W9yUCo973SpvFDbrShXk0Vdrgp7bNgc2HPpveRYGpGDtDm13lqD2camYW92bDwO08MBxx8Pm4l1XOsCclxKW9seYwlfmOvCnUP+mt+E5RY0FrW8gJL3LOx4Fx7jMZJRlz9/9DPgjoaWV7d7e/hD76j5RsMe3jVt+9gu782OXahhcy94EOYMqB9lAQ2yKKNZCh5F3+NpkHos4TnZOU/l+UwAV0qVUpjJHPT2zaX5ai0e1/S7NP9aaSYQhVBARCPwGA0C5tEs81xaJGmY+Mj83IVj0mTp6QAWdQl6cgMCNlC9a/Ndm/R12vT9NItzcGnkezENWBJTKPKculnKIQbuR2HUdP0Wt6N4F7rpt8nZv9PJDRaiRD6xpXJyjnL8x5WnmRdzXtDCTXIaMDeh4GYZRT/i4Gd5HGeM7P4Hk8nM9cwTAAA=",
            },
            AmountRequired = 0,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },

        // Export for Mission [510] - 【高難+】如水晶一般的寶石
        // 來源：上游 Ices-Cosmic-Exploration Main-Branch @ 81d9961 的 Fishing_Sinus.cs。
        // ⚠️ AH6_ 格式，需要同一波出貨的 AutoHook 才吃得下（舊版會靜默把匯入丟掉）。
        [510] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH6_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",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [511] - 【高難+】製作鰻魚預製菜
        // 來源：上游 Ices-Cosmic-Exploration Main-Branch @ 81d9961 的 Fishing_Sinus.cs。
        // ⚠️ AH6_ 格式，需要同一波出貨的 AutoHook 才吃得下（舊版會靜默把匯入丟掉）。
        [511] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH6_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",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },

        #endregion

        #region Critical

        // Export for Mission [542] -  Edible Fish
        [542] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1WS2/bOBD+KwbPEqD36+a4bjaAkw2qLPYQ9DCiRjYRWnQpqm028H8vqEcs23IqFOml2BtNDb/vm4dn5oXMayUWUKlqUaxJ8kKWJWQc55yTRMkaDfJBlGoBJUV+KwTdkKQAXqFB9KMVK/HwKO+f3OQkcaL48tt7yYRk6pkktkFuquV3yusc88O1xtm3HN3LF9IcHH1q8IPIINe7h43EaiN4ThLbso6Q34ZuMOJwkkbrpyIXm3rbK/NsyxuTNoGoRxOcI1UDQHvKc+fnMoXMGfAeOLC9ScBe9/wjqzbLZ6wGwvwTT31/kqdBn1x4wnTDCnUFrPFXX1T9RaqAPlUk8bt0BdE53xS2uGO7B8WwpDjQH5ziBdMy5fSQkv2HC1BtifYqT1Gdifl3O9SHDXAGT9VH+CqkBj666MPiGsf3n5CKryhJYusk9Fq8Scx9Qq7Y+hq2TYTm5ZqjrHo2XV05SdzQ8s7cm0QR7fcGWX5XErpWo1P8INJvsLspVc0UE+U1sLIPpGkbZFVLvMWqgjWShBCD3DXiyJ0okRgtwvMOSaIjN4K3EpX6Zbx7iRWOKyQmufC9ZWy+H/SkO6RKAl/UUmKp3snLE9R383VU7ZnHo+yNVVs4qRI73TBYuU4V7ppWf9DeFddcvo/kIVyj4Z+SfalR4xInzj2gRWBCUQSmFwXUzLzMNuPIdQs7zGxqAdkbZMUq9XehOSqSPLblqR147RZ+HASXNS7VBpl8rmZXwLnGuxNyC/wvIZ40Qt97/kVofut7Lb9xx7ND3aN6m1RJUa5HrCx3YLXCNZY5yOdLhh9EnfFxQieIXw0usA1NLlO1Vg+S7S4xhb7jvppc4joyeoOts9O1NS8UygXU641asa2eHnb74bTomkWllu3Y0odB/2w7mh+fD+w3ZqneHvq/e5/eT/ilZhLzVIGq9cjS68n/Of+Tcj5oKRiFNMgCNP0YQ9PLPceMrNwyoXAj6loY+XFM9p/7ntKNxMfXi7atPL6Qk/7ihpf7y60Q5eyK16iA8aNmaJ9Hp9/Ex4PWBAFLxShwHSmtoDWYb0VdDmI7stjrNngSOVfvG4eARVpPLQugmHLYDfzzx/aiwYrg7w3yu/f9yXv9YVj98ohKv8FO3yx0VJuADodWN6r0sb0+mJ3XtOV4RyVoO7lruzGYDi1s07Ms38wgDkwAihm1ItvL3aYEW+hO5aPvOTNztpCsSf7n2TJnGcdZU4fHJQ40yCInNy0v9E0vs0MTHIxMN6RB7BdZSKlD9j8AhrHG/EAOAAA=",
            },
            AmountRequired = 3,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Moon Bluetail"] = new List<uint>()
                {
                    45937,
                },
            },
        },
        [543] = new FishingTools { },
        [544] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1WS2/jOAz+K4XONuD365Zm206B9IE6gzkUe1AsOhaqWBlJbidb5L8vZFuJnSYNdlAs9rA3m6Q+fqQoku9o0ig+xVLJablE2Tu6qvGCwYQxlCnRgIW0ckZr2CuJUd0SlHlJaqFHQbmgaoMy10K38upXwRoCZC/W9tsO647zotJg7Yenv1qcKLHQzXpeCZAVZwRlruOMkD+HbjHSeHTCOUtmWjWrE4EFrhOcYWRAOGNQKBNJ4Dru0Mw7z4ILQjEzAJEbjACC3uyayupqA3LgKDxgGIYjhpHJOX6BvKKlusS05akF0ghyhYsXibKwz2KUfMQdoqY96iNWFOoCBnyiw3PROGOeOSroXzDFqqsE4/XwtHeQb78/Pa8wo/hFXuNXLjTASGDC8a2x/AkK/goCZa5OkvEZjDyYhF3S5Q1etZFN6iUDIQ2qvk2CMj92gg90R1DJdmuhq19K4P5l6VTPef6G17e1aqiivL7BtDYJsF0LzRoBdyAlXgLKELLQfUsC3fMakNUhbNaAMp2JI3gzLtVv4z0KkHCcIbLRCX3nsdXv+eRrKJTAbNoIAbX6oigPUL8s1qNsP0R81Htr1RVIrvhaP1BaL3MF67YT7rn3RTQRX0N5CPeR6RtdLTBV15Qx+Yn+qanlQ2MQvtf0ZwOaGQrCNCj9yLNx5IV2EJDETtPYtf3S8RzPcZ3YSdDWQjMq1UOpWUqUPXcFrlOw6wdhGkWno7xSFVCxkReXmDGNd8/FCrNvnL9oBNNdfgBu/7Vcgtp16hIzCVbfuXulDs/08F7UJTBwY921DGauBK8H4+78cccfHJ/BEmqCxeYLeLXAf/BmwQ4j7Sy8KN0Z7GmfNDlGbWg1F3R9ylMcev7O5JSvkdEn3no7/SwmpQIxxc2yUjO60gPI7RSH76VdNRrRTTj9MWjlXdMN048z+ZPxqvcC06lMXT3Bz4YKILnCqtFTTy8eJ4rtTPH80xr5/87/nTs3HW7Km1oNz1nooWab7xJ+VFDf83YDnbxiyvRbNQ900AlxGHiR6yQ2JpDYASx8O40KYpeJ5xWJS4qSBGj7p2mF/Wr7vBN03fD5HR20xSA83RbvOK8ZXVbq4pFBwUdzwP0su7cEakULzHRKtbPOYLLSWRiadZ35IKf+eHNMtKdGlLiAnOnmteMenlnSwq2F/jM7fr7GAsqGfcM1MSz06hYe4zEqmz5hvtXe3S2Z82kFxcvu+gb7vTNaU357wOdveK0lrfv2qoZTux/T+rMT782OPathES88lyycxC7D0rGDwHXslMShDU6JiyQqMURuW8QdrinDRmEF5KKt3BGe74ZeUSbEToMitoPAj+3EA2yHZRxjl0DiFRht/wa46t9DWg4AAA==",
            },
            AmountRequired = 3,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Moonlight Pleco"] = new List<uint>()
                {
                    45945,
                },
            },
        },

        #endregion

        // - - - - - - - - - - 
        // Phaenna
        // - - - - - - - - - - 

        // - - - - - - - - - - 
        // D Rank
        // - - - - - - - - - - 

        // Export for Mission [965] -  Aquatic Foodstuffs
        [965] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XS3PiOBD+K5TO1pYf8vNGmEw2VSSTCqTmkJqDbDWgjbGILLOTTfHfp+RHsMHAkmWr9rA30+r+9HXrU6t5R8NCiRHNVT6azVH0jq4zGqcwTFMUKVmAgfTimGewXWTN0i1DkR2EBnqQXEiu3lBkGeg2v/6ZpAUDtjVr/02FdSdEstBg5Yetv0ocLzDQzWq6kJAvRMpQZJlmB/k4dIkR+p0I8ySZ0aJYNgyIZZITFJookaaQqFag1XazT28rJOM0PVBSy/a8cIeJ12XSJTqMxRpQNKNp3uzwleeL6zfIWxzdHUjX7UB6zfnQF5gs+ExdUV6mqA15Y5gomrzkKHLrinvBPm4bNaxRH6jikCXQ4uPtxnndYttNqOR/wYiqSjVPOXzL0rfvXC1uGWSKJzTVXk0BW+vDRPE1TFK6ahb79OsFe0zsnWN3aibTBU05fcm/0rWQmkzH0JTGMbr2R0jEGiSKLF3wAxRIZ8PmLK74/IYuy6INs3kKMm820RpjKHJ8k+yx70AFm42Brn8qSesLrk9xKiZ/0tVtpgquuMhuKM+ag8GWgcaFhDvIczoHFCFkoPuSBLoXGSCjQnhbAYp0YXrwxkKfxyfxHiTk0M8QYXRgvdqxXN/ymawgUZKmo0JKyNSFstxBvViuvWz3Mu7dvfSqBDJRYqXvPs/mEwWrsiFvudciGsrLUG7DlRyeMv5agMZFluuavhkyDJQwTDw/wIHlxNiyqGsHVhyaAGhjoDHP1beZ3iNH0XMlT53AR6PwvcA+zHEk8qWIKVeDK5qmGvBeyCVNfxfiRUM0fec70PK3tuegPm5h2TObW1kv6tSa+1mbqvyJ5et+1mBOlBRZ69E8HW46rfAxzCFjVL5dgFcJ/JTDF1HU/o1jZTmRfgfN9nSSVdw2xTueaa8pX1at7LeqRdad+RFee5m1sf5Ovj3BTzlMJV91s6osZ2Xlu7YuUhX5j/PqoJ2fWR2ub+twpkCOaDFfqDFf6ifXqhZ2r3E5iBWyetP1R+vBKZ+8BWT3QlWv3sfbeOTpc3w33J94jgwvesxqOm5zwR7hteAS2ERRVejBQM9xB27diVt07mXpOPbq/Jigz9Jp26tXe8dFdqZ2/iWRfPbM2109DNw4AQfbls8wIS7DMfN97Ppm7PhmkNiJgzY/mrZez/rPH4aqsz+/o26LJ05wuMVfccHEsvsSWcfqsjMXvqPKYbgURdZxQxFxw93xyemOxYHeqZAzmtSTZO/ITtzQPTFEuhsD/Wf++2xHgk8PAjpYW0a6qmVB26NBPRDoz8q8deuTbUtiZuIQhzKGLUpjTGaJiykzCXY8kgSm5dpAQrQx9iUUHk7gUeSwBp4Orvgfori4kvoFcb6w/ldSdkklMZI4zAkZpk4SYkK9EFPbBJwkZgBObM2I6ZfNqsKtKT6Hnou//BiMqZzD4K5QNFODUZEqvqYK2EBP4HwJWd6dd53AM2cktLA5Cz1MYkZwEDgMMwLWzKYBjS0bbX4BIHQuinARAAA=",
            },
            AmountRequired = 5,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Eelsplorer"] = new List<uint>()
                {
                    47423,
                    47432,
                    47441,
                    47449,
                    47457,
                    47520,
                },
                ["Lehr Brotula"] = new List<uint>()
                {
                    47424,
                },
                ["Macrobrachium Phaennense"] = new List<uint>()
                {
                    47422,
                    47431,
                    47440,
                    47448,
                    47456,
                    47519,
                },
            },
        },
        // Export for Mission [966] -  Efficient Large Specimen Procurement
        [966] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACuVWTXObMBD9Kx6doQMYMHBzXDfNjJN6Ymd6yOSwwGI0wciVRJs04//eESDbYCdxO7n1Bvvx9u1qd6UXMq4km4CQYpKtSPRCpiXEBY6LgkSSV2gQpZzREvfKVKuuUhI5QWiQOaeMU/lMItsgV2L6lBRViulerOy3DdY1Y0muwOoPR33VOH5gkMvNMucoclakJLItq4P8NnSNEY46Hta7ZCZ5tdYMXNty36GgvVhRYCIPHO1DM+f9sIynFAoN4NtuB8Btzb5QkU+fURwE8noMPa/D0NdFhkdc5DSTF0BrnkogtGAhIXkUJPLasvnBMe4hatiizkFSLBM84OP3/fxuxRztyulvnIBsjv5UH/nBEZjTK/+wBVvmUFB4FF/gJ+MKryPQ2Q2NrvwWE/YTOYlsVbNXKLidgLqcF3R1Ces673G5KpALHUSddUqi4chyj9h3oILt1iDTJ8mhHTR1EEu2+AWbq1JWVFJWXgItdW1N2yCziuM1CgErJBEhBrmpSZAbViIxGoTnDZJIFeYE3owJ+c94c44CTzMkJnlF30Ss9Xs+iw0mkkMxqTjHUn5Qlj3UD8v1JNujjE9Gr62aBllItlHjS8vVQuKmXox77m0TjfnHUD6EqznclfRHhQqXpMMkCDKITc8BNF10R2YMkJh+HDgjL3Ac9CyyNciMCvktUzEEie6b9lQJ7GZ95AdvcJwwsWYyH8yrDSi4G8bXUHxl7FEB6MXxHaH+b4ZPaQVKlYEew1bUpOnaI7V5tPNCclbWo9Na7UY4g0KgcTaqNTxAneEKyxT480cB3wn8zKrWXhs2Ep2+0izpGnlvCV3D006lducnqxfC8VVBGrB3y/Gq5zkpn3Becrrp5rA3GHnOcGdyxOyU0SkSXTs1QuNMIp9AtcrljK7VVWY3iv5s1a+Uijd3pfo4uAWaBe2Fx7f7Gxe1elLorabb+BZ/VJRjupAgK3V/qjdLv7f/qoXPbsmO4XE3ndch57XCf33md/vNCVlooxvHpmWnjun6VmyGoWebmecFWeiFACMk2we9Ott37f1O0GxP9d/s6nZT3oe+b35+GEyzjCYUSzmYAV/hQN0odI3lYM5ZUnFcYyl7qzyGoZ2GmRkP3cx0A8sxwUpC007ARwwh8xyLbP8AGPlnTtMLAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [967] -  Opalescent Crossing Specimen Survey
        [967] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XS2/jOAz+K4HO9sAP2bF8SzOdboH0gUmKHoo5KDadaOtIGUnuTrbIfx/Ij8ZO3GRb5LBY7M2lyE8fmU8k+4pGhRZjqrQaZwsUv6JLTuc5jPIcxVoWYCFzOGEcdodpc3SdotiLiIXuJROS6Q2KXQtdq8tfSV6kkO7Mxn9bYd0IkSwNWPnhma8SJ4wsdLWeLSWopchTFLuO00E+Dl1ikGEnwjlJZrwsVg0D7Dr4BIUmSuQ5JLoV6LbdvNPXCpkymr9TUtcLw05RcR32janl5QZU6+Jgj3EQdBiHTdHpM0yXLNMXlJW8jUE1hqmmybNCcVCXMYwOcduopEa9p5oBT6DFJ9yPC7sV9JpQyf6GMdWVFJpb96O9vfr7dfRsSXNGn9U3+iKkAegYmnR8q2v/Dol4AYli1xSpT8thZCTQurCp3wVbXNFVmeiIL3KQqrnE/Ngpiv2hgw/Yd6Ci7dZCl7+0pPVLM5WfielfdH3NdcE0E/yKMt7Uw3YtNCkk3IBSdAEoRshCtyUJdCs4IKtC2KwBxaYwPXgTofSn8e4lKOhniGz0znl1Y3m+4zNdQ6IlzceFlMD1mbLcQz1brr1sDzLuvb30qgQy1WJt3ivji6mGddkZd9xrEY3keSi34UoOD5z9LMDgIjdzMpJ5nh1FJLAxxtiehzS0XY+mfhZELnUjtLXQhCl9l5k7FIqfKnmaBN4e9zCMjnAcC7USejm4L9bUwN0KuaL5H0I8G4CmUzwCLf82dgX67Q1mNFfQvMn60CTWvM7aVGWP3aHpQA3mVEvBW7PrdLjjt8InsACeUrk5A68S+EHBV1HU/o1jZTmRfgfNC02SVdwuxRvGjdeMrcpGNvziWOiO55vHJfBRotkLTPN36LUB/0nSPcEzydYHWdQOw8Dz31x2hI849ZHo+plHNMo0yDEtFks9YSszvdzqYP91lYtKIavxaD5ac6Ap0a3QVZWuU+CaJWYGV6XqGQf+MCCHG8GR4W7WkKYRNsr/Dj8LJiGdaqoLM2PNnvPOczgh74+quOPYK8BjSvuQdv4TIvnsb95qtpkf+VGauLYLnmNjwNiOEpzZAaUkdIMgwCRA2x9Nt6134ac3Q9Vwn15Rt/NijxzrvHOa68EF+1MUnSnhHivO2wMwFTE3VQ6jlSh4xw3FOCD7q43fXTMjc1MhM5rU7ad3r8UBCU4seMHWQv+afxB24/rTQ9oEG8vYVLUsaHts18PafFbmnVufdls6I5lPyByIPQwdYuPIi+y5k/h2NJ97eJgEfjQ0U/hAR77zfgIPXDOdQzp4FPJ5cCu+DHz/7Hrql8XH5fW/nvg59ZSRIE291LUDCFMbEzq3IxJSe+5kaUgJ9aIEl32rwq0pPpFwaH/9Mbhb0xxUAlwPxlIoxfhiYPZjtgI+mBbyBTbdnZTgMPR837Fd4s5tjN3MjgB828HhPPUhAZoGaPsbZo+nW50QAAA=",
            },
            AmountRequired = 2,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Untitled Work No. 33"] = new List<uint>()
                {
                    47430,
                },
            },
        },
        // Export for Mission [968] -  Bulk Provision Procurement
        [968] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XS2/bOBD+KwHPImBREvW4OW6aDeCmQZygh6AHWhpZRGTRIalss4H/e0E9bMmW7XWQAnvYmzyc+fjNcF5+R+NSiwlTWk3SBYre0VXB5jmM8xxFWpZgIXM45QVsD5P26CZBEQlCC91JLiTXbyiyLXSjrn7FeZlAshUb/XWN9U2IODNg1QcxXxUODSx0vXrIJKhM5AmK7NGoh3wcusII/Z7F6CSZSVYuWwauPXJPUGitRJ5DrDuGdleNnL5WyISz/EBIbUJpuMOE+j0mbgP0lavs6g1Uh4q3Y+l5PUvaPgN7hlnGU33JeOWJEahWMNMsflYo8prA0mAft4saNqh3THMoYujwobt2tB9T0ppK/g9MmK6T41HB9yJ/+8F1dpNAoXnMcqPVxqlzPo41f4VZzlbt4VCa0mCPCdl5Xadh8pCxnLNn9ZW9CmnI9ARtaByrL7+HWLyCRJFtAn6Agtu7sH2LS764ZssqaONikYNU7SUmlRIUOf7I3WPfgwrWawtd/dKSNXVsXvFBzP5mq5tCl1xzUVwzXrQPg20LTUsJ30AptgAUIWSh24oEuhUFIKtGeFsBikxgBvCmwrzHB/HuJCgYZogwOnBe31idb/nMVhBryfJJKSUU+pO83EH9NF8H2e55PHh7pVUnyEyLlal9XixmGlZV391yb5JoLD+Hcheu4vBY8JcSDC6CgLlz4rsYHDvBLkldHBBK8QhIQmzi+CFJ0dpCU67099TcoVD0VKencWDTKHwakMMcJ0ItxZxxfXHJ8twA3gq5ZPlfQjwbiLbv/ABW/TZyBXpThSnL1aYxNIfGtbY+G1Htv2v7pp+1mDMtRdGZjafNR07HfAoLKBIm3z6BVwX8qOCLKBv9VrGWnHC/h0aocbK227q47cL38DLIomv3b3wbMH5U8CD5qu9BLTnLA98jJiC15Vk+9CzP96IxN1U4TjXICSsXmZ7ypRmldn2wW57VHlXKelabj84gqUZZBsWt0PU028y8IyPN8b1wf2E5snuYLantpG3h3MNLySUkM810aQa+WcMOVNOJ6ji3CHqKg/l7LFHPysmu1mCeHU+oM3PnDyXJR9+8062DuZ0woCl2wwCwawcxDryRgz1C05DFoTcKHbT+2bbrZlV/2gjqjv30jvqt23WOtO4ryNUqFxJkb8jYx0Kzs/K9o1phvBRl0VNDkeuFu5uR0994A3NTKVMWN0vi4NLteqF3Yj/01hb6z/x72U77D894Y2wkExPVKqDdqd/MevNZi7dqQ5nbyTKSsnA+imPsAyXYDcgcsyBIMfNtm9EkDEdOgtbWfhY5hx2YQiYvZixnS1YkfyCVhjPi/Mz6P5WKz0wl3yUhzImDWUBd7IbExiyFBNtJHFDPswPwkqph1bgNxaeQBvjLz4vLMn++uJPilSsuCvMVlxKWUOj+EkvdOaHpPMXETgLsxizBLGAutn1q+5D6MSUMrX8DJKztrSwRAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [969] -  Opalescent Crossing Distribution Survey
        [969] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XTW/bOBD9KwbPIkBJFPVxc5w0G8BNgzqLPQQ9UNLI5oYWXYpKmy383wvqw5YcO9kUwWKB5kaTM2/ejB5n6B9oWhs145WpZsUSJT/QRclTCVMpUWJ0DQ6yh3NRwv4w74+ucpR4UeygGy2UFuYRJa6DrqqL75msc8j329Z+22J9VCpbWbBm4dlVg8MiB11ublcaqpWSOUpcQkbIz0M3GHE48iAvkpmt6vWJxKhL6AuMehAlJWSmz4S6xB2aeS+zUDoXXJ4g4nqMjWpMO7cPolpdPEI1CBwcMA6CEWPWfwN+D4uVKMwZFw1vu1H1GwvDs/sKJUFXVRY9xR2ixh3qDTcCygwGfNihHxtX0OtdtfgHZty0yuijHnp7B/X3O+/bFZeC31cf+IPSFmC00afjO+P9z5CpB9AocW2RjkmbRVYCg4B9/c7E8pKvm0Sn5VKCrvog9mPnKPFDQp+wH0FF262DLr4bzbuLZyt/qxbf+OaqNLUwQpWXXJR9PbDroHmt4SNUFV8CShBy0HVDAl2rEpDTIjxuACW2MEfw5qoyv4x3o6GC4wwRRifO24jN+Z7PYgOZ0VzOaq2hNG+U5QHqm+V6lO2TjI9Gb6xagSyM2tj7KsrlwsCmaZR77p2IpvptKA/hGg5/luJrDRYXebHnh8RlmPmUYOqSDMdBlGPqR2HkQQ6uV6Ctg+aiMp8KG6NCyV0rT5vA7nKHLPJOc5ypaq1SLszkjEtpAa+VXnP5h1L3FqLvFX8Bb36318+eVmBsDv1F7LbaRKkb2mbTOy+MVuXyNe7EH7jPYQllzvWjRegMd22g4LIC53XA56pO5S6lkYXH4p3BnvZJk2PUhla3WmxORQoDz9+ZnIo1MnomWmdn5TstDOgZr5crMxdrOzfc9uBQ182LodbtYLKLQQdum2MQPx2tz0xJO977jtIL6DN8rYWGfGG4qe2wsu+HQ1X9O/G8ViPv3/y/+eaDrkXjDLI0DDBlPsM09V0cZVGM08iPaEEg9UiGtl/6ttW9Me92G23nuvuBxi2M+vR0C7sBruVksQIpR93Wfa42VzmURmRc2oLYQK3BdK3qcmSGEhrEh08Ef/xci2ykWhc8g4W0ref4QzWIgxceSsHWQf+bd/d+7P3ysLPOdmdmq9oUdDj+uqFnl+323uyYdAcyiyHzC5LnmEc8w7RICY5YluPC/k5JCDQO0NZ5KqPguUmYcmkmZ+JvVb/r6PfQkReFaeYDw0EYZ5i6QYG5FwCmfsjCEFyf+OFRHbHTCcz5evNN6fvJrM7qdQr6XUy/h5hyxiPmuQQTEseYxiHFKecepkB81yNxlNOgmX0tbkfxLmYxPv8y+bThEqoMSjOZaVVVolxOzkVltEhr+56aLGr9AI/jPwkpy30/8gn2wpBgyhjBPGYZ5nmUAuWcBWmKtj8BZj/ZPT0SAAA=",
            },
            AmountRequired = 2,
            UniqueFish = true,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Cobalt Bijou"] = new List<uint>()
                {
                    47429,
                    47435,
                    47461,
                    47465,
                    47511,
                    47531,
                },
                ["Lampwork Cucumber"] = new List<uint>()
                {
                    47436,
                },
                ["Pearl Shell"] = new List<uint>()
                {
                    47428,
                    47434,
                    47460,
                    47464,
                    47510,
                    47530,
                },
            },
        },
        // Export for Mission [970] -  Large Mutant Cultivated Specimens
        [970] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACrVWS0/jMBD+K8jnRHLej1spj0UqLKKgPSAObjxpLNK42A4Lu+p/XzmJadKWQldwS+bxzTfjmbH/olGt+JhIJcf5HKV/0WlFZiWMyhKlStRgIa2csArWSmpUFxSlbpxY6FowLph6RaljoQt5+pKVNQW6Fmv7VYt1yXlWaLDmw9VfDU4YW+h8eVsIkAUvKUodjAfI+6EbjCQaeOAPyYyLevFOYr6D/Q8YGRBelpApk4nvYKdv5n7MggvKSGkAQscfAPid2RmTxekryF6gYINhEAwYhqbm5BGmBcvVMWENTy2QRjBVJHuUKA26KobxNm4fNelQr4liUGXQ4xNu+oXDirnGVbA/MCaq7QQTddPb3ai313nfFqRk5FGekWcuNMBAYNLxrKH8BjL+DAKlji7SrlYOY33kvYCmfsdsfk4WTaKjal6CkCaIPlyKUi/C/hb7AVS8Wlno9EUJ0g2arvwtn/4my4tK1UwxXp0TVpl62I6FJrWAS5CSzAGlCFnoqiGBrngFyGoRXpeAUl2YHXgTLtV/410LkLCbIbLRO/o2YqNf85kuIVOClONaCKjUF2W5gfplue5ku5XxzuiNVdsgU8WXel5ZNZ8qWDaLcc29a6KR+BrKfbiGw13FnmrQuMihjhtjP7czbzaz/XyW2InvZTaJCc5CH+OIYLSy0IRJ9TPXMSRK79v21Am8DXcUxns4jrlccFUcXddLouGuuFiQ8gfnjxrAbIpfQJr/dvi0VoLSGZgx7ERtmr4T6VVjnKdK8Gp+iDv2eu4TmENFiXjVCJ3h2xLISSnBOgz4TsIJrzt7Y9hKTJ4DNzfU2bQG61zeNfkM3x3OdxJuBVsOWbWS3ayiwNXZtCbv8RoYHc6sc9dDMcoViDGp54WasIW+jZxWsTktzbujFu11pz96e33H8vaiINm+DfdcvfrNYNaW6dMbeKqZADpVRNX6RtSPks3m/VyPHtqKA8Pv7aK+1fd3xje1wEV35ttvtP1n3luNQY4joD62KdDE9sNwZscRzWzPhyCCbDbDToRWD2Y3dg/X+zdBux71f7uMu1V4n0TYPnk4Gj3VRLHs6IxzKlWd53K4mT1CvcgLYjvycWT7eQj2jGBiJ15CqZOT3PUoWv0DUL9M3KILAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },

        // - - - - - - - - - - 
        // C Rank
        // - - - - - - - - - - 

        // Export for Mission [971] -  Efficient Aquatic Foodstuffs Procurement
        [971] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACrVWS2/jNhD+KwbPEkC9Kd28rpMG8KbBOkEPix5oamQTkUWHpLbrLvzfC0piLNnyulm4N2ke33wznBnyB5rWWsyo0mpWrFH2A80ruiphWpYo07IGBxnlgldwVOZW9ZCjzCepg54kF5LrPco8Bz2o+XdW1jnkR7GxP7RYn4VgGwPWfPjmq8GJiYPud88bCWojyhxlHsYD5J9DNxhpMvDAV8nMNvXWMgg9HF6hYL1EWQLTPUevb+ZfDytkzml5oaSxFw7wws7rjqvNfA+qFzc6IRxFA8KxrTl9heWGF/oT5Q1tI1BWsNSUvSqURV0VY3KO20dNO9QnqjlUDHp84lO/eFhA37pK/g/MqG47YaytYnIG5p+cRtCBPW9oyemruqPfhDR4A4HNLnCG8i/AxDeQKPNMzS5QCAcBbTk/8fU93TZ5T6t1CVLZIOboc5QFCQ7P2A+gyOHgoPl3LWk3d+YgnsXyb7p7qHTNNRfVPeWVra3rOWhRS/gMStE1oAwhBz02JNCjqAA5LcJ+BygzhRnBWwilfxnvSYKCcYbIRRf0bcRGf+Sz3AHTkpazWkqo9I2yPEG9Wa6jbM8yHo3eWLUNstRiZ8aXV+ulhl2zJ4/cuyaayttQ7sM1HF4q/laDwUUMxxQIJi6BELshDplLYuq78YpFBaRA4hCjg4MWXOk/ChNDoexr254mgfdZT2ISXOY4E2rL2WQmaQWTu3JvIB+F3NLydyFeDYhdHn8Cbf6NXIF+n8OClgrsXHZKk5yd0E7UViD0ErOULOZSS1H1brPr7jjouS9gDVVO5f4GvBrgFwW/ibqzt4at5Er6AzQ/Nkm2fscUL5r8lzRGnF8UPEu+G5JtJR8im0S+yb31vER3YPRxwp27ma5poUHOaL3e6AXfmlvOaxWnY9e8Z2rZXqPmo3dBjNwCQRKl58+Cn9zw5i1i959t9i/wVnMJ+VJTXZub1jx2LkzAlY7+aOMODEd77mbN1bcabZibdsb/1AIPv3jmvR2beAGOVkHqkoR5bljgxF0Rr3ALnxR+TMNVGAM6/GWXbPcg/vouaPes+W+3ut2pE3cyLwrOOFR6Mn2rqeZscidErnRdFGryJAWrJWyh0sOlH+UEk5xgl6Q5c8MEQjfFCXXTyC8S5gUUE4oO/wIYNEvHDAwAAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [972] -  West Beaconveil Distribution Survey
        [972] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1Wy27bOhD9FYNrCVcP6rlz1CQ3gJsGVYougi4oamQTkUWXpNK6gf+9oB62JNtJU2RxF3cnD2fOnBkfzvAZzWvFEyKVTIolip/RZUWyEuZliWIlajCQPlywCg6HeX90k6PYCSMD3QnGBVNbFNsGupGXP2lZ55AfzNp/12J95JyuNFjz4eivBscPDXS9uV8JkCte5ii2LWuE/DJ0gxEFowjrVTLJql73DLBt4Vco9FG8LIGqQaA9dHNeT8tFzkh5pqW24/ujpuIu7IrJ1eUW5CCxN2HseSPGft908gjpihXqgrCGtzbI3pAqQh8lir2ujX54jDtEjTrUO6IYVBQGfPxpnD/uoNOHCvYLEqJaKfRZp9HOpP9uF32/IiUjj/KKPHGhAUaGvhzXGNs/A+VPIFBs6yad0rIfagkMEvb9u2DLa7JuCp1XyxKE7JPoPztHsRtY+Ij9CCrc7Qx0+VMJ0t003fl7nv4gm5tK1UwxXl0TVvX9MG0DLWoBH0FKsgQUI2Sg24YEuuUVIKNF2G4AxboxJ/AWXKq/xrsTIOE0Q2SiM+dtxub8wCfdAFWClEktBFTqnaqcoL5brSfZHlV8Mnvj1QokVXyj7yurlqmCTTMZD9w7Ec3F+1AewjUcvlTsew0aF9nUDcPcC007ynITZ0BN4kFmgus7rusFAXUttDPQgkn1qdA5JIofWnnqAvaXO/BD9zzHhMs1o7NEkApmV+VWQ95ysSblv5w/apB+WnwF0vxuL6A+laB0Ff1V7ExtqdgO9Ljpg1MleNVcn85rf40LUkow/hjVcgeoC1hClROxfS/gD7zOyn2lIw/Hj/YOR9Ucu5yiNvS6F2xzLlPgOe7e5VyukdML2To/ret5oUAkpF6u1IKt9UKx24Op4Ju3Qy3ajaU/BqP5xPx1Ay+aLp4Xl7je+/3k6WX2Gb7XTECeKqJqvdT0w2KqvTdJ7I8l878E3iaB/j/Hb/zPB9PNyzMLE+qYReS7JiY0NAl2iZkRyPzCCR0vw2j3rR9v3ePzYW9oJ9zDMxqPOozx+VF3XRIpZ6liiq5AjAaz/VJ7bnKoFKOk1D3RuVqH+ZrX1cgNxdiLpq8Jd/yyC3WmWhSEQlrqYXTyKYmPL9T0TeXtDPSfeZMfNuRf78X0B9loS6K72jR0uCm7/ag/W/PB7ZR6B0qzfIeCS4np0cw1sVfkZgSQmyF1gFqQg0PsRmktbkfxIQocM/k2+wpSzS6AUF49AStnH5hUgmW1nlyztBZPsJ05/zjj1R1YXp4FtDAzBxcm9iPPjKIMmxYOaIY9j4AW928FpR30xA0AAA==",
                "AH4_H4sIAAAAAAAACu1WS2/bOBD+KwbPIlaUqOfNcZM0hZsN4ix6CIoFJY1sIrLoklS2buD/vqAetuRHjAQ59NCbPJz55pvxxxm+oHGlxYQprSb5HMUv6LJkSQHjokCxlhVYyBxOeQm7w6w7uslQ7ISRhe4kF5LrNYqJhW7U5c+0qDLIdmbjv2mwvgqRLgxY/eGYrxrHDy10vXpYSFALUWQoJrY9QH4dusaIgkGEfZbMZFEtOwaU2PQMhS5KFAWkuhdI+m7O+bRCZpwVJ1pKHN8fNJW2YVdcLS7XoHqJvT3Gnjdg7HdNZ08wW/BcXzBe8zYG1RlmmqVPCsVe20Y/PMTto0Yt6h3THMoUenz8/Th/2EGnC5X8F0yYbqTQZd2Pdvb677bRDwtWcPakrtizkAZgYOjKca2h/R5S8QwSxcQ06ZiW/dBIoJew698Fn1+zZV3ouJwXIFWXxPzZGYrdwKYH7AdQ4WZjocufWrL2ppnOP4jZf2x1U+qKay7Ka8bLrh+YWGhaSfgKSrE5oBghC93WJNCtKAFZDcJ6BSg2jTmCNxVKvxvvToKC4wwRRifOm4z1+Y7PbAWplqyYVFJCqT+oyj3UD6v1KNuDio9mr70agcy0WJn7ysv5TMOqnow77q2IxvJjKPfhag7/lPxHBQYXMd8GGgU59kOWYErSAIdAfJy6oc/swA2YTdDGQlOu9N+5yaFQ/NjI0xSwvdyBH7qnOU6EWvJ0NJGshNFVsTaQt0IuWfFZiCcD0k2Lb8Dq38auQG/vYc4KBd29bA9Ncd0NbU1NBygJzBTqMGdairK3v86H224vfApzKDMm1x/Aqwb+JKqk2K+08XD8aOuwo33S5Ri1vteD5KtTmQLPcbcup3INnF7J1voZXY9zDXLCqvlCT/nSLBTSHOwLvn47VLLZWOajN5qPzF838KLDFfzKNjV7v5s8nczu4UfFJWQzzXRllpp5WJzQ3hktvVUyfyTwNgm89z/vTTeIPDchro994lJMU8hxaLshhiBIbDsNac4StPnejbf28fm4NTQT7vEFDUcdpd7pUXddMKVGX6Ao1rmJ6k9m8lp/bjIoNU9ZYZpikjUO46WoyoEbiqkX7T8n3OHTLjSZKpmzFGaFmUZH35LUi7wzjypvY6Hf5lG+W5HvXowm2Fgmpqt1Q/ursl2Q5rMx79yOybcnNULdzEndAHsBtTF1vAgnSehhxyVAfSePAmajjXUopeCclO55kogSM7n8fbT0RzydKrZKUd3F2tMTK0X572MUOHjyffQNlB5dAEtF+Qy8GH3iSkueVGZFjWaVfIb1yPnLeZ8EwQdKSQY4Cx0PUzPoEj/LMQuClHlpmDupW0+7Brct9C3USE2tl9L1aJoRSLAXJh6mNPQxs+0Ik9wNWJpnIYEIbf4H/Z4YxkgQAAA=",
            },
            AmountRequired = 2,
            UniqueFish = true,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Glass Ribbon-arm"] = new List<uint>()
                {
                    47447,
                },
                ["Untitled Work No. 11"] = new List<uint>()
                {
                    47446,
                },
            },
        },
        // Export for Mission [973] -  Fish Paste Ingredients
        [973] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XS2/bOBD+KwHP0kLUg3rcXG+aDeCkQZ2gh6AHShpZRGTSJam22cD/vaAesSTbiVOkwB72Js/jm4/D4cz4Cc1qLeZUaTUvVih5QuecphXMqgolWtZgIaNcMA47Zd6rLnOUuFFsoRvJhGT6ESXYQpfq/GdW1TnkO7Gx37ZYV0JkpQFrPlzz1eCQyEIXm9tSgipFlaMEO84I+WXoBiMORx7Oq2TmZb3uGfjY8V+h0HuJqoJMDxzx0Mx9PayQOaPVkZRil5BRUv3O7SNT5fkjqEHgYMI4CEaMSZ90+gDLkhX6A2UNbyNQvWCpafagUBJ0aSTRPu4QNe5Qb6hmwDMY8CFTPzLOoNu7SvYvzKluS+FOwSdePX5hurzMgWuW0cpY9VkZ6GeZZt9hWdFNrzxUlCTaY+JO7tLrmNyWtGL0QX2k34U0ZEaCPjWeNZZ/hkx8B4kSbBJ+hII/CtjfxQe2uqDrJmkzvqpAqj6IKZwcJV7o+HvsR1DRdmuh859a0u7Vmlu8FcsfdHPJdc00E/yCMt5fjI0ttKglXIFSdAUoQchC1w0JdC04IKtFeNwASkxiDuAthLmP38S7kaDgMENkoyP6NmKj3/FZbiDTklbzWkrg+p1OOUF9t7MeZLt34oPRG6u2QJZabMzbZ3y11LBpuuyOe1dEM/k+lIdwDYc7zr7VYHBRlGPwPOzYEQ5D24/z3I4KktpOlsVRHgQYQ462FlowpT8VJoZCyX1bnuYAz40iJJF/nONcqLX4IeTaYF0LuabVP0I8GO++5XwB2vxuX57RKtCGfv8GO1F7Rh+Hpmf1zkstBV+96N60mxL4tdD7HWeC7XgD7AWsgOdUPhr4zvC5PRS0Us8d63XSDfCdgr9F3dn3hq2kT4LR3LI1yEkfumL8WYUSHP7VdsCu8X6GbwfjusTkqo2wy9SRo5wYOjKhj0Y5JWcHnO8U3Eq2GWemlfyhzISBa66kjXFSbo74vv3EnbtpBbNCg5zTelXqBVubeY5bxbRHNKtbLduFwXwMptmBkeWFQTyd+y/uUGbt6pt1/0A/w7eaSciXmura7BRmr5u+2tMe51uf2chw/4WcVuCnVefQar/iTi2YUyvjD5VAf+f+G+98MBBCir0iS2M7LVxq+1Hh2LEfujaOijANUyhSiNH2az8Rut3//lnQDoX7JzSeDr4fHZ8OVzSTIpU0K1m9PrspKXAOXI2nGn4pUZMd8wm1BrO1qPnIDCV+EE9XMW+8YkcmUi0LmnUz4uBO7+8/relCGmwt9J/5c7RbL357qTDORjI3WW0SOlwzuuXCfLbindmhOh7UXBrgIA9javskcmzfT8FO3ZjaDsmcIsvilMQx2lp7NRW8cIAFlPJsTuXm3YvocC28vab+LyL+nkXkp1nk5hTsgAaZ7TuuZ1NCfDsPvAAXcexiQprG1eJ2FO/j0LPnX88M8NkNVRrOLvlKQs6AazXelVOnCIhDiV24HrH9AAd2HHvEzmkepJi6EQ4I2v4CAURz04ERAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [974] -  Multi-purpose Bait Test
        [974] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1WS2/bOBD+KwbPIlYSqefNddNsACcbVF7sIeiBkkY2EVl0SaptNvB/L6iHLfmRR5EWBXZv1HDmm29Gw5l5RNNaixlTWs2KJYof0UXF0hKmZYliLWuwkLmc8wr2l3l/dZWj2A0jC91KLiTXDyh2LHSlLr5lZZ1Dvhcb/W2LdS1EtjJgzcE1pwbHDy10uVmsJKiVKHMUO7Y9Qn4ausGIgpGF/SyZ2apenwmMOjZ9hlEPIsoSMt1HQh3bGaq5z7MQMues7AF8h44AaKf2gavVxQOogSPvgKHnjRj6fc7ZPSQrXuh3jDc8jUD1gkSz7F6h2Ouy6IfHuEPUqEO9ZZpDlcGAj39o548z5vamkv8LM6bbSui9Hlq7B/kmnfVixUrO7tUH9kVIAzAS9OEQayz/CJn4AhLFjknSqVL2Q/PLBw77/L3jy0u2bgKdVssSpOqdmJ+bo5gENj1iP4IKt1sLXXzTknUPzWR+IZKvbHNV6ZprLqpLxqs+H9ix0LyWcA1KsSWgGCEL3TQk0I2oAFktwsMGUGwScwJvLpT+YbxbCQpOM0QYnblvPTb3ez7JBjItWTmrpYRKv1GUB6hvFutJtkcRn/TeaLUFkmixMe+VV8tEw6ZpjHvuXRFN5dtQHsI1HP6u+OcaDC4q/BAcCFLs256NKQlzHLrUxUFk55CmeWEXKdpaaM6V/qswPhSK79ryNAHsHnfgh/Q8x5lQa/FVyLXBuhFyzco/hbg31n2b+AdY892+PHOrQBv6/RvsRG2M1AlMn+mNEy1F1bybTmv3fgtWKrBejGqTAeocllDlTD68FfB7UaflLtKRhutHO4WjaI5VXkLthPFC8s05AoHnkp3KOQojpdeT6MxN+U8LDXLG6uVKz/nazB2nvTh8F82GUct2sJnDoIOfaNMk8KLD+fTkqDfbQd+g+qL8CJ9rLiFPNNO1mX1m/Tis1FcV5IsL7BcWzK+tjJ9UAv0/p6/854MmmOVFTlKXYJu5gKkDBIehH2LP9woHSOSzwkHbT30X7FbUu52gbYR3j2jcEalHznfEy5IpNXnPVVarUfd2nkrOVQ6V5hkrTUaMp1ZhuhZ1NVJDMfWiw5WDjNe/0HiqZcEySErTuE4vusfP6XDx8rYW+m329v0Y/eHhaYyNZGay2iR0OE67IWqOrXivdqp2h8MWCkZd18YUQoopjSIcZhHFxHNdQsIQqB2grXVcR+5zdZRorrMVyP8r6T9SST5hxM2JWdYopmngY5alFKdeTosiLQIKYdOxWtyO4l0UUDz7NLmuS83xppYboWBi+E0WoPSE/EHG22FOnNRnXoTtgBRmOwQcsczDHlDHCSGloe+h7Xci65QCJhAAAA==",
                "AH4_H4sIAAAAAAAACs1W227jNhD9lYDPYitRlETpzXGzaQAnDdYu+hAsCloa2URk0UtSm2QD/3tBXWzJl7gbpIu+ycOZM2fGc3tFo8rIMddGj/MFSl7RVcnnBYyKAiVGVeAg+zgRJewes+7pJkMJYbGD7pWQSpgXlHgOutFXz2lRZZDtxFZ/02DdSpkuLVj9QexXjRMyB12vZ0sFeimLDCWe6w6Q34auMeJoYOGeJTNeVquOAfVceoZCZyWLAlLTM/T6auS8W6kywYsTKfVIGA6SSluzT0Ivr15A9xwHe4yDYMA47JLOH2G6FLm55KLmbQW6E0wNTx81SoI2jSE7xO2jxi3qPTcCyhR6fMJ9u3CYQdKZKvEdxtw0pdB53bcme/n3W+vZkheCP+pP/JtUFmAg6MLxnaH8M6TyGyiUeDZJx2o5ZLYEeg67/F2KxTVf1YGOykUBSndO7J+docSPXHrAfgDFNhsHXT0bxdtOs5mfyekTX9+UphJGyPKai7LLB/YcNKkU3ILWfAEoQchBdzUJdCdLQE6D8LIGlNjEHMGbSG3ejXevQMNxhgijE++Nx/p9x2e6htQoXowrpaA0HxTlHuqHxXqU7UHER73XWk2BTI1c234V5WJqYF1Pxh33tohG6mMo9+FqDn+W4msFFhcFUZSxMOM4j4iHaR6lmAEPcU4Z5LHLvDCbo42DJkKbP3LrQ6PkoSlPG8C2uaOQ0dMcx1Kv5JNUK4t1J9WKF79L+WituzHxF/D6t5VrMNsGzHmhoWvI9tFG1bVmK2pCp15kx0+HOTVKlr3FdcT8lj9b6Uysmt7/xT2AdP0e5AQWUGZcvXwA1xr4N1nNi/3oGw0SxluFXSgnVf4NtSPGMyXWpwhEAfG3KqcoDJR+nERrbltilBtQY14tlmYiVnYXec3Dfq/UZ0elmmVnP3pT/cjo9qMgPtzebyxiezJ0Q6sr1M/wtRIKsqnhprL70N4kJ6r3TDX+aIH9xIL5uZXxH5XAe//z3mBMYy/1IAgw8XiMKcznOGaM4piEXuT7NAtchjZfusnY3q0PW0EzHB9e0XBK0iA4PSUvpZIXl8CzwTj33srMTQalESkvbDqsm0ZhtJJVOVBDCQ3i/RvEH96DzHqqVM5TmBZ2arWsgzg4c3oFGwf9b0733SJ99/q0xlYytmlsSvCJr5ulqrvJ0t+xKEG8lOXfD3FE8fjLxW1VGIHXlVpLDRcW6mIG2lz4v/qoD9ZzcKTGe/VIaJ6zkFNMYhpgGucEszgmmNEwDjxOIt/16npscNvgztEhNZ2em5yEc5KyCPtk7mGauSFmUZBifx66jDE355SjzT8n4EKjGQ4AAA==",
                "AH4_H4sIAAAAAAAACs1WWW/bRhD+K8a+9IVseSyP5ZuiOq4B2TUiFX0wgmBEDqWFKa6yu4ztGvrvxfKQSB1WYrhB36jZmW++Gc31QkaVFmNQWo3zBUleyGUJ8wJHRUESLSu0iHmc8BJ3j1n3dJ2RxIuZRe4kF5LrZ5K4FrlWl09pUWWY7cRGf9Ng3QiRLg1Y/eGZrxonjC1ytZ4tJaqlKDKSuI4zQH4dusZg0cDCOUtmvKxWHQPqOvQMhc5KFAWmumfo9tW8826FzDgUJ1LqemE4SCptzT5ytbx8RtVzHOwxDoIB47BLOjzgdMlz/QF4zdsIVCeYakgfFEmCNo1hfIjbR2Ut6h1ojmWKPT7hvl04zKDXmUr+D45BN6XQed239vby77fWsyUUHB7UR/gmpAEYCLpwfGso/4Sp+IaSJK5J0rFaDmNTAj2HXf4+8MUVrOpAR+WiQKk6J+bPzkjiRw49YD+Aijcbi1w+aQltp5nMz8T0EdbXpa645qK8Al52+bBdi0wqiTeoFCyQJIRY5LYmQW5FicRqEJ7XSBKTmCN4E6H0m/HuJCo8zpDY5MR747F+3/GZrjHVEopxJSWW+p2i3EN9t1iPsj2I+Kj3WqspkKkWa9OvvFxMNa7rybjj3hbRSL4P5T5czeGvkn+t0OASAD+DDDw7YH5gU5a6NoM4tgP0QieNnCzOHbKxyIQr/WdufCiS3DflaQLYNncUxvQ0x7FQK/Eo5Mpg3Qq5guIPIR6MdTcm/kaofxu5Qr1twBwKhV1Dto8mqq41W1ETOnUjM346zKmWouwtrhPmM75CudfxN/C0fSKJS3+NDlw5fs/VBBdYZiCf3yGGGvh3Uc2L/aw0Gl7Itgq7EE+qfA+1I8YzydenCESB529VTlEYKP04idbctMoo1yjHUC2WesJXZke5zcN+D9XnSCWbJWg+etP+yEj3o4AdbvVXFrQ5Jbph1hXwJ/xacYnZVIOuzJ40t8qJqj5TpT9aYD+xYH5uZfxHJfDW/7w3MLM5Yog+2AhRZNPYndvMdzPbgdhxwjwIIz8nm8/dxGzv2futoBma9y9kOD1p8Mr0vFsCliX8oi5GUorHwbB3X8vPdYal5ikUJinGWaMwWomqHKiRhAZs/0Lxh9dibDxVMocUp4WZXUfPUxqw4MydFmws8r+583db98271hgbydhktanLR1g3G1h1uekvZLN3S1F+uWcRtcefL26qQnN7Xcm1UHhhoC5mqPSF95tP+mA9B0cKv1ekc4wYYy7YcUSpTd0gsBmEuc3SEOIMYsrCpkgb3Da4c3Tcmk7PjetGLIqA2pBTx6ZR6tgQ+Lnt+RDOKUMWs5hs/gURRS9ORg4AAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [975] -  Emergency Bulk Provision Procurement
        [975] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1X227jNhD9lYDPYqELqYvfHNebBnDSIE7Qh2AfKGpkE5FFL0Vl4y787wV1iSVbjjeBCxTovtFDzuGc0Znh+Acal1pOWKGLSbpAox9omrM4g3GWoZFWJVjIbM5EDrvNpN26TtDIDSML3SkhldAbNHIsdF1MX3lWJpDszOb8tsa6kZIvDVi1cM2qwvFDC12tH5YKiqXMEjRybLuH/D50hREFPQ/7ZDCTZblqIyCOTU6E0HrJLAOuO45O95h7+lqpEsGyIyl1XN/vJZU0bl9EsZxuoOhcTPciprQXsd8mnT3DfClSfclEFbcxFK1hrhl/LtCINmn0w0PcLmrUoN4xLSDn0InH3/fz+xl0W1cl/oYJ07UUhnTlhwdg7t7n8BqwhyXLBHsuvrAXqQxez9Cy86y+/R64fAGFRo7J2ZEQSO/CNp2XYnHFVhXvcb7IQBXtJebbJ2jkBTY5iL4HFW63Fpq+asWawjMf4kHOv7P1da5LoYXMr5jI29xix0KzUsENFAVbABohZKHbKgh0K3NAVo2wWQMamcQM4M1koT+Nd6eggOEIEUZH9usbq/1dPPM1cK1YNimVglyfieUe6tm4DkZ7wHjw9upULZC5lmtTviJfzDWsq0a5i70R0VidJ+QuXBXDYy6+lWBwEUl5FDqRjRkHigmEHIep52JOIuIQCqFnE7S10EwU+s/U3FGg0VMtT0PgrdYDP6THY7ySkiv2PQNlwG6lWrHsDymfjXvbNv4CVv029gL0WwWmLCugrchm09Bqa7Mx1dyJE5h21GLOtZJ55yEbcL9hr8b6IFZV8fu/2QeQtteBnMEC8oSpzRlirYAfC/hdls359mBtOZGSHprrG+K13472G7W9hnYj8j5r5x24n6E84PxYwIMS6z6x2vIhYgF1TZ5qz3NQ6wF+nFzjbmp4nGpQE1YulnomVuYtdeqN/eKuxqZS1Y+1WXSeoYG3xgtodDh9vDNImJGn7bJtYd3Dt1IoSOaa6dK852amOlJtJ6rnowXROzio5VOi/Wkhdk8Niuu0ij6gjH9JAp/95p1O7ru2zyCKsEu5h0kQpDiKPR9Dyojj8dClxEbbr20rb+bupzdD3c2ffqB+Wyc0fKetb1Y5U3xZFr0HyHkvNdcJ5Fpwlpl8mHvqA+OVLPPOsYGiIDTaH6K8/nwbmotLlTIO88x03YYFjeiJ2ZFuLfSf+SuymwQ+/f4bZ2OZmKxWCe1OBM0cYJa1eXdsSLndecH1oojwFHvAEkzsIMZhkgbY8ziJvTSMeZygrXWoouA4gSlkxTqTCtTZVfRp2QzL75eKzqOiBBxKXYixz9wYk8RPcZz6KYYA4pTbYehye1BF0XEC13nGNhf3bPOrFf0/RMQhtd0wSXBqezYmASM4SsDHzA5sCi54Lq0fvBq3CfEpCiiefL3AF9MVqAXki83FZZk9X9wp+SIKIXOz4qWCFeS6/2fJDtLEJQnFYFMPE/BcHMVRgDnzwPXsgHqcou0/alDVR0UTAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [976] -  Vitrified Freshwater Fish
        [976] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XTW/jNhD9KwbPYiFK1OfNcZM0QJIN1tnmEPRAUSObjSx6KSq7buD/XlAftmXLNjbIoUBzkznDN4/Dx5nxGxpXWk5YqctJNkPxG7osWJLDOM9RrFUFFjLGW1HA1ph2ppsUxU4YWehBCamEXqGYWOimvPzJ8yqFdLts/NcN1p2UfG7A6g/HfNU4fmih6+XjXEE5l3mKYmLbPeTT0DVGFPR22GfJTObVomNAiU3PUOh2yTwHro9khBKb7O5yzrOQKhUsP4JHHN/v5Zi2265EOb9cQblzAG/vAJ7XO4Df3QF7gelcZPqCifoYZqHsFqaa8ZcSxV6bVT88xN1FjVrUB6YFFBx2+Pj7+/x+Qp1uqxL/wITpRhnfSvhS5Ksnoec3KRRacJYbry4rO/Yx1+IVpjlbdsYhjfrhARNn72rdlsnjnOWCvZRX7FUqQ6a30KXGtfrrX4HLV1AoJibhRyjQXsDuLi7E7Jot6qSNi1kOquyCGOGkKHYDmx6w70GF67WFLn9qxdpHbG7xUU5/sOVNoSuhhSyumSi6i8HEQreVgjsoSzYDFCNkofuaBLqXBSCrQVgtAcUmMQN4t9LcxzvxHhSUMMwQYXTE3kSs7Vs+0yVwrVg+qZSCQn/QKfdQP+ysg2wPTjwYvfZqBDLVcmnevihmUw3LuuhuubciGquPobwLV3P4VojvFRhcxDPPy+w0wWGSMEx54uPIAxenAfPdkAfgJBlaW+hWlPpLZmKUKH5u5GkOsCkUgR+6xzlOZLkQfDRRrIDRVb4ykPdSLVj+h5QvBqSrPE/A6t/NAzTWErQ5RfcUzdKjWIDae6J3otiYUEzob3bja9JQp4USJwhNatpIU61kMTsb6+j+W5hBkTK1MhCt54ZTxvJyU8nOI9vmzn6XVZJvEtDzcPxo47DlfdRliNqu16MSy2ORAs9xNy7HYvWcTkRr/Yzcx5kGNWHVbK5vxcL0LNIY9t9BPa1UqmmK5mOnYtc9Yw7FvdRN29g0lxO9ww286HAsONHSzSzSlaxOn1/heyUUpFPNdGU6qxl29kW7d6tBNCi3obs/JapPkfyaSN5757tlkRCWhh7H4EYRpqFt44TSFBPPDYhLSORGPlr/1dXFdiB+3iw0pfH5DfVrJPVP1PGJTFiuRxfib1n16jk5lZy96eoNNQ7jhayKnhuKqRftDyFuf7g05W1aqYzxdh4bno69yDszinlrC/1n/iVsG+u726nZbFYmJqt1QncbbNtWzWezvHUb0u6OzigEwKPAx47rZZh6SYLDNAwwDV0WZjSlGfXR2jrU0Ylee7kAxfI0Yfzlw2U0rIZfV9WnjD5URsS2I5pBgombEEw938NhGmQ4oVHipg4AT7NBGTnHD3Ahi9XoSTGh55nZ9Sml/4WUuEMdn5EE0zThmFLi4SRLA5w4ruMAcYDbTedrcLveNcKjP4VWIhOQjq5MIn8wDWpUC64XIITQ9SmPMLNthmnkRZhxx8c2uLad+JQyJ0PrfwE9wWYb1hIAAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [977] -  High-silica Environment Observation
        [977] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1YTW/bOBD9KwHPYkFKlCjp5nrTNECaBnUWPRR7oMixzY0suhSVNlv4vy8oWbbl2G6zTYECm5tMDt988PFx6G9o1DgzFrWrx9MZyr+h80oUJYzKEuXONhAgP3mlK9hOqn7qUqE8TLMA3VhtrHYPKKcBuqzPv8qyUaC2w95+1WG9M0bOPVj7EfqvFidJA3SxvJ1bqOemVCinhAyQT0O3GBkfrCDfDWY8bxZHEmOUsO9E1IOYsgTp+kwYJXTXLPx+FMYqLcojgdAwSQY1Zutlb3Q9P3+AesdxvBdxHA8iTvo9EHcwmeupey10G7cfqPuBiRPyrkZ5vK5qkj7G3UXN1qg3wmmoJOzEk+yvS4YVDPulVv8DY+E6ZvRe91eHe/WP1qtv56LU4q5+I+6N9QCDgT6dKBiOfwBp7sGinPoiHaJ2knoK7Djs6/dazy7Eok10VM1KsHXvxG+2QnnECXsU/QAqXa0CdP7VWbE+eL7yt2byRSwvK9dop011IXTV1wPTAF01Ft5BXYsZoByhAF23QaBrUwEKOoSHJaDcF+YA3pWp3X/Gu7FQw+EIEUZH5juP7fw2nskSpLOiHDfWQuWeKcs91GfL9WC0jzI+6L216ggycWbpz6uuZhMHy1Yot7GvSTSyzxPyLlwbw5+V/tyAx0WMS8UjSnFcZClmnEssoqjAWRJTnlEZCg5oFaArXbv3U++jRvmnjp4+gc3h5knKjsc4NvXCfDF24bGujV2I8q0xd351LxMfQbS/u5PnZ2twPvz+DK6HuhwZ5V5n+sUTZ001O7n8fVU+fJxDNZJO38OkPApMoh3gK5hBpYR98Nhrw402TEVZQ/DDEbfAf5imKDfJDizCJNsYbBM6anIotF2rW6uXxzzxOIw2Jsd8DYxOeFvbeU6Ppg7sWDSzubvSC3+Z0G5in+xtG9HY7rbyHzuy3O/UtXGPN+uAMEc8zvZvpJOXve8PeknqafgBPjfagpo44Rp/2/kGZJ+bP0bBp/LphR9P40e/5+yJe74je7GMBAgiMctIjBkhCc5USLHiMZ0yTkAxilbBYZ2Lj+vchTHSii8l2J8Uulu9ALvH9Xe62kyhnLJXJEC+Yz1k68eH9ukr9tMa+qKUv9dJOCKGTz0YL2L4vxZDRbiaqizEvIgyzBgQLGLFMEiuJIQyZUSi1V99E7h+sX/aDHT6+OkbGgolS5LjQjkRlSpKUTuwg96VnqrNpYLKaSlKXxDvqDMYLUxTDcxQzuJs/8EVDR+/qffU2KmQ6/7i8LP/cWux/+yMVwH6bf7F2D4iNhsRZz7xH+rM/TqPMPYFbWu5+45Yvx78Zze8NTvE2h2GcZKGLCliTKkimMmpwlnBUiwpUxwoy1QUtdftPoNOXLVjU4jSnb3Wf5vmhULPSKGeBH0OByj1lCboV3EqjAgFTmIsKKWYhTLEBWEcq6igEEdExYIe5NSJZ+oNCFueTeZQli+U+rWq9DtQiMdRwjkhOKZTgllKQyyAJZhwxtJpxmUKvL34OtxeeM7w2Vs9m+Nal1qKs/PqXltTLaByojx7X9Rg74Xvpob/tMQqAloUKZbMPzkKpXBB0wxzEtEQwgRkqNDqX16LKAiCFwAA",
            },
            AmountRequired = 3,
            UniqueFish = true,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Cobalt Bijou"] = new List<uint>()
                {
                    47429,
                    47435,
                    47461,
                    47465,
                    47511,
                    47531,
                },
                ["Opal Shell"] = new List<uint>()
                {
                    47467,
                },
                ["Pearl Shell"] = new List<uint>()
                {
                    47428,
                    47434,
                    47460,
                    47464,
                    47510,
                    47530,
                },
                ["Sandblaster"] = new List<uint>()
                {
                    47466,
                },
            },
        },

        // - - - - - - - - - - 
        // B Rank
        // - - - - - - - - - - 

        // Export for Mission [978] -  Upper Soda-lime Float Distribution Survey
        [978] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1Xy27rNhD9FYNrqdCLEqWd45ukAZw0uErQRdAFJY1sIrLoS1LpdQP/e0E9Ykm2Eyd1gS66o4czZx46Mxy/omml+IxKJWf5AkWv6LKkSQHTokCREhUYSF/OWQm7y6y7uslQ5JDQQPeCccHUBkW2gW7k5c+0qDLIdmKtv22wbjlPlxqsPjj6VOP4xEDX64elALnkRYYi27IGyO9D1xhhMLCwPgxmtqxWXQSebXkfhNBZ8aKAVPUM7b6a87FbLjJGiyMltR3fHxTVa82umFxebkD2HONRxBgPIva7otNniJcsVxeU1XFrgewEsaLps0QRbsvok33cPmrYot5TxaBMoRePP7bzhxV0OlPB/oIZVQ0VOq9ja2dUf7e1fljSgtFneUVfuNAAA0GXjmsM5d8h5S8gUGTrIh3isk80BXoOu/pdsMU1XdWJTstFAUJ2TvTHzlDkBpa3F/0Aimy3Brr8qQRtO01X/oHHf9L1Takqphgvrykru3qYtoHmlYBbkJIuAEUIGeiuDgLd8RKQ0SBs1oAiXZgDeHMu1Zfx7gVIOBwhMtGR+8Zjfb+LJ15DqgQtZpUQUKozZTlCPVuuB6Pdy/ig91qrIUis+Fr3KysXsYJ1PRl3sbckmorzhNyHq2N4LNmPCjQuwjh3cWaDSZI8NL0w8M0kdTOTpEFOg5yESYrR1kBzJtVvufYhUfTU0FMn8NbcgU/84zFecw6byZyKF6rR7rhY0eJXzp+1fTcofgda/256T99KUDqBrgtvWamlD2zV9OkvloFatSZzzw709OkAYyV4WXdTq/XW1TktJBjHPY1QLbeHOocFlBkVm3MBP0r4xqtWv1NsJF1J3tIezaUPK+L4uiAN2IflOGp5SsoHjB8lPAi2HibWSP55YgF2dPEauE+mNrD9fHKtuW7iaa5AzGi1WKo5W+nX024uxt1dL0qVaJ5nfei9QwceGzfA4fiVfXdj0UtON2a7xvoOPyomIIsVVZV+wfUWNe62TzXQyQ0xUNzn8mn8PI2Ifa19cp1KmFOZ8S9RoPvm3ie/eW+U536YYwy5SfzANr0wzEzqATFt8FNMiU2I56DtH90sbzftpzdBM86fXtFwrnvEOz7XbySXKRQgJzOe0EIN3iH7vQLdZFAqltJCV0V7axSmK16VPbUDreHhcLxLucO9lmjHlchpCnGhZ2+by35LjVdIvDXQf+YvyG4h+PIaoI21ZKarWhe0vxi064A+NuKd2iH+9riWhilNPMcyaYKp6dlBYiYJxmYQhNj1Uhy4WY62xj6XrOMJXAkuFWSTeAlFcXYifZk5hxn4P5Huz0IkANsijpua1HUy08u8wKSpRU0vJDTEvkUCnNRDq8FtQ7yYmJPH9RrEJOYZNQu2gslVwamafGNSCZZU+hmcxJV4gc1w4bUIAT+3c9PHgLUbywz1lIQkCEnoJolLMNr+DcJMSar6EAAA",
            },
            AmountRequired = 3,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Isosceles Cobalt"] = new List<uint>()
                {
                    47484,
                },
            },
        },
        // Export for Mission [979] -  Central Soda-lime Channel Distribution Survey
        [979] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XTW/bOBD9KwbP4kKUqM+b66bdLJI0qB30UOxhJI1sIrLoUlRaN/B/L6iP2HLkpCmy2AW2PsnD4eOb0ZsZ6p5May1nUOlqli9JfE/OSkgKnBYFibWq0SJm8UKUuF/M+qXzjMROGFnkWgmphN6SmFnkvDr7lhZ1htnebPx3LdallOnKgDUPjnlqcPzQIu83i5XCaiWLjMTMtgfIT0M3GFEw2GE/S2a2qtcnAuPM5keMgnDIyO5RZFFgqvtQOLPZoZ/zPA2pMgHFCSbM8f3omMowOcPlaSLvkMQ5FFV/wjtRrc62WB1w9I4gPe8o3/0Lg1ucr0Su34BoYjSGqjfMNaS3FYm97hX44WPgAazTwV6DFlimeMDIP97oDxm5/VYlvuMMdKujMVH64SMwZ5ixsMNarKAQcFu9gzupDNzA0EfnWkP7R0zlHSoSM5O0Ewz4gLzfHfhGLN/Dugl7Wi4LVFV/iNFJRmI3sPkj8gOocLezyNk3raArWvMiFnL+FTbnpa6FFrJ8D6LsU0uZRS5qhZdYVbBEEhNikauGBLmSJRKrRdhukMQmySN4F7LSv4x3rbDCcYaEkhPr7YnN+p7PfIOpVlDMaqWw1K8U5RHqq8U6yvZRxKOnN16tQOZabkz9inI517hpmuyeeyeiqXodyodwDYebUnyp0eASz/U8lvOMAmQ55V7EacQDoLbN7Ch0MnATRnYWuRCV/pCbMyoSf27laQJ4KPXAD4PTHC9hWWIB6g4M2JVUayj+lPLWbO+7xieE5r+xV6gfKrDpeX1FdosmrL42O1MbO2dBZMLvMOdayfJgCJ7YvhBrVEclfynKhyUSs+APbh/+fIt8KIvtpxWWV1JPUy3ucF78HKsLXGKZgdo+S+wAwQlMf7up8K2suw29Z2t5Jn8DOMc3fNp9+xy9dOcgjhGvmwoXSmyGZFvLi8gGnmM03e58Id3B3icId36mMKe5RjWDernSF2JtBiRrF44rtrlH1aodwebhYLaMDBA38KLj+8eTFxpzB+pbZ18tH/FLLRRmcw26NkPaXLJOlNAzJXHkZbunJDrmOKq5nxDXy1U0KpifUsa/LIFffecH7Tl33ZRlPlDX9YDyyA1okmJOnczJWeInEbOB7P7u+3N3Ef/8YGhb9Od7MuzVPAxP9+o3RY2TazgaKuypzJxnWGqRQmHSYY5pHaZrWZcDNxJzLzq+CbnDW6ppc/Na5ZB2DXX8Lu9F3tPXQebtLPKf+TTZT/dfnulms7HMTFabhB5O+W62m8fWvHcbE+6ByJwgSbjjAM0x55RHNqfgQ0pT5gK4XuZxOyU767GIotMBzNS20lAU4jtmk8VKya+Vlv+ApMaV8XKF/ZbUq0oqc30/iiKgLAo8yh3boxDZAfXDkCG4PAffHZWUfzoA47qd/IVFsc3Nrt/d6X8hJeSQo+9lFNwMKM/CgCa+jTTgmeuESRY6aDcjsMXth9iETmZYmm+wyVxmQAuxxslsBWWJxeStqLQSSW0uVpN5re5wO/wssj2GiZf4NEzBo5wzmyaR7VAv4CmzIbQ5OGT3A5FMMbFrEwAA",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [980] -  Arthrolure Field Test
        [980] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XS2/jNhD+KwbPImBKJCX65rhJGsCbBmsXPQQ90NLIJiJLWorabLrwfy+oRyzZst0GPuwhN3k4882DH2fGP9G0NNlMFqaYxWs0+YluU7lKYJokaGJ0CQ6yh3OVwv4wao8eIjRxA+GgJ60yrcwbmhAHPRS3P8KkjCDai63+rsb6kmXhxoJVH679qnB44KD7fLnRUGyyJEITMh73kM9DVxjC71mMLwYz25TbNgJKxvRCCK1VliQQmhMVoWRMulbu5SgyHSmZnMAjLue9GtPG7E4Vm9s3KDoJsIMEGOslwNs7kC+w2KjY3EhVpWEFRStYGBm+FGjCmqry4Bi3iyoa1CdpFKQhdOLhh3a8X1C3NdXqH5hJUzNjiGY8OAJzD27Ha8CWG5ko+VLcye+Ztng9QZud5/TlXyHMvoNGE2JrdiIE2nPYlvNGre/ltsp7mq4T0EXrxN59hCaeP6ZH0feggt3OQbc/jJbNO7QXscwWrzJ/SE2pjMrSe6nStraYOGheavgCRSHXgCYIOeixCgI9Zikgp0Z4ywFNbGEG8OZZYT6M96ShgOEIEUYnzmuP1fk+nkUOodEymZVaQ2qulOUB6tVyHYz2KONB75VWTZCFyXL7fFW6XhjIq765j70h0VRfJ+QuXBXDn6n6VoLFRb4LPATKsEelh6lPAQfxeIVZFHjCBUpdStHOQXNVmD9i66NAk+eanjaB97fuczsITsV4Z2n/Kg3o0VSbjc6SUoPFfcz0Via/Z9mLRWo7yF8gq99WXoB5f4yxTApoH2dzaDNsn2kjqstAiW87U4u5MDpLOyPusvnY65jPYQ1pJPXb/0b4LStXyWFKtYbLxbvCPr6TKr0YBrSWWuWnPPnM9d5VTvnqKZ3x1uhZFk9jA3omy/XGzNXWThNSHxzSu9ojSl2PK/vRacQD3dbzmTgex2dGqd0B2j7T8ukrfCuVhmhhpCntRLNLxgmSXSDNf+bGJwU+RIGP3nmnlwmX+j7QGEeEC0yFjLHwVgTHPADmi7HHPYJ2f7fNrFlEn98FdT97/on6jY3amE41trnawuhhm0MRmkz32jA5V56HCFKjQpnYmlhftcJ0m5VpR21ovWTicJXw+lteYB2XOpYhLBLbm5pMmGAXNii2c9Avs5/v5+GHp6A1tpKZrWpV0O5cbKah/azFe7Uh9naZFjHu+yLGMXcZpoz6WPJVgNkqXMWxBwKAo51zzKQzCSxeVJ6rdP0q365Oow/zZph/nzS6Do0CJoKxCCUWkQBMKXOxYB7HMad+4FMY08gbpJF3hkZlivNS5wlEo6XMoTD6+nz6bEu/JJ985kUrECGOSeBiuiKAJScB5hxi4hM3dGOvGoA1bhPizQh3lvLRnYIkGi2hMP1/CkCikPHAw5LHAlNCXCw8SrGMfcECP2CcuGj3L9Fyo3dREgAA",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [981] -  River Basin Large Aquatic Resources
        [981] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1WTW/jNhD9KwbPEmBJFPVxc9xsGsDJBnGKPQQ9jKmRTUQRHZJKNw383wvqI5ZsOWmDHHrYGzUcvnkcPc7MK5lVRs5BGz3P1yR9JeclrAqcFQVJjarQIXZzIUrcb2bd1mVGUj9OHHKjhFTCvJDUc8ilPv/JiyrDbG+2/rsG60pKvrFg9cK3qxqHxQ652N5tFOqNLDKSetPpAPl96BojiQYnph+SmW+qx44B9ab0AwrdKVkUyE3voNd38z8OK1UmoDiRUs9nbJBU2h77JvTm/AV1L3B4wDgMB4xZl3R4wOVG5OYMRM3bGnRnWBrgD5qkYZtGFh/j9lGTFvUGjMCSY48POzzHhhn0u6NK/I1zMI0UxnTF4iMw/+B3BC3Y3QYKAQ/6GzxLZfEGhu52gTO03yKXz6hI6tmcnaBABwG7dJ6J9QU81veelesCle6C2H+fkTSIpvSI/QAq3u0ccv7TKGgfnv0Rd3L5F2wvS1MJI2R5AaLscut6DllUCq9Qa1gjSQlxyHVNglzLEonTILxskaQ2MSN4C6nNp/FuFGocZ0hccmK/iVjv7/kst8iNgmJeKYWl+aJbHqB+2V1H2R7deDR67dUIZGnk1j5fUa6XBrd1odxzb0U0U19DuQ9Xc/ijFE8VWlwSxAg0YFMXMVm51M+ZC5xSF/KYZ0kCQZ4xsnPIQmjzPbcxNEnvG3naC7y99YjF0WmOV7AusQD1DBbsWqpHKH6X8sEe78rGD4T629o1mrcXmEOhsXuR7aa9Vvc2W1Nzd+pFthx1mEujZNlrZCPHv5fFy48NltfSzLgRz7gsTmJPgx72AtdYZqBe3oUfQ/hNVqvi8L6Nh8+SN4c9+ZMuAw4jXndKbE9FikI/eHM5FWvg9E601s/qepYbVHOo1huzEI+2v3jNxqHg61GiUk0Ds4teaR6pv0EUJscd+Z3maseArvJ0YrvFp0oozJYGTGV7nJ0zTijwA0X9a238ksCnJPDZf96rblkeBQGj1EUWBS7F1dSFMOQuCzJOMYqTVc7I7s+uvLWz6P2boalw969kWOpoEp8udZcc1wVoPbkANSjL3nvJucywNIJDYTNiIzUOs0dZlQM3ktIwOZwlguGYF9tIlcqBt8VsdKSiYRJ+MFGFO4f8bwb0fX/8dFe0h61lbrNaJ7TfJ9vuaJeNee82pt2ezmIKCaerlZvEq8SlWYwu0Ahd7vN4ShmPYi8kO+dYR8k7LVPKUhtZ4uRKKvhyJY0L4r8L65eSyq9UUsKZxxIIXGQBdekKIxeCPHYzRoNkxXiENK4rVoPbUjybuJNb8YxqcgZalJMFqDVOZk8VGMEnt6hlpTjqg9EvgwCmEboBY5kVLXVXsR+71KecTSlHP/LJ7h/yrTMLExAAAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [982] -  Saltpeter Shore Sunken Resources
        [982] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XS2/jNhD+KwZPLSACelDPm+PNpgGcbBBlsYegB1oaWYRl0UtS200D//eCetiSLdubIIei6E0mZz5+M/w4M35F00rxGZVKzrIlil7RdUkXBUyLAkVKVGAgvTlnJew3027rNkWRHYQGehCMC6ZeUGQZ6FZe/0yKKoV0v6zttw3WHedJrsHqD1t/1TheYKCbzVMuQOa8SFFkmeYA+Tx0jRH6Aw/zIplZXq07BsQyyQUKnRcvCkjUiYwQy7T6XvZlFlykjBYn8Czb8wY5Jq3bZybz6xeQvQDcgwBcdxCA190BXUGcs0xdUVaHoRdktxArmqwkitw2q15wjNtHDVvUB6oYlAn0+HiHft4woXbnKtjfMKOqUcZXCV/K4uUbU/ltCqViCS20VZeV3v40UewHxAXddJtjGvWCIyb2wdU6LZOnnBaMruRn+oMLTWaw0KXGMYbrj5DwHyBQZOmEn6BABgd2d3HFljd0XSdtWi4LELI7xDbQMAVnQnR8kxyFODgv2G4NdP1TCTp46TsUffNPPP6Lbm5LVTHFeHlDWdldJrYMNK8E3IGUdAkoQshA9zVxdM9LQC3CywZQpJM5gjfn+g7fifcgQMI4Q4TRif3mxHp/zyfeQKIELWaVEFCqD4ryAPXDYh1lexTx6Om11aM2mvGqVCAaD23f3XojuVjxja4mrFzGCjZ1Gd9H1spyKj4moD5czfBryb5XoHFRktLUz2wXBy44mPh2hhcudXDiOb5pOkniWT7aGmjOpPqS6TMkip5f69N0ALvS43vhGY6fqhKyJt7JVKhc8KISoIHvuVjT4g/OVxqqq2jfgNa/9boEtXs5GS3k7j22m/3stktNHojl60rZYcZK8LL3EC+7m07PfQ5LKFMqXj6AVw38iVeL4lKkA0fbC3d++2jqypxD2VTmXfEaPbqP8CsBjTg/CbY5ot0a+K7t7Ez2DM8YjZEY2umnMs0UiBmtlrmas7XuoFazcfiG6tmpEk2L1h+9/tHl6Z6rE6kaLfNueDyknBkw9GTUFcNO1Y/wvWIC0lhRVek+r0evE1K/IN23KvSy4s5J603a+U+I5L133iupBLLUJYGJvcBPMcnMBaY2DTGEmeVZXhpatoW2f3Y1tR3Pn3cLTVl9fkXD+urqceZUfX0QTK6pYsnkjgvKDhqCdS5DBwPfK2oMpmvdwfZmY3O3Gx5OQM5w/A30wZXIaNJOjG0wbuhemA7drYH+NX9c9p353f1YO+uVei6oE9rv0ChCVxM8iWmxAQViEudcwCSuyhWUk0eQvBIJyMlvOom/owat8d/jjSm9p8oMPDdwXBObabLAJCQmDjzbx07guIFDSOCYIdoax6qzT0d6w4s0K+gKJjEtU93fP1x279bZuF7/l91eOrLLzcPblVhn+V06DCCxKc1c7JOUYLLwbRx6HmDTyXzXSiCjVlBXxwa3De5XON3pQX1wlk984rmegwO6CDEJLQcvHMvCtkMzxw+ykIQUbf8BHyZpJZMRAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [983] -  Mutant Aquatic Specimen Observations
        [983] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1WS2/jOAz+K4HOFtbv1y3NtN0CaacYd7GHYg60TSdCHSuV5Ha6Rf77Qn4kdpqk20EPA+zcBIr8+JGiSL6Saa34DKSSs2JB4ldyXkFa4rQsSaxEjQbRl3NW4e4y76+uchLbYWSQW8G4YOqFxJZBruT5j6ysc8x3Yq2/abGuOc+WGqw52PrU4PihQS7Xd0uBcsnLnMSWaY6QT0M3GFEwsjDfJTNb1quegWuZ7jsUeitelpipgaE1VLPfd8tFzqA8klLL9v1RUt3O7ILJ5fkLyoFjb4+x540Y+33S4QGTJSvUGbCGtxbIXpAoyB4kib0ujX74FneIGnWot6AYVhkO+Pj7dv44g3ZvKtg/OAPVlkLvdd/a3su/01nfLaFk8CAv4IkLDTAS9OE4xlj+DTP+hILElk7SoVr2Q10CA4d9/s7Y4hJWTaDTalGikL0T/dg5iZ3AdN+wH0GFm41Bzn8oAaOftiWgH+KOJ8+wvqpUzRTj1SWwqk8PtQwyrwVeo5SwQBITYpCbhhO54RWSDuFljSTWeTqAN+dS/TTerUCJhxkSSo7ctx6b+x2fZI2ZElDOaiGwUp8U5R7qp8V6kO2biA96b7QuuMiw+WXPsO4fuxHmWtr8Gy/yPKOrrETxtf7orFokCtdNS91F2VXfVHxOcEO4hu1fFXusUeOSKHfB8jOgkQ0OdUPLpAA2UCdzsLAz33eDlGwMMmdSfS20D0ni+9fGmw5g2xUCX8+IYxwv9H95BoViMhVqKXhZC9S4N1ysoPyT8weN1PeavxEedr9H30ocpbYTtfG6VqCbVW+cKMGrxUfMTWdgPscFVjmIF43QKW5/cQGlRONjwF94nZbbkEYath9tFXa0j6ocojbUuhNsfcxT4NnOVuWYr5HSCW+dnq7iaaFQzKBeLNWcrfTcsdqL/fJuVoxatINNHwYd/ECbdgIv2p9PJ2e9Xg/6jtTX0zd8rJnAPFGgaj379P6xX2T/rZY+WjK/S+BjJdC/ufvBNx/0MojyIgpNn5ppEVA3B4+CbyFNLddz0txE187I5nvfzLod9X4raPvZ/SsZNzbPPNHYknqNYpIoEGktpBr1YetUfq5yrBTLoNRJ0c5ahemK19VIrZkc+1uHM94AQ+2pFgVkmJS6Gx1cOfUEemf38jYG+WV2991A/OkxqI21ZKaz2iR0OBi7caiPrXindqh8B6XmWIUZ+kVBnSJKqZtHAQVApKmTeg64mR26AdkYb0spOB7AleQywxLlZLpCtXz5XU3/l2pKfbR8J/WoH7g2dQMrpGnhAYUwNNMgtyM7bxtXi9tRvI9Ch9rfJ2cTOrmuFVRqMn2sQbFsohdVtsJq8jWVKJ5Az0Q5sf+wx8ufG6AbpmZBLSfNqFtkGU2DIqN+YVmh56eFW6Rk8y8QmAvLPxAAAA==",
                "AH4_H4sIAAAAAAAACu1WyW7jOBD9FYNnEaOVWm6OO8kEcBa0M+hD0BjQVMkmIosOSSXtCfzvA2qxJW9pN3LoAeYmkVWvHouPVfWOhqUWI6q0GmUzlLyjy4JOcxjmOUq0LMFCZnPMC9hupu3WTYoSN4ot9CC5kFyvUOJY6EZd/mB5mUK6XTb26xrrVgg2N2DVh2u+KhwSWeh6+TiXoOYiT1Hi2HYP+TR0hRGHPQ/7QzKjebloGfiO7X9AofUSeQ5Mdxydrpn7cVghU07zIyl1XEJ6SfUbtyuu5pcrUJ3AwQ7jIOgxJm3S6TNM5jzTF5RXvM2CahcmmrJnhZKgSSOJ9nG7qHGD+kA1h4JBhw/Z9SP9DLqtq+T/wIjqWgpt1F1vdyf/XuP9OKc5p8/qir4KaQB6C+1xPKu//hWYeAWJEsck6ZCWSWQk0AnY5u+Cz67pojrosJjlIFUbxFx2ihIvtP099j2oaL220OUPLWnz0kzmH8XkjS5vCl1yzUVxTXnR5gM7FhqXEm5BKToDlCBkobuKBLoTBSCrRlgtASUmMQfwxkLpX8Z7kKDgMEOE0ZH9OmK1v+UzWQLTkuajUkoo9Cedcgf10856kO3eiQ9Gr6xqgUy0WJr3yovZRMOyqoxb7o2IhvJzKHfhKg5/FfylBIOLbObYJHZSHDlejH2WupgGbIoDz6e258VTYAStLTTmSt9nJoZCyVMtT3OAzeMOSUSOc7wWAlaDMZWv1KDdCbmg+Z9CPBv/tlB8A1r912/P7CrQ5gDtK2yW6lP6TmgqTes80VIUs5Pu90W++jaHYsg0f4VJfhTY9jrAY5hBkVK5MtiN4aY6ZDRXYP004wr4iyin+eawPQuXxBuD7YGOmhyi1rV6lHx5LFIYuN7G5FisntGJaI2dUfUw0yBHtJzN9ZgvTDtx6o1duVeTQynrfmU+OoW5vak7ofcv60Bp9sIg3u1JJ/u7GQnaotTK8Cu8lFxCOtFUl6bfmZljV5s/J8Fz9fS/Ps7TR3vn/pl33i18oRekNmE48iKCfQhcTFOf4sBzfaBp6Dt+gNbf28rXzKVPm4W6+D29o34VDOzoeBW8YTDLqVKDq1z0S7ZzKjs3KRSaM5qblJhQtcFwIcqiZ4YSP4h35wyvP/NFJlIpM8qaR9Uw339Au+NVsLbQbzOemyJbT6t1Fdg2z25Hio/fxZWh+kY1yMFQ6rkUeSkBdZBHJru1Ot/osu6nqg3Xba8oQbQQxd9PceRh9/vgYoAHt6WmhR4MX0qqORuYcYAvoBjcTxXIV2rKjhq4f7ioi96JeOA9dLQbOTZ4fgoYYtfGfsYojlxmYx+mHnGcFDwSobW1r81THboS5heuWKl+H2keqPX/NaXuK/PsYW6yJ8kd/Zmh7hd0NJ2GmU2DGPvulGHfDTMcpQCYZdPIt4PMCyNS1cAat6FY6dw5Q+dOpfNu3IjRkKQxJlPiYD9yPEydOMJ+nJKMeDS2sxSt/wVNxQdRfhAAAA==",
            },
            AmountRequired = 4,
            UniqueFish = true,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Glass Discus"] = new List<uint>()
                {
                    47426,
                    47453,
                    47506,
                },
                ["Glass Stitcher"] = new List<uint>()
                {
                    47425,
                    47437,
                    47444,
                    47452,
                    47505,
                },
                ["Iceglass Floe"] = new List<uint>()
                {
                    47508,
                },
                ["Isosceles Amethyst"] = new List<uint>()
                {
                    47507,
                },
                ["Super Starburst"] = new List<uint>()
                {
                    47509,
                },
            },
        },
        // Export for Mission [984] -  Vitrified Freshwater Fish Observations
        [984] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1WS2/jNhD+KwbPIqAHRT1ujpukAbzJYp20h6AHShzbbGTRS1LZTQP/94J6xJKjxEkQoHvoTR7OfPPN+OMMH9G0MnLGtNGz5Qqlj+i0ZFkB06JAqVEVOMgezkUJ+0PeHV1wlPpx4qCvSkglzANKPQdd6NOfeVFx4Huz9d81WF+kzNcWrP7w7VeNQ2MHnW+v1wr0WhYcpZ7rDpBfh64xkmgQ4R4lM1tXm44B8VxyQME/oNBFyaKA3PQCvb6bfzytVFyw4oWWej6lg6aSNuxM6PXpA+he4vCAcRgOGNOu6ewOFmuxNCdM1LytQXeGhWH5nUZp2LaRxs9x+6hJi/qVGQFlDj0+9DCODjvod6FK/AMzZhopdFnpkf4HbfT1mhWC3ekzdi+VBRgYunICZ2j/Brm8B4VSzzZpTMs0thLoJez6dyJW52xTFzotVwUo3SWxfzZHaRC55Bn7AVS82zno9KdRrL1ptvPXcvGDbS9KUwkjZHnORNn1A3sOmlcKvoDWbAUoRchBlzUJdClLQE6D8LAFlNrGjODNpTYfxvuqQMM4Q4TRC+dNxvp8z2exhdwoVswqpaA0n1TlAeqn1TrK9lnFo9lrr0YgCyO39r6KcrUwsK0n4557K6Kp+hzKfbiaw00pvldgcZGfeQlPALAfAsfEpwQznnFMIHJ96lKXuznaOWgutLla2hwapbeNPG0BT5c7onH8MseZ1BuRT66ZYqWpCmYhL6XasOJ3Ke8sSDct/gRW/7Z2DebpHi5ZoaG7l+2hLa67oa2p6QDxIjuFOsyFUbLs7a/j4W7QC5/DCkrO1MO7EW40/Car1r9zbCxH6hyg+dRW08Tta3lv5KCMEa8bDddKbIdkG8u7yEahb2tvIt9JdxD7CuHWz16j6dKAmrFqtTZzsbH7y2sODu9X/VSpVLMg7UdvE4yM+yAKk8ON/+qbwT4zukHXqfobfK+EAr4wzFR2h9p3zAtSPyLdNyt0zHFUc28Q1/tVNCqYNynjP5bAR//z3jDNc88NgzzADBjBJGM5zsCOVWAkyfzcjxOCdn9107R9694+GZqBevuIhpM19IKXJ+vVlhWgcyjNZGFEudJrsR1sA++1Jl1wKI3IWWE7YzM2DtONrMqBG0pJmBw+YYLhczK2mSq1ZDksCjsY2wLCJDzycgt3DvplXv77Pfzh7WuDrWVm21h3sL+P2y1sPxvz3m1MtD2BJS5bEsJcHAeUYxJlHDPm5tgPA5otwedx5KKd81xArxQwkxkrzORE/C2rX1449H/hOB8RDneznEJIcOASign1chz7WYhJTMMAvCAKl1E9mRrcluLJBE/+EEaJpQA+ObON/MEMqInNNLnKNKh7ZtedHj4qKQ/CjHHAYcQyTJIoxlnGKc5iSLLQA4ggR7t/AescJCReEAAA",
            },
            AmountRequired = 17,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Cobalt Bijou"] = new List<uint>()
                {
                    47429,
                    47435,
                    47461,
                    47465,
                    47511,
                    47531,
                },
                ["Kintsugi Chip"] = new List<uint>()
                {
                    47512,
                },
                ["Opalescent Stingship"] = new List<uint>()
                {
                    47513,
                },
                ["Pearl Shell"] = new List<uint>()
                {
                    47428,
                    47434,
                    47460,
                    47464,
                    47510,
                    47530,
                },
            },
        },
        // Export for Mission [985] -  Saltpeter Shore Large Resources
        [985] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XS1PjOBD+Kymdoy1blmU7t5BlWKoCwxKoOVB7kK12osKxgiyzw1L571Pyg9jBgYXNVs1hbk6ru/V16+tHntG0NGrGC1PM0iWaPKPTnMcZTLMMTYwuYYzs4VzmsDsU7dG5QBMSRmN0paXS0jyhiTtG58Xp9yQrBYid2Opva18XSiUr66z6IPar8sPCMTrb3Kw0FCuVCTRxHafn+W3XlY8o6Fk474KZrcp1i4C6Dt2DQPw+hNZKZRkkpmPodtXI+9cqLSTPDqTUJYxFe0hY1EPSBzqN1SOgScqzor3hiyxWp09QdDD6ey79fnCsfR9+D4uVTM0Jl1WIVlC0goXhyX2BJn6TcRa+9tv1GjVer7iRkCfQwUP27Vg/2aQ11fIfmHFTs+a2gK959vRNmtW5gNzIhGdWq01g53yaGPkIi4xv2sMh/rLQYfvPvsc8r0Fys+KZ5PfFF/6otAXTE7Sp8cZ9+TUk6hE0mrg24Qcg0N6F7VucyOUZX1dJm+bLDHTRXmI5JtDECxz6Cn3PVbjdjtHpd6N5U+D2FW/U4m++Oc9NKY1U+RmXefsw2B2jeanhAoqCLwFNEBqjywoEulQ5oHHt4WkDaGITM+Bvrux7fNLflYYChhEijA6c1zdW5zs8iw0kRvNsVmoNuTlSlHtejxbrINpXEQ/eXmnVBFkYtbG1L/PlwsCmasg77A2Jpvo4kLvuKgy3uXwowfpF4IeMEJLiNE4ppjFzcEx8FzPgPHJch4SCo+0YzWVhvqb2jgJN7mp62gBeGkVQtb5DGC/4Moe4XFpXl0qvefaHUvfWuO0434BXv628APNSf1W3bOuxObRBtZXZiOrIqRvYTtb6XBit8s64fN/c8Trmc1hCLrh+OgKuyvFtAb+rstFvFWvJO+H3vBFmg6ztdiFeyNxq3ci1bWLE+c2rGkzTk6/hYRBZ19e/iXfA+LaAGy03/ahqyYeiCnxik1Rb/ue4et4+Hlljbut0mhrQM14uV2Yu13bYuvXBfgFXK1ip62luPzqjZmCeeIEf7W8yb+5Cdn1qO2lbPtfwUEoNYmG4Ke3At/vZgZp6p0Y+Wgo9xUEWv0XXD7GwqzXIrLcp9EFm/E8U+Oybd7o1p2nIPZJin5MEUwAfc5ECTgkIiCgPkzhA27/adt3s8Hcvgrpj3z2jfuv23fBw6/6zlMl9wXMxuuC54VpJ0Zs27ls52tv9nlGtMF2rMu+oDVQH9aP9jcnrb8KhvbjUKU+a5bEJx4/8dxZFfztGP83/m93Y//Swt8ZWMrNZrRLaHf/N0LeftXinNkThDt18oMwLicCBEBGmEU1xHFOGE0eQkEQBkJih7fg1ndjhAKbrtUpWWuUqU0tZmKNz6dPkGSbhLy4dh0sCQvBEwHGSsAjTIAlx5CYBZg4F4rFIBE48yKXgcABXKuN2ExCjU8h+HiL9Yk5+TObQFDjzXY5pQEJMacRwGHgRZm7gMU4SJ+GkGnq13wbiyQiPFjzbgAE9WqyUhtGc6yWMrqFQpU6g6P8REsIDh5EAs5hSTH1P4NhzfeyFMYeYEu5DgLY/AGpfSVeJEwAA",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },

        // - - - - - - - - - - 
        // A Rank
        // - - - - - - - - - - 

        // Export for Mission [986] - Cultivated Specimen Survey
        [986] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XyXLbOBD9FRXOZIYbuN0UxfG4SnZcljw5uOYAgk0JZZJQANBjxaV/nwIXi9Qap1yZHMYnGeh+eHh6jW69oHGl+IRIJSfZAsUv6KIkSQ7jPEexEhUYSG9OWQnbzbTbukpR7ISRgW4F44KpNYptA13Ji2eaVymk22Udv2mwrjmnSw1Wf3D0pxrHDw10uZovBcglz1MU25Y1QD4NXWNEwSDDOktmsqyKjoFnW94ZCl0Wz3Ogqpdo98Oc88dykTKSH5HUdnx/IKrXpn1mcnmxBtk7GO8wxnjA2O9EJ48wW7JMfSSs5q0XZLcwU4Q+ShTjVkY/3Mfto0Yt6i1RDEoKPT7+bp4/VNDpUgX7DhOiGit0p+5mOzv6u232fElyRh7lZ/LEhQYYLHTXcY3h+h1Q/gQCxbYW6ZCX/VBboHdgp99HtrgkRX3RcbnIQcjuEP1lpyh2A8vbYz+ACjcbA108K0HaStPKz/nsH7K6KlXFFOPlJWFlp4dpG2haCbgGKckCUIyQgW5qEuiGl4CMBmG9AhRrYQ7gTblUP413K0DCYYZoPDJHo0mVK/ZEFKSj2QooK6AczSrxBOuR84eDjiA0nJDZnVfv6nwlSD6phIBSvZMOO6jvpsZBtvWNTkT17t1YaKb4Slc0KxczBav67dxyb202Fu9DuQ9Xc7gv2bcKNC5ygsjyXADTCqLI9BI7MkNMwQwxOJmT4cwNQrQx0JRJ9SXTZ0gUPzQG1hd4Lf/Aj9zjHO8gHU24LHhBFguupIa84aIg+Z+cP2qQ7j35CqT+X69LUK+VmpFcQle57aa+XFfD7VKjgGcH+p3qMGdK8LLX4Q6kfynz9dcllDdcjaliTzDLj2Jbbg97CgsoUyLW70C6Br6X8IlXbXwX2Kyc0WaA5vhagSZve/9rVuqoOSv0Wxh9iAZ//laHMyL00X9EgQPJc8FWe1dqAwLsuK8hW/Yngg6R2IljBfBKXZNn3QQ+WAbSRTjOFIgJqRZLNWWF7o92s7FbnfUoVImmAesPvU5zoJ24AY52JwrdeY4OB3qM6Z7Jribu4FvFBKQzRVSle7Sek35NobwV9X3q5yj80Le2rb++Q6gH6+RUQbzJ1f+lfd9k1nsJk0oqXjTO6b8kv8DHvfZig5X6YWSZQeQmpmdn2AyzxDUjSijGNk2SLEObv7v+0v4+eHhdaFrMwwsa9hrsnOg1nzgvIB39xddkAWLQGe1TQl6lUCpGSa4l0mc1AeOCV+UgDMUe1m/lQBR3OHyH+qRKZIS2ldBSxxE+M+fijYF+m99J25nkpycRnaxXJlrGWsH+bPLD86QObhJbIOeI9bfmo7adEp8QM/Ss1PTCgJoJJWBG2AkDJwq9xCVoY+yb68QVZ4qI0USQROnB77fx1oHK/d9q9p7V9PC7Z6Rjb+jWSJBEWRpkjolDapleSEIzotQxie162MsSGnhR/Yo1uC3FM74ezuEEY+zjNDRdmlmmh7PITDIXm65P3cyynYgmGG3+BQH9j32zEQAA",
            },
            AmountRequired = 2,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Bottom-layer"] = new List<uint>()
                {
                    47524,
                },
            },
        },
        // Export for Mission [987] - Stardust Bait Test
        [987] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XTXOjOBD9Ky6d0RQCBIib481kU+VkUoO35pDag4DGVgUjjxDZZFP+71sCY4O/MjvlQw7xSW41r183T63mDY1rLSe80tUkn6PoDV2XPClgXBQo0qoGC5nNqShht5l1W7cZipyQWehBCamEfkURsdBtdf2SFnUG2c5s/Nct1p2U6cKANQvHrBocP7TQzWq2UFAtZJGhiNj2APk8dIPBgsET9rtkJot6eSIxj9jeHiNKh4w6EFkUkOouE4/YpO/mvM9Cqkzw4gQR4vg+22PiD5kMiY4T+QwoynlRdRG+impx/QpVjyM9n5zfvS7+BPFC5PqKiyZFY6g6Q6x5+lShiG5egB8e4vZR2Qb1gWsBZXpKVB6x/X0Yf1h7t0NS4l+YcN1q6phA/fAAzNmTlrMBmy14IfhT9ZU/S2XwBoYuWdca2r9DKp9BoYiYEnYxvUGErpxXYn7Dl03e43JegKo6VCOTDEVuYHsHdAdQ4XptoesXrfjmyJoXMZPxP3x1W+paaCHLGy7K7l1jYqFpreAOqorPAUUIWei+IYHuZQnIahFeV4AiU9YjeFNZ6d/Ge1BQwXGGCKMT+23EZn/HJ15BqhUvJrVSUOoLZbmHerFcj7I9yPho9MarFUis5cocX1HOYw2rpsXuuG9ENFaXodyHazj8VYqfNRhcFBIW0iwJcAY0wR7zAxwG3MWu7UPo5ozaHkdrC01Fpb/lJkaFosdWniaBbe8JfOac5nhV1DCKNVdZXWmDdy/Vkhd/SvlkELrO8QN489/YK9Dbg990PWvTCDabJrOuJWxMbfoeCUxH6jBjrWTZuwVPPD4TS1B7neZOlNst052+ULv/cyx0x196Hq77xT4gY7s9MlOYQ5lx9XqBLBvgP2SdFPt1az0cn20ddkU46XKMWt9rpsTqVKSAOu7W5VSsgdOZaJ2fWIKs9R1/6QprDs0416AmvJ4v9FQszeVF2o3909RMOLVqb0ez6DX6IxeKG1B2MBmcGzXMdNK1tU7G3+FnLRRksea6NheoGX8+gLZ/SY2fsmv8Liayb2Xx+mMB5b3U41SLZ7jNoNQiNSNhW9hfleF7Ouy3c4cnxGYuZjYz7dyxccKpg3PCQ8ZYCmlO0Prvrp9vxvbHraFt6Y9vaNjbqeOf7u3jWknFR/ECimJwD5Fz5dxWw9TQRGodxktZlwM3FHmU7Q9P7nCuDU2kWuU8hbgwyj0+gFJG35kZ6dpCH+ZbZjcQ/PYYYB42lompalPQ/mCwGQfMsjXv3I6pvaezIMsCAi7DJCGAPU4oDlM3wJQmrsec3IXAQ2vrUEfh6QRul0Jz005H1+VccTN8X1pNx0Xx/8X1qaaLqsnzCac2pZgxj2HPdgGz0M+wnUIYBqlNUzs8qiZ2Rk2VrFIooBo9AFeXb0yfUvqQUiIUvNxxXOyHqY09CFIcMprhhCV2SHnmO7nXXIAtbneFjfD262RkaI1mUOnhlxJPeepBGmAv9UPsJZmPmeNwnHDuhyEnhFGG1v8BUQAx63wTAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [988] - Elemental-esque Aquaculture Specimens
        [988] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XyW7jOBD9FYNnsaGF1OKb40lnAjgL2gn6EMyBoko2J7LoUFQmmcD/PqCWWLLlbPAAA0z7JBdZr4pPr1ilFzQptZyyQhfTdIHGL+g0Z3EGkyxDY61KsJBZnIkctotJu3SeoLEbRha6VkIqoZ/R2LHQeXH6xLMygWRrNvs3NdaFlHxpwKoH1zxVOH5oobP1zVJBsZRZgsaObfeQ34auMKKg52G/m8x0Wa7aDIhjk3dSaL1klgHXHUenu819P6xUiWDZAUod1/d7pJLG7bsolqfPUHQC052MKe1l7Leks3uYL0WqT5io8jaGojXMNeP3BRrThkY/3MftokYN6jXTAnIOnXz8XT+/z6DbuirxN0yZrqXQRt31dnf49xrvmyXLBLsvvrNHqQxAz9Aex7P69h/A5SMoNHYMSUNa9kMjgU7Alr8TsThjq+qgk3yRgSraIOZlJ2jsBTbZy74HFW42Fjp90oo1lWaYv5Hzv9j6PNel0ELmZ0zkLR/YsdCsVHABRcEWgMYIWeiySgJdyhyQVSM8rwGNDTEDeDNZ6C/jXSsoYDhDhNGB9Tpitb7NZ74GrhXLpqVSkOsjnXIH9WhnHcx278SD0atdtUDmWq5NvYp8Mdewrm7Gbe6NiCbqOCl34aocbnPxUILBRTxNqJeEHIfE55iwOMaR6wc49AknwGMS8QRtLDQThb5KTYwCje9qeZoDvBZ34Efu4RxPshJGc81UUhba4F1KtWLZ71LeG4T2qvgJrPpfV59ZLUCbI7R1eCFyY70Rq6pSw2+2hS7YU8fmucbWuNZ8ECcwd1IbZK6VzBefCUP3w7j+QBjb64SZwQLyhKlnE6nZ+HqrpCwrwDqcwBDwbQG/ybLZ326sLS1vrwnuXF8754m+Ubv7C3YCur7hq4besvVB8OAT4B/haMD5toAbJdZ9JmrLsZkIqGu4r8GPz0UP/vNsNO7mNpmkGtSUlYulnomVaeNOvbB7zVQTW6nqOcE8dBriVZ49/1xCPuFaPMI82wpyoB96AY32R6I3phszh7WdoK38H/BQCgXJXDNdmiHDDHq718HHqvmzxdjbuF9HH6iGD2u6u2tfpx9S2yc08y+J46vvvNttPOICpRz7LHQwcW0Xx6EdYeZwSnwvYA4J0OaPtt00HwN3r4a649y9oH7roR453Hqu1iwTGkbnqzUUXEvV65XOWwydJ5BrwVlmaDHh6g2TlSzzzraB2iA02p33gv60GprApUoZb+qsOQyN6DtjLt1Y6D/zmbQdWr48qhhnY5kaVitCu8NLM7KYx9q83TYk4I7YEi+hQRpG2A5sFxOXhJgB9XHMaZzSiAEPYrSx9sXkHT7AVCYrmbNS/xLR/0NEHELHo8zBLKEMk8hOcBgQH7sJRCROfZ56ZFBEbxxgKmOW6dGJ+FOWR9fRAeF49D3hDAvwl46uj6IjloS27acuhpBRTNwoxGEYpTgOaUrBJSn1WNX5atwmxckIj04zWEGuWYaheChhNHkoGS8zXSoYmc9MsYK86H/UAQfb9r0Ee7YTYuLbKY5oHGDHi7lvO0kaexxt/gHTpl913hMAAA==",
            },
            AmountRequired = 15,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Cobalt Bijou"] = new List<uint>()
                {
                    47429,
                    47435,
                    47461,
                    47465,
                    47511,
                    47531,
                },
                ["Codmonaut"] = new List<uint>()
                {
                    47533,
                },
                ["Opalite Impesctor"] = new List<uint>()
                {
                    47534,
                },
                ["Pearl Shell"] = new List<uint>()
                {
                    47428,
                    47434,
                    47460,
                    47464,
                    47510,
                    47530,
                },
                ["Selenium Herring"] = new List<uint>()
                {
                    47532,
                },
            },
        },
        // Export for Mission [989] - Prismatic Pull Distribution Survey
        [989] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1Y227jNhD9FYPP4kKUqOub42TToE4arBP0IegDJY1swrLopag0aeB/L6hLLNlyvAlcYLGNn+Th8HBmeHh4eUHjUokJK1QxSecofEEXOYsyGGcZCpUswUC6ccpz2DYmbdNVgkLLDwx0K7mQXD2jkBjoqrh4irMygWRr1v6bGutaiHihwaoPS39VOK5voMv13UJCsRBZgkJimj3kt6ErjMDr9TCPBjNZlKs2AkpMeiSEtpfIMojVgYpQYpJuL+t4FEImnGUH8IjlusFOYK7TC6wf9zgSj4DClGVFO8JXXiwunqHo5OrsQDp9SLedLraE2YKn6ozxKmNtKFrDTLF4WaDQaSbA9fdxu6hBg3rLFIc8hk487m6KVr/2VttV8n9gwlRNoiFGuv4emLUzkXYDdrdgGWfL4it7FFLj9QxtdrbRt3+DWDyCRCHRNTsQAu0N2JbzjM8v2arKe5zPM5BFO4imSYJC2zPpXvQ9KH+zMdDFk5KsWbJ6Iu7E7G+2vspVyRUX+SXjeVtbTAw0LSVcQ1GwOaAQIQPdVEGgG5EDMmqE5zWgUBdmAG8qCvVhvFsJBQxHiDA60F6PWLVv45mtIVaSZZNSSsjVibLcQT1ZroPR7mU8OHrlVRNkpsRaL1+ez2cK1pXEbmNvSDSWpwm5C1fFcJ/z7yVoXEQCK2LUcTC14xRTYgH2XdPFSWoB8b3IpHGMNgaa8kL9keoxChQ+1PTUCbyudc/VOn0oxvMyh7TOdzSWaiFFVkrQwDdCrlj2mxBLDdVKyJ/Aqv/aXoB6XY2V/LWrs2nUKbbrtDHVdaDE09LUYs6UFHlnOxzofs1zbb3jq0oIzC+mga7ZU8dmky9m90ftvUEtz++MOoU55AmTzydIx9Qze1/AuSgb/9axthypWg/NcnVt6n7byrxmuqN//cJYRBfmINyPpDzQ+b6AO8nX/cRqy7sS8xxL16nueYrUeoDvT67tzlcgSnXNnioW2V0WWQbSijBOFcgJK+cLNeUrvTOTumFXKqrjWynrrV9/dDa1gZ3L9pxg/xT0xglGH71azW6X5jf4XnIJyUwxVerTgT7bHVivR9bfEK3fWi89x0GqH+P0D/O06zXIveMkewdx+n4no8BH57y7L0S261kmxQ5xCKYuZdhPgwgTk7kBmD61TAttjF91I/iwWPzn8v8rKP3/Q9T1ZeFT1T9V/SdS9TSxE89lLo4j4mLquDYO0sDEUer7MYuSyItTtPmrPe43rzoPr4Za6B9eUF/xHTs4rPizMl9CPvqdq/4thbxVm6sEcsVjlumC6IFqh/FKlHnHbeihxgl2b9p2/xFEy/OslCmLYZZpsW3ScALnyAODszHQT/PStb0ufviSqDtry0RXtSpo99rYXBb1Z23eug1Rt0MzyoCQyPdwYEcephYk2CdOhG3qpyyNk5i6UXV42KXRGweHS5ElozOpix6xeHlyKn2YO8Mc/KTS7UmoZDq2w1KSYteiFFNmE+z7iYmjwHYS2zYj8L1KsWrcJsTxCI9uJS9WTPF4dFtm2eicF0ryqNSb32hWykd47r+ERMQn4CcedjxNWmYzHNCAYRKDQ20/ImDHaPMvpxhiq1wXAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [990] - Aquatic Glass Resource Distribution Survey
        [990] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACt1XW2+jOhD+K5Gf4QiDub6x2banUtqtSo/2oToPBobEKsGpbdrmVPnvK0NIICGpturD6ryR8fibb8ZzyzuKa8WnVCo5LeYoekcXFU1LiMsSRUrUYCB9OGMV6MMfVbluflMxh06hvZF3P69zFNlBaKA7wbhgao0ibKBrefGWlXUO+V6s9TetgRvOs4W20HzY+qvB8QIDXa0eFgLkgpc5irBlDZDPQzcYoT+4YX1IZrqolx0Dgi3yAYXuFi9LyNQ+hoOIEGzh/i37YxZc5IyWJ/Cw7XnhATHPHRAb8o5T/gIoKmgpOwuXTC4u1iB7vroHkO4Q0uueiz5BsmCF+kZZ47EWyE6QKJo9SRS52wfwgmPcPmq4Rb2jikGVQY+Pd3jPG8be7q4K9h9MqWqTaCwjveAIzD54SGcL9rCgJaNP8pK+cKHxBoLOO8cYyu8h4y8gUIR1zE5QIAODXTi/sfkVXTZ+x9W8BCE7IzpNchQ5vkWO2A+ggs3GQBdvStBtHeuHeODJK11dV6pmivHqirKqi62JDTSrBdyAlFTXMkIGum1IoFteATJahPUKUKQDM4I341J9Gu9OgIRxhshEJ85bi835nk+ygkwJWk5rIaBSX+TlAeqX+TrK9sjjUeuNVpsgieIrXb6smicKVk2L3XPfJlEsvoZyH67h8E/FnmvQuIj6LnWClJhF6rkmASs0AwK+aRM/c1Mvx5jYaGOgGZPqR6FtSBQ9tumpHdjVuu+F7mmOlzrtX6kCMYmFWghe1gI07i0XS1r+zfmTRuo6yE+gze+2CvWpBKVd6epRix7YEsRBnd7Qt90RirD/l2U0Q+/nAqpbruJMsRe4zqFSLNO9eY+mo9VEj2BfN7SOSqIEr5qK3GrtLDat2DjPsYdqOT3UGcyhyqlYn/VyzJsDVNsPNgb6zuu03EVtoGJ74U7hyJdjlQGxEa0HwVanLPmu7exUTtkaKJ2x1umxJfBa3dC3LgC6dOJCgZjSer5QM7bUIwy3B4c11Sw7tWhnpP7odf+P82JkCDi+Gx4vFGeWAb3FdO2vS/N7eK6ZgDxRVNV60Oo16TD3fyspP06yMcX/b9p8WZJ89s17LVbP/oymYAa2b5kkJ8QMwbbMrKBpEVIXwiJAm3+7HrtdpR93grbNPr6jYb91CTndb7VLk8uSv4KQg9mAzwVnVwD6urbUKsRLXlc9tbH12A2986unblNJLQqaQVLqFrf1w9Vj4+xa524M9Mf8v9gP6U+P5uSVrrRkqqPaBLQ/rLcjWn+24r3aWO7288wP/JC4vmnTHExiZ44ZUAImhtRKc5r6roObPGtxtxTjiTmJn2uqWDa5KqmUk3uQvBYZTL4zqQRLa926JkktXmA9XB7CwgUcEsvEGfZMQsLMTHMnMz1w0hB7BfVxgDa/ADXdD5qkDgAA",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [991] - EX: Red Cosmomaggot Field Test
        [991] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XW2+rOBD+K5WfYYW5GMNbTrbtVkp7qpLVWanaBweGxCrBHGPaZqv895W5JEBCK1Vd7T7smxmPv/lmPDMe3tCsUmLOSlXO0zUK39BlzlYZzLIMhUpWYCC9ueA56M3vebarv5lcQ6fQnEi6z5sEhTYNDHQvuZBc7VCIDXRTXr7GWZVAchRr/X1j4FaIeKMt1Atbr2ocQg10XSw3EsqNyBIUYssaIL8PXWME/uCE9SGZ+abaHkMxcMzFljti5IwYdSAiyyBWnScutnBfzf6YhZAJZ9kEEWwTEoyYEG/AZEh0thLPgMKUZWVn4YqXm8sdlD2O3gjSG0KS7rrYE0QbnqpvjNcuakHZCSLF4qcShV57AYSe4vZRgxb1nikOeQw9PmR8jgyDbXdHJf8L5kw1SXQuIwk9AbNHN+e0YMsNyzh7Kq/Ys5AabyDovHOMofwBYvEMEoVYx2yCgjsw2IXzG19fs23t9yxfZyDLzohOE51jvuWesB9A0f3eQJevSrK2jvVFLEX0woqbXFVccZFfM553sTWxgRaVhFsoS6ZrGSED3dUk0J3IARkNwq4AFOrAnMFbiFJ9Gu9eQgnnGSITTew3Fuv9I5+ogFhJls0rKSFXX+TlCPXLfD3L9sTjs9ZrrSZBIiUKXb48X0cKirrFHrm3STSTX0O5D1dz+D3nPyvQuIjZsYVTFpjBiqWmS7FvBrAC0yKxZ1MKKzclaG+gBS/V91TbKFH42KSnduBQ6z4JnGmOD5BczEW5FVu2XgtVasg7Ibcs+02IJw3SNY8fwOpvLS9BHeqwbnxdXbab2rmuQltREwEX+7opdZiRkiLvvY4Tx5d8C3JU+Lc8P2yhMPjFMtAte+2JMNEy/eScO67lI31X6+uH+McG8juhZrHizxBlk65YTs+VBawhT5jcfejNGOFXUa2ycXgbDZsEB4VjrCZVBhzOaC0lL6Ys+Z7tHFSmbA2U3rHW6ulimqUK5JxV641a8K1+1HCzMa6yevypZPNq6kXvPZizPIZsphRsC9XFUuss9bzUYJ7c3E0CueKxfukn5ynH94Lx3PHuIKNnn65pdhXyAD8rLiGJFFOVfp71cDUum3+yPv7PyX8hJz+bPr0eH2CXuUkSmy6xHdNNAzApw8SkFraIl7iMpCu0/7Nr8u0s/3gQNH3+8Q0NG77n+dMNP3riRcHz9QvbDd4m/F5sDqWkA6INNQqzrajygRoKXS8YD1TOcNal2lIlUxa3zfX8D4EXeB+Mld7eQP+Z/5vjkPDp0SB6YYWWzHVU64D2h4V2RNDLRnxUO5e6vTTzGSQkSG3TdRLbdFPPMmnqMZM4fkBXXmLbgOs0a3Bbipd/XJgXo/Hg4opDllwsoVTDacWnYGGaWCZOKTVdGqzMgGBq2r67coAmfkIZ2v8NKN0q1xUPAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [992] - EX: Large River Resources
        [992] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XyW7jOBD9FYNncSBRu29uT5IJ4CyIE/QAwRwosmQTkSU3SaU7E/jfB9QSS7Ycd4I0MEDnZherXi18rCo9o0mpiylVWk3TBRo/o5OcJhlMsgyNtSzBQuZwJnLYHvL26JyjMYliC11LUUihn9DYsdC5OvnBspID34qN/qbGuigKtjRg1Q9iflU4QWShs/XtUoJaFhlHY8e2e8ivQ1cYcdizsI8GM12WqzYCz7G9IyG0VkWWAdMdQ6erRo67LSQXNDtQUocEQa+oXmN2KtTy5AlUx7G/E7Hv9yIO2qLTB5gvRaq/UFHFbQSqFcw1ZQ8Kjf2mjEG0j9tFjRvUa6oF5Aw68QS7dkG/gqQ1leJfmFJdU+FOwVWePX0VennOIdeC0cxotVXpnE+YFo8wz+i6PRwiZRDtRUJ27tJtIrld0kzQB3VKHwtpgukJ2tK4Vl9+A6x4BInGjin4gRC8nsP2Lr6IxRldVUWb5IsMpGqdGOJwNHZD29uLvgcVbTYWOvmhJW1erbnF22L+na7Pc10KLYr8jIq8vRjsWGhWSrgApegC0BghC11WQaDLIgdk1QhPa0BjU5gBvFlh7uOdeNcSFAxHiDA6cF57rM638czXwLSk2bSUEnL9QVnuoH5YroPR7mU86L3SujFK06LMNcjawui3PDstJANuvFevz49932o4NdfF2rQLkS/mGtZVY96m2/BuIj8myy5cFfZdLr6VYHCRHaYh9QjBKecR9lzuYgrUxzHELvGcgPoE0MZCM6H0VWp8KDS+rxltEnjpLWEQ+4djPDUv5TvVIEcTqZeyyEpZ4V4WckWzv4riwSC1Hesr0Oq/kSvQL+83pZl6aSnNYbfijagug+eEphO2mHMti7wzQwfML+gPI70Vq6p1BH/Ye5C224GcwQJyTuXTB8RaAd8p+LMoG/1WsZYcKUkPjQQm8dpum/ZLajvt8ELknayJbbLuV4JEe5XouviZMgwY3ym4lWLdT7aWvCnZ0CemdrXlr0q35+TtCTfm5tlPUg1ySsvFUs/Eyox7pz7Y7QfVZlfKep8wPzrDbmCiuaEf7y9Ir+w6Zitre3n7AG/gWykk8LmmujQrh1n7DrzKI6/srQ+npzjI+WPk/mlydrUGCXecWW9gxi+iwHvvvNP8SUgdL/QY9kPbxV7gJ5gSSHEQRCF3GXgOeGjzT9v9m0+D+xdBPQDun1F/EvhBcHgSXEuhVlQLNppmpdIge6PLea1CO7vnM6oVJiszgLdqA2/D8+Pdjc3tb+KRcVzKlLJmeW2S8c1Ue3VR9TcW+t98NG13iHdvDsbYSKq1pipod5doNgjzsxZv1YYI3CFbagcOiVIXO0HiY4+FBEece5g4TuQkSRrbPEUba59M3uEErhIluKD56KJUCrIPp9K7uTPMwU8qfQyVWOwmnhPFmMWpiz3bBhynlGOfM+6nEYuAxINUcl9JgD4+jaYFZJTRXC8/qfR7UCnisevEIcORH0XYAz/GlCch9pPEScGzqe06g1R65WPnLKNKjeaszNa6+JxvvwmTCIEgDn0bM5cy7FES4wSSFEd2SpzADcFLSbVM1bhNiCd/j/BoRuUCRjfiEeToBlRRSgaq/50OdsJZGLs4CNMAe3bk4NhNbGyHjFNCEk6CBG3+A5Xe9QswFgAA",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [996] - Precision Lens Development Support
        [996] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XTW/bOBD9KwLPYqAPSqJ8c900NeBkgyhFDsEeKHFkE5ZFl6KyyRb+7wUlK5b8lW2RBXrITR4O37wZPs7QP9C41nLCKl1N8jka/UCXJUsLGBcFGmlVg43M4kyUsFvk3dKUo5FHYxvdKiGV0C9o5NpoWl0+Z0XNge/Mxn/TYl1LmS0MWPPhma8GJ6Q2ulrfLxRUC1lwNHIdZ4B8HrrBiKPBDudNMpNFveoYENchb1DodsmigEz3Nrp9N+/tsFJxwYoTJXW9MIz3mJAhkyHRcSqfAI1yVlRdhC+iWly+QNXjGOxBBsEAMuzOhy0hWYhcf2KiSdEYqs6QaJYtKzQKthUP6SFuHzXeot4yLaDMoMcn3N8XDovtdVuV+BcmTLeqOSbBkB6AeXsn52/B7hesEGxZfWFPUhm8gaHLzreH9jvI5BMoNHJNzU5QIIOAXTk/ifkVWzV5j8t5Aarqgng2ehB6kRRs3UEdQfYjhxwkN4hENxsbXT5rxbZX2JzTvUz+YetpqWuhhSyvmCi70mPXRrNawTVUFZsDGiFko5uGI7qRJSC7RXhZAxqZuh3Bm8lK/zberYIKjjNEGJ1YbyM26zs+yRoyrVgxqZWCUr9Tlnuo75brUbYHGR+N3ni1+km0XJvbLcp5omHdtNwd963Gxup9KPfhGg7fSvG9BoOLAjdjHIIQh2meYkJpiGlAchyRNCZO7INDGdrYaCYq/VduYlRo9NjK0yTw2gqiMCanOSYaioIpK2HFSpbWnQQDeiPVihVfpVwamK67PABrfht7Bfr1OjWdsbte20WTXnfRtqa2BsSNTNfqMBOtZNkbjUe2X4vSWO/FqukR5CKw0TV77tm84MI5COP4vTAzmEPJmXp5B/4N8GdZp8V+RVoPL4xfHXbpnXQ5Rq3vda/E+lSkKPD8V5dTsQZOZ6Jt/cwNGOca1ITV84WeiZUZVG67sH81mudLrdpJaD56Pf5ouw2MGA9n78mJbt4eXZPq9HgH32uhgCea6dpMS/O4OSHSN0T3q5r50MCvaeDUoZ99PG4GjdDLUsqJx3EURAQTn8SYOiTF4GY5iXnII4egzd9dJ9w+gB9fDW0zfPyBhl0xIPGZrii5WEGprZnIlqAGTdw9V58ph1KLjBWmKCZY6zBeybocuKERCeL9l4c/fDNSE6lWOcugfcZsuQdx8MZ7LNjY6I/5J7Abn789NM1mY5mYMjYV7I/R7fA0n61553ZMrz1tMTejKXdS7HOfYEJ9H1POAOecR5SnlIQQo419oB1zTme0w3AhVmAlWgFb/Q/yOdJZP9T0jmoaW9Opha2vL2tQmIFegJKZLHmdafEE1jXToAQrqv8sOscjA905jEIWehTHLskxyV0XM/AcTFjEUxLnce6Zd9hhzwpPp/qt1EIXwK0HqZbWjbywYvrnNK7jkv1Q3u279LHIiyIW8AwzHjiYAE9xSiDCmZuGAafMj4nXzMgWd0txbGHrVkEmKiFLawZlZX2GJyjkupl9Sb1eS6WHf0t4HDOapSEmgWMihTlmKaXYDVLX83mWUnDR5iefFn1k+RIAAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [998] - EX: Large Aquatic Resources
        [998] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XS2/jNhD+KwbP0lYv6nXzutk0hZMNLBdbIOiBkkY2EVl0SCobN/B/L6iHLdmSXQQ59NCclOHMNx+H8/I7mpaSzYiQYpatUPiObgoS5zDNcxRKXoKG1OGcFnA8TNujuxSFlh9o6JFTxqncodDU0J24eUvyMoX0KFb6+xrrnrFkrcCqD0t9VTiur6Hb7XLNQaxZnqLQNIwe8mXoCiPwehbGVTKzdblpGTim4Vyh0FqxPIdEdgzNrpp13S3jKSX5SEhNy3V7QXUas29UrG92IDqO8QljjHuM3Tbo5BmiNc3kV0Ir3kogWkEkSfIsUIibMLr+OW4XNWhQH4mkUCTQ4eOe2rn9CFqtKad/w4zIOhWG8sr1z8Csk+ewG7DlmuSUPItv5JVxhdcTtLeztb58AQl7BY5CU8VshILTc9iG8ytd3ZJNde9pscqBi9aJevsUhbZnOGfse1D+fq+hmzfJSVN46iGWLPpJtneFLKmkrLgltGhjq5sampcc7kEIsgIUIqShh4oEemAFIK1G2G0BhSowA3hzJuSH8R45CBhmiHQ0cl57rM6PfKItJJKTfFZyDoX8pFueoH7aXQfZnt140HulVSdIJNlWlS8tVpGEbdUoj9ybJJryz6Hchas4/FHQlxIULsKOFdgeSfQ4SDzdMTzQSUqITrCLDddIPc8z0V5Dcyrk90z5ECh8qtNTXeBQ654b2OMcF5BOZkxs2IasVkwKBfnA+IbkvzH2rEDa5vEDSPW/kguQhzrMSC6grcvmUF2urdBGVEfAMT3VlFrMSHJWdMbZiPmSboCfFP49eTscodD0vhga+l7kux9rKB6YnCaSvkKUj/Iw7A6POaygSAnfXaRyT4uOS8v94gTdP3vYx6+sjPPT6NUalhscFI6hGFXpsRzQWnK6HfPkYcs+qIz56ild8NboqVqZZhL4jJSrtZzTjZpZZn1wWkTVelLyeiiqj067n5EigXwqJWy2so220lkSvoIac6Dv2x4OzjeBC0NdrR9tx2vTewEvJeWQRpLIUs1Wtd+M5PyVHP7XGfZ/mnwoTT765p2umgTYII6T6Y4ZY91xfVuPsZfpVmwaJmSp7Qc+2v/VttVmB346COrO+vSO+i0WY2u8xSrV3eR3yPNdpqy6E8G8FJ+7FApJE5KroChntcJ0w8qip4ZCBwena4zd3zB95ankGUmarji4zTk4wFeWObzX0H/mt8FxNH94IEc/yVZJZiqqVUC7IxqF6ObPiT5ZEA6T6UtJJE0mCxCs5AmIifkLRjVEbXQEGUrubiK6sW9mONED7Ji6Y6S+TiBOdcMLMteJ48xI3SoRa9zmAhWVuWqK51z660NmBU4cZJZu+ODrTux6emzGth67ZurGdoaTJEH7fwA1PiZwVw4AAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },

        // - - - - - - - - - - 
        // Weather-Restricted Rank
        // - - - - - - - - - - 

        // Export for Mission [993] - EX: Capsule Pools Distribution Survey [10/21]
        [993] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1X32+jOBD+VyI/YwmDAZO3bLbtVcp2q01PPam6BwNDsEpw1pju5qr87yfzI4H8lKo8nHR9I+Px52/Gn2cm72hSaTnlpS6n6QKN39FNwaMcJnmOxlpVYCGzOBMF7BaTbuk+QWOHhRZ6VEIqoddoTCx0X978jvMqgWRnNv6bBuublHFmwOoPx3zVOD6z0N3qKVNQZjJP0JjY9gD5PHSNEQaDHfZFMtOsWnYMKLHpBQrdLpnnEOsTGaHEJv1dzmUWUiWC5yfwiOP7gxzTdtutKLObNZS9ALy9ADxvEIDf3QF/hXkmUv2FizoMYyg7w1zz+LVEY6/Nqs8OcfuoYYv6yLWAIoYeH39/nz9MqNNtVeIfmHLdKOOYzHx2AObs3Y7bgj1lPBf8tbzlb1IZvIGhi861hvYfEMs3UGhMTM5OUKCDA7t0fhGLO76s454UixxU2R1i7j5BYzew6QH7ARTbbCx081sr3r5DcxFPcv6Lr+4LXQktZHHHRdHlFhMLzSoF36As+QLQGCELPdQk0IMsAFkNwnoFaGwScwRvJkv9YbxHBSUcZ4gwOrHenFiv7/jMVxBrxfNppRQU+kpR7qFeLdajbA8iPnp67dUIZK7lyjxfUSzmGlZ13dxxb0U0Udeh3IerOfxZiJ8VGFxEOUkDFno4jliAKY8dzDh3MQFOIxL6lHAfbSw0E6X+npozSjR+aeRpAti+9cAPvdMcb43sf3ENajRROlMyrxQY3Aepljz/Q8pXg9RVkGfg9W9jL0FvH2PK8xK6x9kumgi7Z9qamjRQEpjK1GHOtZJFr8Vd3u4ErLd/BgsoEq7WVyDWIH+VVZTvx9q4OH64ddgRP+lyjFvf60mJ1amTAs9xty6nzho4nTmt9TPynqQa1JRXi0zPxNK0GdIs7Ou+HjAq1fQx89Gr0N+LfP2cQfEg9STW4g3uEyi0iE2zbDJ7pFC7gRcedvIzXdiMD12J6qT4A35WQkEy11xXphma+eSEPi/obc/Ldi+oauD4KZKLIvnonffKoO07LktDF6eO42KauAEOY8/GSeB7rs9C7sQx2vzd1cF2hn3ZGppS+PKO9moicU/XxClflVUOo8kyAjUo4ORcdrYvwKTEHNU4TJayKnpuxwZTL9wfQtzhfGiq0rxSKY9hnpvq1Qbimdp+dvbyNhb6z0z2u0764f5pNhvL1GS1Tmi/o7Z91Hw25p3bMfH2hMaSgAZh7GObBhRTjwLmLnGxS/w48GnqJsxDG+tQSPaZANaxFnGms3V5dRl9WDfH9fcpo+vIKApZEjA7xLEbcUzdiOGIeSFmCbWZT4IU7PSojJzL9cgIQqbpZ0X6f0iJxzQmESc4ClIbU89nOLJdHzMScT90aEpSWre+Brel+AxcZ6BGN3+N8KhTzqOUeTn6KkqtRFSZaWo0r9QbrIf/OQLiRaHLAsyCyNRA5uGQEYKD2HGcxPGoE6Vo8y9+YvlkmxIAAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [995] - EX+: Stardust Bait Test
        [995] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1X227jNhD9FYOvNQvqLvrNcZM0gJNdxF6kQLAoKGlksZFFL0Vl4w387wV1sSX5FgR5KIq8yeTwzJnh4cz4FY0LJSYsV/kkXqDRK7rMWJDCOE3RSMkChkhvTnkGu82o2bqJ0Mj06RBdr+aJhDwRaYRGxhB9lVxIrtblj5v88iVMiwii3bI+v6mwb4UIEw1efpj6q8R1/T4uIR3k09AlBvU6J8hZMpOkWDYMbIPYZyg0p0SaQqiOZMg2SCcl5nkWQkacpUfwDNN1+zl3u8S6vMeBeAY0ilmaNx6ueJ5criFvxer0IB2nA+k218WeYJbwWF0wXkasF/JmYaZY+JSjkVNfgOvv47ZRaY36lSkOWQgtPm7/nNvNvdkclfwXTJiqRPQthy9Zun7gKrmJIFM8ZKm2ahLY2h+Hij/DLGWrZvOQvF1/j4nZU4FVM5knLOXsKb9iz0JqMp2FJjXWsLt+D6F4BolGhk74EQp2x6FfO7zgi2u2LJM2zhYpyLxxYg5RNwUnQrQ8Yu+F2PHnbTZDdPmiJOsUiS2Kvvm5mP1kq5tMFVxxkV0znjWXiY0hmhYSbiHP2QLQCKEhuiuJozuRAaoR1itAI53MA3hToe/wnXhfJeRwmCHC6Mh+5bHc3/GZrSBUkqWTQkrI1AdF2UP9sFgPst2L+KD30upeG01EkSmQ1Qlt39x6JbmZEitdTXi2mClYlRV/F1kty7H8mIDacCXDbxn/UYDGRY5txy6xXRw6BsF2QAmmoRNjg9DYjqkZWUGENkM05bn6EmsfORo9vpbedADb0uO51DzO8SItYDBTTEZFrjTenZBLlv4pxJNGaArZA7Dyt17PQW0fTFmEm2dYb7aTWi9V4duGpwtkgzlTUmSt93fk+JwvQfZe6C3Ptlu6gPxOhuiWvbTWTE+vlbUxgWy/NvaIEatFbAoLyCIm12e59RH+EEWQ9pNVWZgu3RrsIj9q0uFwwGou+eqYJ88xra3JMV8doxPeajv9LsaxAjlhxSJRU77U7dKoNvoPppyxCln1Y/3RahbNldwJVd3KtqifrOkO3R9eTgweemJqKl+j5Xv4UXAJ0UwxVeimrkeyIwI/I9g3q+dTJO8SyXvvvFU/qeG5tu1ZmEVugG0v8jDzIoIji1iWa8Qkjny0+d4U0Hpsf9wuVDX08RX1iql9opheSaGSWNu3q75xKjO9qe4VVQbjpW5TO7NDc7hD+2OO1Z1x9Vw1K2TMwrr01UE41DkzAjqbIfrP/JHZtd93N119WK+Uzb9S5E+2qlpx3iS13ZnRCLFMZH8/Uupg4/vgAZhKQA4u//ptgLf9cqAxB3PI1eBWDyNt2JarA+JvCTUOmAsuGNi2LB/bMYsxNWwD+65FzQiYGcQW2gz3hWgdT8JMRAwHurXfLFeQh0rIT0l+SvKtkmQGI8QyTBw4LsM2JSEOTOpj07H9yPGpz5z4oCRPJGG8BJWsNUH+jyg+XI2f8vv/yC9wY8cnHsE28y1sM2Jhaun+7RrEc0xCrCgoW3eFW4dZEjPfQKy8+Y5DO3Q96scxNn3f1A4ppiFQ7BoOBeIz6kce2vwLlC7AMF0UAAA=",
                "AH4_H4sIAAAAAAAACu1XW0/rOBD+K5Vft0Zx7s5b6QKLBBxEilgJHa2cZNJapHGP47BwUP/7yrm0TXo7qlhpV+KtHc98c/HnmckHGpVKjFmhinE6RcEHushZlMEoy1CgZAlDpA9veA7rw6Q9uk5QYPp0iK4Wk5mEYiayBAVkiO4lF5Kr9+rPdXHxFmdlAslarO2XNfatEPFMg1c/TP2rwnX9Pq5hdJAPQ1cY1OtYGEeDGc/KeRuBTQz7SAitlcgyiNWeCtnE6JTEPB6FkAln2R48Yrpuv+ZuN7Bu3KNIvAIKUpYVrYdLXswu3qHYyNXpQTpOB9Jtr4u9QDjjqTpnvMpYC4pWECoWvxQocJoLcP1t3E1U2qDeM8Uhj2EjHrdv53Zrb7amkv+EMVM1iR4L+JZn709cza4TyBWPWaa12gLuYrDrbzkzexdtNc4mM5Zx9lJcslchtb+OoM3eGnblDxCLV5AoILqme0KwOw79xuE5n16xeVWXUT7NQBatE3OIulkeSNHyDHsrxY4/b7kcoos3JVmnD6xQ9OVORPg3W1znquSKi/yK8by9L0yG6KaUcAtFwaaAAoSG6K4KHN2JHFCD8L4AFOhi7sC7EfqaTsS7l1DA7ggRRnvOa4/V+TqecAGxkiwbl1JCrj4pyx7qp+W6M9qtjHd6r7QetNJYlLkCWVto/fbWa8qFSix0w+D5NFSwqJr6OrOGliP5OQltwlURPub8RwkaF4Hr+L4FFnZjQrDtGT6OPLAws9LUphGzWeSg5RDd8EJ9S7WPAgXPH5U3ncCqu3guNffHeJ6VMAgVk0lZKI13J+ScZX8I8aIR2l71BKz6r+UFqNWDqfps+wybw82iNqI6fZt4uge2mKGSIt94f3vMJ3wOsvdCb3m+OkKBd+YM0S172xCZ3pmx5d2wNrzfwBTyhMn3owH0EX4XZZT1K1JrmC5dKazT26vSiWGH1kTyxT5PnmNaK5V9vjpKB7w1epr8o1SBHLNyOlM3fK7HHqkP+q+i2pVKWc9V/WNjIlRjaQb5nVCjWPFXWHXug43bodtLyIEFQm8+bXtrCfsAP0ouIQkVU6Ueznq12sPiI6z8ZfZ8keQkkpx65xtNMrUd6nsswimkNrYd08LMYRZ2Ypf6kUut2HXQ8nvbJZv1+3klqBvl8wfqdUz7QMe8lELNUq2/2drJocr0trMPVCuM5noWrdV27dMO7e8yVndX1ctTWMqUxRBmunE1STjUObLnOcsh+s98kKxn7MmTVRtrSTXhm0+z9axFAbr487cBXg27gdYdTKBQg6pwNUBtsobYReoNAvqRFfm+E+OU2RG2iRFjSlwTW2BRylyH2GaElsNtgln7kwtFwnCk5/L1fAFFrIT8otr/jGr4NDp5CSNe4nrYjkyG7TQ2MAUg2DAM3yImc4jn76KTRfcn8JgrrjJIBk9CvgzuxNmA+p/Op5MJtJuIX3xak6doa9OjGMtF/tczpQ42vw+egKkZyMG/0OJii6au7xmYJJRg20gMHJkexSx2LWYQm5nEqmZsjdukWQVGfiGwW/0V13WYeDSGiOAo0Y8gZhZmphVh2/UBYtuxgTho+Q+gLBnhzhMAAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },

        // - - - - - - - - - - 
        // Time-Restricted Rank
        // - - - - - - - - - - 

        // Export for Mission [994] - EX+: Large Saltpeter Shore Resources
        [994] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1X227bOBD9FYOvKwa6ULSkN9dJswGcNIiT7QLFPoylkc1GEV2KSpsG/vcFdYkvkR0lSLH7UD/JI/LMmeHhzOiRjEotx1DoYpzOSfRITnKYZTjKMhJpVaJFjmWux5DHmJ1LGS9IlEJWoEXMponIcb0pabecJSRyg3D/3kslpBL6gUSORc6Kkx9xViaYrM0GZ1X7aHY+kurBNU8VPg8scrq8XigsFjJLSOTY9hbyYegKIxz24mi/SHK8KO/2JII5NtthOjRMe/htwWWWYazbyJljO322uy+zlioRkO0h7richzvMeeD3cb0d8Ggm77FZ1Xj+KIrFyQMWGzH5O658v1eSeCsTuMXpQqT6A4gqVcZQtIaphvi2IJHfHDwPnvvr4y1svF2CFpjHuMGf7+LxfofstpBK/MQx6FrsNwV+yrOHz0IvzhLMtYghM6vaA+q6cTx4RsLtqTSvIXG9gEzAbfER7qUyPLYMbRY9a9t+hbG8R0Uix5zZHmqsF5H2OD+I+SncVfkd5fMMVdE6N7JOSOQNbfYs2l4ugtXKIic/tIKm5BmBXMvpd1ie5boUWsj8FETeni11LDIpFZ5jUcAcSUSIRS4qcuRC5kisGuFhiSQyiezAm0hzdG/Eu1RYYDdDQsme97XH6v2az3SJsVaQjUulMNfvFOUO6rvF2sn2WcSd3qtVtXCmWi5NuRH5fKpxWbWcNfdGXCP1PpQ34SoON7n4VqLBJb7jpuAnCY1DP6XMS0M6sznQIcSB77IZInKysshEFPpTanwUJPpSy9ME8FRrhtz0rX0cj8sc0zrewUjphZJZqdAAX0h1B9mfUt4aqLaEfUao/ht7gfrp9jYXp/7fvDQhtve6MdV5YM7QlMYWc6qVzDfGiY7t5/DDWK/FnSkcrnNkP4O0vQ3ICc4xT0A9vAPXCvimwGNZNuvXw46xvJCSLTSXm8Drfeuwn0LbKYbnIt+I2uFHzCJmduhaa+w76+0jFm7+ggNk+iSsY/NNgddKLLfTUltelZah75os1zt/eWL4kbeVF3aQzutT02w3tWSUalRjKOcLPRF3ZtZw6he7RaYakEtVDznmYaN9Vr19gfmF1KNYi3t8avIHGrw39MPdUdI03L1Tnhlz237QXvsr/FYKhclUgy7NRGTm6D214IW7/drrurWw86a9dKV6C31zVad4X1bpK7Tzi0Sy78wPfo+stnqOl7AEXJfRGXo2ZXEQ05CDR7k9c/gMQ8fxhmT1T9t0mpnpy5Oh7jtfHslOA/L8/Q3oCrMy1pDrwSUsUX1HMV/ora7pHMrSzrj7SOoFoztZ5lvLSMT8cHcE9MyFWKckMJ5KlUKM08z0g+5PND/0uwbnjaHRX1nkV3+J9v7iXI8vbx5azGZjGZusVgndHGNIRExlTQYnf/8xoIMJqDkOppDpJWpUg+lCKhxcYSFLFWMx+AuUMOftkBq4hlpDdwl/U6QzL7XBS6iX8CFlPB3SWeg4FPw0TRyeQug5ZGU9FyHfH/RpBoUh9lXBV/j+/1Hfb7l1y42+TTocbM9FN6Qc3ZSyGdg0dNyQxuAGnuuFSRywTukcGKDXOr+SBb67dLrrz28l/cdKCj035QEENLH9kDIAlwbMHVKWpHESBMxN3aDqlDVuQ/E1VfLcfL9u+UyDEBzm+dT3YkYZTxI6A9Ongxhse8YZA4+s/gX/CdRvpBUAAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [1001] - Lower Soda-lime Float Distribution Survey [10/21]
        [1001] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XyW7jOBD9FYNnMdC+3Rx3kgngpINWBjkEc6DIkk1EFt0UlY4n8L8PqMWWbNlpBDnMoW9ysfjqVbE2v6NppcSMlKqcZQsUv6OrgqQ5TPMcxUpWYCB9OOcF7A9Zd3TLUGyHkYEeJBeSqw2KLQPdlldvNK8YsL1Y628brDsh6FKD1R+2/qpx/NBAN+vHpYRyKXKGYss0B8jnoWuMKBjcMD8kM1tWqxOOuZbpHjDyvCGjDkTkOVB1Gsfq37I/JiUk4yQ/gWfZvh8dEPOHoRrynqbiFVCckbzsLFzzcnm1gbILvmuZ3nlf/e71yAskS56pS8Jrj7Wg7ASJIvSlRLHXvocfHuP2UaMW9YEoDgWFHh//8J4/jL3dXZX8X5gR1eTUWIL64RGYfZBaTgv2uCQ5Jy/lNXkVUuMNBJ13jjGU/wAqXkGi2NIxO0HBHRjswnnJFzdkVfs9LRY5yLIzYhvoiavlLYNCcUryWQc4gu8Epnvk4sBeuN0a6OpNSdKWuX6tR5H8IuvbQlVccVHcEF50D4AtA80rCXdQlmQBKEbIQPc1U3QvCkBGg7BZA4p19Ebw5qJUn8Z7kFDCOEOE0YnzxmJ9vueTrIEqSfJZJSUU6ou8PED9Ml9H2R55PGq91mqyKFFirWucF4tEwbpuy3vubaZN5ddQ7sPVHP4u+M8KNC4KnMgJAi/DhFoWdoPIwsQJPByEGTAIfUJThrYGmvNSfc+0jRLFz016agd2DSHwI/s0x8u8gkmiiGRVqTTevZArkv8lxItG6NrLE5D6d1NC+rQEpV3oikmLHvkK5EGR3fFid4Riy70wDXRH3noyu5bpcTJ2X8uHGOGF24oHMJar5d+LfPO0hOJeqClV/BWSfMhRx75+C9cKdA/tHEyUFMXiQxd7102nd30OCygYkZuzCENHbOtcMA4CZ1/4p70eY/ZNVGm+e7eBhu1HO4W93ydVBr6NaD1Kvj5lKfBsZ6dyytZA6Yy1Vk+X6DRTIGekWizVnK/0PLWag8ParXewSjYDW3/0RtFRsuxGxtmJ4UWH283ZdUlvWF2b7crqB/ysuASWKKIqPfX1CndYa7+Xrr+dlX+S5FNJ8tk377XyMKXMzcDBaWAH2KUOwWkEAQbq+5bJIHJtD23/6Xp5u+Y/7wRNO39+R8O+7oXu6b5+I/lqcrtaQ0mVkIMpZJ0Lz35rIs1cbhSmK1EVPbWxbd2LDjcpZ7gJh9pwJTNC27bceuJF3gdbprc10P/m389+Hfj0EqAva8lMR7UOaH8taJcB/dmI92pj2dvLNEYjNyVhhH0CDLtu6GBCmY190zdtCBkzwUdb4ziTzjlAXjeTmYCcUFKo5Zen0qdzZzwH/6TSw5ekkk1Ml5pOisOUZti1PRNHWUpwxlIfWEYjM4zqptXgthT1WsImeDIXv0BOEsEIzvkKJte5IGryjZdK8rTSo3CSVPIVNsOlNwwoc7wwwpSRELtW5uMotSmGlFrMzNLUCT20/Q+O+sPWexEAAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [1004] - EX+: Elemental-esque Saltpeter Shore Specimens
        [1004] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XS2/bOBD+Kwavay30oJ431+tmAzhpEKXoAkEPlDiyidCiQ1Jps4H/+4J62JIf8cLIoYfeZHL4fd8MhzPjNzSptJgSpdW0WKDkDc1KknGYcI4SLSsYI7M5ZyXsNmm3dU1R4kbxGN1JJiTTryhxxuhazX7mvKJAd8vGftNg3QiRLw1Y/eGarxoniMboav2wlKCWglOUOLY9QH4fusaIw8EJ+6yY6bJadQqwY+MzErpTgnPIde+g0zdzz9MKSRnhJ0LquEEwCCpuj31majl7BdUj9vcU+/5AcdAFnTxBumSF/kRYrdssqG4h1SR/Uijx2zAG0SFuHzVuUe+IZlDm0NMT7J8LhhF0u6OS/QtToptU+KrgS8lfvzG9nOSavUDKyboLybGkC6IDJnfvrryW6WFJOCNP6jN5EdKQDRY6173xcP0ecvECEiWOCegJCXhA2MX6E1tckVUdlEm54CBVR2ISg6LEC218oH4AFW02YzT7qSUZvMqtAHNpDyL9QdbXpa6YZqK8Iqzs7sFyxmheSbgBpcgCUILQGN3WmtCtKAG1CK9rQImJ0xG8uVD6Yrw7CQqOK0QWOrHfMNb7Oz3pGnItCZ9WUkKpP8jLPdQP8/Wo2gOPj7LXVvfGaCqqUoNsThj77tabbEq1WJtCwMpFqmFdl9ydZ23GTeTHONSHqxV+LdlzBQYXeTgKIMau5cQ4sjDNPSvKgtyK3YDYHlAbvAJtxmjOlP5SGA6Fkse3ms04sK0aYRBHpzVeCU5HqSaSVkobvFshV4T/LcSTQehq0Dcg9W+zrkBvH0xBuNoWkXazH9R2qXEfO6GpbR1mqqUoF5eiPrAVyL2He8PK7RZKHPynfaDA9noK5rCAkhL5uisC532oEf4SVcb3o9JYuEG8NThw8dBkoOGI1YNk61NMoe96W5NTXAOjd9haO/MAJoUGOSXVYqnnbGVamtNs7L+MenqpZNMzzUev4B+p6l7ox4dDwDv93EweXQHrUvIenismgaaa6Mq0VTPanMjT/5d353PjdwpclAKX3nmvDPpBETkZjqw4Cx0LBzSzssglVhTlEEaxjb2Aos33rg624+/jdqEphY9vaK8mhu/U7ZSUdMGJUqOUE7UEOSjiznsRuqZQapYTbsJi6BqDycp0nb4ZSrAf748p3nC8jAxTJQuStxPb0XkW+7F/ZljzN2P0y/wx2HXTi3uoOWxW6l7eZOYPsm46q+pi02+0KEGmKdDR7J8/RtZoxmEFpSbcAvVcwSglXK9BgxylSyFhZEYItoJSjR5N4L+jPkGP9Mhz6KUu9uwC24Vt5STAFo4cx4og8KwsinFGCzvIw7xO3Qa3dfhCnTeElUZnj59GeRZiQq2I2o6FXTe3stihVgwEbC/0cRFmaPMf7Vq59aQOAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },


        // - - - - - - - - - - 
        // Sequence Rank
        // - - - - - - - - - - 

        // Export for Mission [997] - Hyper-aetheroconductive Materials
        [997] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1X207jSBD9laif3ciX9i1vIcswWSUMIrA8oNWq7S4nLRx3pt0GMij/vmpfiB0cMiBGWmknT0511amLT1WXn9GoUGJMc5WPkwUaPqOzjEYpjNIUDZUswED6cMoz2B2y5mjC0NAOQgNdSi4kVxs0tAw0yc+e4rRgwHZirb+tsGZCxEsNVj7Y+qnE8QIDna+vlxLypUgZGlqm2UF+G7rECP2OhXk0mPGyWDUREMskR0JorESaQqxahlZbzT7uVkjGaXqgpJbteZ2iktrsC8+XZxvIW47dvYhdtxOx1xSd3sN8yRN1SnkZtxbkjWCuaHyfo6Fbl9ELXuO2UcMa9ZIqDlkMrXi8fTuvW0G7MZX8B4ypqqjQeN23tvfq79TW10uacnqff6EPQmqAjqBJxzG68iuIxQNINLR0kfq47AWaAi2HTf1O+eKcrspER9kiBZk3TvTLZmjo+CZ5FX0HKthuDXT2pCTtdNpLAPpFXIv5I11PMlVwxUV2TnnWlAdbBpoWEmaQ53QBaIiQgS7KmNCFyADVCJs1oKGuUw/eVOTqw3iXEnLojxBhdOC88lie7+KZryFWkqbjQkrI1CdluYf6abn2Rvsq417vpdaVVhqLIlMgKwut37z1ik1zJda6uXm2mCtYl2N0l1nNuJH8nITacGWENxn/XoDGRabpxaHrRTg0WYSJ6wAOw9jEdhInQUiD2CQm2hpoynP1LdE+cjS8ey696QReJoHvhfbhGE/TAgZzRSUrcqXxLoRc0fSrEPcaoZkrt0DL/1qeg3ppmISmOTQdXB+2i1qLqvSJ5et51WDOlRRZq/8OmF/zFci9Dp3x7OVIz4YT00Az+tSS2faJY7Z/5FUwptMKZgoLyBiVm0/IsgS+yeEPUdT6jWIlOVLMDprt6ZJVdruCHaxLpwZVXb5l6eZ2CdkoVvwB5umBsNuOfqYYPcY3OVxLvu6mXEnelbLv2rqCleWvTbrj6v1p1+Z6aIwSBXJMi8VSTflKX+1WdbA/TcotrpDV7qAfWpdkz03o+G74ehl6Y6/RG1gz9Js2voLvBZfA5oqqQq8XesU70NtHevW9TdRR7OX/MaL/NEXbWr20O86vdzDjF1Hgo++8dXWA50XMBQ9HJDQxiVwTh0EUYN9yacKsKA6Ih7Z/N3dH/Rlw9yKoro+7Z9S9R1z/jXvkJlNcpcAGt0LeDy7EycD33M7lZ71VpQmDTPGYpro02mWlMFrp23qn1tMfxA33tz2nu3kH2nEhExrXg6BOyA3dI0uuuzXQf+YjabeFfHj30MZaUu5AFTsf6braSPKmqO0FBQ0RzUT2z2gwmQzw4OtmDRJTUEuQIhYZK8rxOphRBZLTNB/M9E7Whm256mmEFmltQi2bmSE2LdPChHkBpoQCtihxnIj6ceIztDVay3o9kbskJYF3uChadTP4E9J0k2irz6bnh/nYz+vf9NzjIv4YtTxCbIc5CQ58RjCBJMRBZNmYgBv5fkBN0/JLau3PuzcSGK3XKURSPP4m0f9txv1FJaeZGlgfYyMLIKZh7OLYD2JMXDvGgRn6OGTUpn7C4siLytu5wm349t7o7O7nZOAmkR+7JrbCKMLE1p+TJAkwgOdGIQtYZFlo+y+H0PtBhRQAAA==",
                "AH4_H4sIAAAAAAAACu1X227bOBD9FYPPYiBRJCX5zfWmbRZJWsTJ9qFYLChxbBNRRJei2rqB/31BXWLJseMmSIEFtn6SyZkzF5256B5NKqunorTldL5A43t0Wog0h0meo7E1FXjIXZ6rAraXsrs6k2hM4sRDH43SRtk1GgceOitPv2d5JUFuj538psG60DpbOrD6gbinGofHHnq3ul4aKJc6l2gc+P4A+WnoGiOJBhr+UWemy+qu84AGPj3iQqel8xwy21MM+mLkuFltpBL5gZQGhPNBUmmr9laVy9M1lD3DbMdjxgYe8y7p4hZmSzW3b4Sq/XYHZXcwsyK7LdGYtWnk8WPcPmrSon4UVkGRQc8fvqvHhxkknapRP2AqbEOFzuquNtnJf9hqXy9FrsRt+VZ81cYBDA66cEJveH4Fmf4KBo0Dl6R9XOaxo0DPYJe/N2rxTtzVgU6KRQ6m7Iy4ly3ROIx8+sj7AVS82Xjo9Ls1YlBpDw64F3GtZ9/E6qywlbJKF++EKrr04MBD55WBCyhLsQA0RshDl7VP6FIXgFqE9QrQ2OVpD965Lu2L8T4aKGG/hwijA/eNxfp+689sBZk1Ip9WxkBhXynKHdRXi3Wvt48i3mu9lrpyQlNdFRZMo+Hku7fesGlm9coVtyoWMwuruo1uI2sZNzGvE1AfrvbwplBfKnC4iEeCUBYKzAjnmHIusZA8wwyCjIUsIIQKtPHQuSrth7mzUaLx5/vamgvgoRNEPCGHfXyTVzCaWWFkVVqHd6nNncjfa33rELq+8glE/d+dl2AfCmYu8hK6Cm4v+0ltj5rwaRC5ftVhzqzRRa/+DqhfqzswOxV6oYqHK9fKTiK//ws8dCG+9yQIOQkHEvSRa37Yc+0cFlBIYdavEHMNfFPCH7pq5TvB5uRIagdohLsENnrb9P1klviJ76EPRb7+tIRikln1FWb5Abf7hn4mGXuUb0q4Nmo1DLk5eVbIESMug43mrw16YOr5YbfqroVM5hbMVFSLpT1Xd27QB83Fbm+pd7rKNJuEe+iNzD1zMYxY8ng1emLLcftYNwK6or6CL5UyIGdW2MotG27hO1DpRyr3uUU0ENzL/2NE/2mK9qX20u44v57BjF9EgZe+894gmYu55L6MsaQiwdSPIpzMSYZTQklCUz8VbpD83U2S9qPg88NBM0w+36PhVGHRE1PlprDK5iBHn7S5HV3qk1HE2WAUBk9l6UxCYVUmcpcaZ7IRmNy52b0V21MflCW7u1843MNjZ7gyc5G1jaANiCXsyMrLNh76z3wybXeSF28iTtmd1BtRw85vYtXsJ2WX1P66gsZIFLr4ZzI6Oxvh0fv1CgwWYJdgdKYLWdXtdXQhLBgl8nJ04Ta0PmzP1J5C6JFWEOKDiGNMw8DHNAojnPAkwYxEASNxKIEA2ni91b3tyLskfSIpk9Uqh9Tob69OzBczcT+jfxNzh4X4ZaSCKEpZmlI8D5IAU+EHWPg8wiH1IY6jIJJRWJNqh0Quh4cCcKLr0Z+Q5+u50/pNpf9Zj/tLGCUKOyIv4ySlPM2Y8DGwIMA0Ax8nhFKcCWBpIv2Ep6yezg1u17qe610w/LhMMyIkEI79JJSYUilxkmQxjinnGWN+mLIUbf4FeTD5+5MUAAA=",
                "AH4_H4sIAAAAAAAACu1X227bOBD9FYPPYqEbKclvrjdts0jSok63D8ViMZJGNhFFdCmqrTfwvy+oiy05it0YWWCBrf0iD2fOXHRmOH4gs0rLOZS6nGdLMn0gFwXEOc7ynEy1qtAi5vBKFLg/TLujy5RM3TCyyAclpBJ6Q6aORS7Lix9JXqWY7sVGf9tgXUuZrAxY/eCapxqHhxZ5u75dKSxXMk/J1LHtAfJx6BojCgYW9slg5qvqvovAd2z/RAidlcxzTHTP0OmruafdSpUKyJ8oqeNyPiiq35q9EeXqYoNlzzE7iJixQcS8Kzrc4WIlMv0aRB23EZSdYKEhuSvJlLVl5OFj3D5q1KJ+AC2wSLAXDz+048MKup2pEn/jHHRDhc7robV7UH+vtb5dQS7grnwD36QyAANBl45nDeUfMZHfUJGpY4o0xmUeGgr0HHb1ey2Wb+G+TnRWLHNUZefEvOyUTL3A9h9FP4AKt1uLXPzQCgadtgvAvIhbufgO68tCV0ILWbwFUXTloY5FriqF11iWsEQyJcQiN3VM5EYWSFqEzRrJ1NRpBO9KlvpsvA8KSxyPkFDyxHnjsT7fx7NYY6IV5PNKKSz0C2V5gPpiuY5G+yjjUe+11kejNJdVoVE1Fka/e+tvpEqw7sFDYWqkdVcFPHKtlncLLddmDIhiudC4rgfuvgYtN2fqZVLvw9W5fCrE1woNLklSm3MGEc38NKF+EKU09nhEYwa+z1wnTDKPbC1yJUr9PjM+SjL98lB7MwnsZkaT3VMxvs4rnCw0qLQqtcG7keoe8ndS3hmEbgJ9Rqh/G3mJetdaGeQldr3eHvYr3Yqa9H0nMJOtw1xoJYtepz5hfivuUR308rUodkdm6L0K7P7Hscg1/OhpuO4rb6DhPwrN9nqhXeESixTU5gVyroE/lfibrFr9TrGRnCjtAM3lpoCN3b5874t883mFxSzR4hteplhokZjbbySePsLPZDlifKvE+plhB8z1dpZnBj7AeH7orbnp71mmUc2hWq70lbg397XTHBw2fr2aVapZCMxD7+Ybud68gEWPN5wjy4pZq7pJ3nXcR/xaCYXpQoOuzM5g9rYn2vBEWz2X4QPFUXIeY+GzaHaST8eJ80xm/EsUOPed96a8w30ndnlEs4xn1A9ZRCOwOXVjD4IkDIIMA7L9sxvz7W7/ZSdoJv2XBzIc+Sw4MvI/FVroHNPJZ6nuJjfy1STgbHBPOceqtGtTUxrjslGY3ZsreK820h8+iw5XOG+4TofGcaUySHCRm4naJsQidmJzZVuL/Gf++ewXhrPXBGNsJPViUxe0vzi064J5bMR7tTES9wjHgIfgc5u6LLOp7wScAmOMplmI4EGEISDZWo8JdSSB2XqdY6zk9xcn0dmsGWffLxLtGVN2tTngFRSy+Gs2ubyc0Mm7zRoVBdQrVDKRRVrVF/XkGjQqAXk5+QOUgEJPnPPYyF0eIHge9VwnpD7YLgU/9agNQWy+iOEoG83LeKoSRnUz+R3zfJMZq1+c/J9y0j1zQvpuApA41MY0oj53IxojR5qxIAljn0OYhfWV3OB2M/Bno7s2/28HDiENHWQeo4huTH0OHo1Dl9GY+RnEvpNx3yPbfwCInF74ShQAAA==",
            },
            AmountRequired = 3,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Untitled Work No. 765"] = new List<uint>()
                {
                    47572,
                },
            },
        },
        // Export for Mission [999] - EX: Rare Aquatic Resources
        [999] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACt1XyW7jOBD9FYNnKaN9uzmeJBPASQdWGj1A0BhQUskmQpMOSaWTDvzvA2qxJW+ZDnJojE9yserV41ORVXpD40rxCZZKTso5St7QBcMZhTGlKFGiAgPpxSlhsF0suqXrAiVOFBvoThAuiHpFiW2ga3nxktOqgGJr1v7rBuuG83yhweoHRz/VOEFkoKvV/UKAXHBaoMS2rAHyaegaIw4HEda7ZCaLatkx8GzLe4dCF8UphVz1Au2+m/N+Wi4KgukRSW0nCAaiem3YJZGLi1eQvcT+DmPfHzAOOtHxI6QLUqpzTGre2iA7Q6pw/ihR4rcyBtE+bh81blHvsCLAcujxCXbjgqGCThcqyE+YYNWUQpd1N9rZ0d9to+8XmBL8KC/xMxcaYGDotuMaQ/sMcv4MAiW2FulQLQeRLoFewk6/czK/wst6o2M2pyBkl0S/7AIlbmh5e+wHUNF6baCLFyXw4KRtCOgXcc/TH3h1zVRFFOHsChPWyWPaBppWAm5ASjwHlCBkoNuaE7rlDFCL8LoClGidDuBNuVQfxrsTIOEwQ3Tx98gczbCA0fipworkoxlIXokc5Mj/w0cG0lENwg6bZvMHwRuHk+BuDb7darqCXAlMJ5UQwNQnCbiD+mkyHmSLTHTSq1VFe82004RXTIFoIrR/p2lTqKniK31vEDZPFazqG3q7s7aYx+JzNtSHqxl+ZeSpAo2LQivzMhcXZh7mrumBV5qx5XimZfteEQZR6Wc2WhtoSqT6UuocEiUPb3U2vYHNJRMGsXecY6qAUixGKaZLzkYzDhr0loslpn9x/qhhunvrG+D6v7ZLUJsDWWIqobsh2sW+sq2p0cCzQ30fdpipEpzNP4J6Q5i23pNlc0edWXuZLLeXaQpzYAUWr5+QzD3z4v4vMtANful7RGe+1f+Fh8n9yauM7grbeDhBvHHYU2nf5dD2+l73gqyOZQp9x924HMs1cDqRrfMjS+CVusEvh9XQB21cKhATXM0XakqWutPazcLuCayHqko0rVw/9HrWF0Zfvy2A3XI1zhV5husCmCK5nheal3egdbmhH+9PLycGET0ydRdtdy5m8FQRAUWqsKr0PKBnst/zsPzng/FLCP/f6v2l6vwqYVJJxZdNITTl0fsaOFK4n1SWvZ6RO9iPs6gwsZ+VpudGuZllODDLEhdlEQcxRAFaf++aRvtp8bAxNH3j4Q0NG4gfhscbyGVFqZlR/oONzqsso8OOZ7+nZb2wbcxbMdozrAXUTJrY8VI7bRFqfn68O0m6w6k+0iQqUeIcUqorut2YH/vvDND+2kC/zQfYdgz58PChg7Wl1doejiMoQZhx9s9DHMem/X10apK0d8bULeL+CbEcb1CmnpU5mRVgE3zHMr0sKs3MCwLTdmIAy7LKMIrrMm2g2w3VrPzTrJrhuX8iIAtLBzIzxrFrerljmVHug4mdzA2j0MnDyEfrfwFg+oC21w8AAA==",
                "AH4_H4sIAAAAAAAACtVX227jOAz9lUDP9qwv8vUtk+10C6SdIm4xCxSDhWLTiVBFSiW5M50i/76QL42dpEm36MNsnhyKPCSPSZF+RuNKiwlRWk3KBUqf0RkncwZjxlCqZQUWModTymF7WHRHFwVKvTix0LWkQlL9hFLXQhfq7GfOqgKKrdjobxqsSyHypQGrHzzzVOOEsYXO1zdLCWopWIFS13EGyMeha4wkGlg4J4OZLKtVFwF2HXwihM5KMAa57hm6fTXvtFshC0rYK5S6XhgOSMWt2ReqlmdPoHqOg52Ig2AQcdiRTu4hW9JSfya0jtsIVCfINMnvFUqDlsYw3sftoyYt6jXRFHgOvXjCXbtwyKDXmUr6CyZEN6XQed219nb491vrmyVhlNyrL+RRSAMwEHTp+NZQPoNcPIJEqWtIOlTLYWxKoOew4+8zXZyTVZ3omC8YSNU5MS+7QKkfOXgv+gFUvNlY6OynlqTtNMP8jch+kPUF1xXVVPBzQnnHh+1aaFpJuASlyAJQipCFruog0JXggKwG4WkNKDXEHMCbCqXfjXctQcHhCNHZ3yN7NCMSRuOHimiaj2agRCVzUKPgjwC9Yt3Eg+zOV32arSHXkrBJJSVw/UEc7KB+GBMHo60zOqLVy3tmlCai4hpkY2H0uypsiivTYm16nfJFpmFd36rbzNoCHMuPSagPV0d4y+lDBQYXlW6ZJ1Ewt4t57Nk4wsRO4qKwA1KWc1yUhQNztLHQlCr9tTQ+FErvmtI2CbxcDFGY4NdjzDQwRuQoI2wl+GgmwIBeCbki7C8h7g1Md9d8A1L/N3IF+qWLS8IUdF3dHvaZbUUNB9iNzB3WYWZaCr54D+ol5UZ6Q1fmXvGcT4HT+4V7bh2/53YKC+AFkU8fkE8NfKvgT1G1+p1iIzlB2wDNCw05jd2QmjrPnYtzhwL3E076P99CXzl7+rYEPs41fYSMvZJE3+1bqDlgfCPpei/XViEKPP9FZe+NH1I6FMRQzzTquNQgJ6RaLPWUrsx0dZuD3Q6uF6lKNuPbPPTmVEfRldANSxcFcE1zsyM0VB0YV34UJPsby5Hlw6xJ3VXc9dUMHioqocg00ZXZAcwe9ns225t76T8hHOyPU43w5qL931TnrYJJpbRYNYXQv0GOFe4HlWVv5hQEB6U/d23XIYWNC8+3ExwVdukSSOLYiUg8R5vv3dBpPyfuXgTN3Ll7RsMBFEThkQG0JIxxoUfZEhgbjEv3GJEvXWooMr4ahfHKzPe+GkpxkOzuh/5wV4+Np0qWJG/vyDb0IAlOrMXBxkK/zWfVdlF593pijI2kXpOaiv1B1s3SorpSu+7tMChFhAv+z12SJHbwfXR6Pd0Ctk68V9piW5h+4pOicLEdExzZ2PdCO8ZhbDu5S0JSBn6Uh3VhNrhtgnVI+HhIuA6p5yov/SAnJdhO4pY2LhPXnnvg2qGDHRfChMQhRpt/AUdNNWe9DwAA",
                "AH4_H4sIAAAAAAAACs1X227jNhD9FYPP0laiqOub182mAZw0iLJogWBRUNLIJiKLDklt4gb+94K62JItx9s0aOsnecg5c+Z4RjN+RdNK8RmVSs7yBYpe0UVJkwKmRYEiJSowkD6csxL2h1l3dJWhCAehgW4F44KpDYpsA13Ji5e0qDLI9mZ9f9tgXXOeLjVY/YD1U43jBQa6XN8vBcglLzIU2ZY1QH4busYI/YGHdZbMbFmtOgbEtsgZCp0XLwpIVc/R7l/D58NykTFanJDUxp43EJW0bl+YXF5sQPYCuweMXXfA2OtEp48QL1muPlNW89YG2RliRdNHiSK3ldELjnH7qGGLeksVgzKFHh/v0M8bKog7V8H+hBlVTSl0UQ+98YH+Tut9v6QFo4/yC/3OhQYYGLp0HGNov4OUfweBIluLNFbLXqBLoBew0+8zW1zSVZ3otFwUIGQXRP/YGYoc3yJH7AdQwXZroIsXJWjbaVr5ex4/0/VVqSqmGC8vKSs7PUzbQPNKwDVISReAIoQMdFOTQDe8BGQ0CJs1oEgLM4I351K9G+9WgIRxhshEJ86biPX5nk+8hlQJWswqIaBUH5TlAeqH5TrK9ijj0ej1raZAYsXXul9ZuYgVrOs34557W0RT8TGU+3A1h68le6pA46KMAgnCxDM9m1CTWBibgY8TEycUey4Edp5ZaGugOZPq11zHkCh6aMpTJ7Brbt8LyWmOsYKioGIS02LFy8kdBw16w8WKFr9w/qhhuvfFb0Dr79ouQe06MaeFhK4z20OdXtejranRgNi+fg91mLESvOxNsBPu92wF4qD1r+nL7ghFtv/Jtfof/yiw5fQCz2EBZUbF5gMyqoF/5lVSnNNo4Ii9cOe31+FkuqzspYvxJ+tQAuxr28kQP5LxiPO9YOujvNoLvoud3ZVhCicujZE4uMdWwCt1TV/Gf1TdodNcgZjRarFUc7bSo9FuDg5bt96CKtHMXv3QGzIjk8Tx3fB4mXhjL9AbTPcO7drlDp4qJiCLFVWVHs96Rfp3eugj6ni0Qf55J5wr+R8u2f+yNv9W7X2VMKuk4qumEJry6C3nJ8ryvXXYmx2enSQpDRLTshPXJEkAZphkYGI/z70AuxhyirbfuuHRrvYPO0MzPx5e0XCQuP5bg+SRrdesXDzTzWDm2W/JdpVBqVhKCy2IDtRcmK54VQ6uoYi44eGi5gyX5kBHqkROU4gLXaGjWzpxQ/fMuupuDfS/+buzXz7evXJoZ22ZaVWbcn2m62YRkZ02t729BEWIlrz84yEMQ5N8m1z8PjEnd1TAZPpUUcXSyR1IXokU5IT85KI+YC/IWE/si9TGLiEEPDPL9IJDaWiGBHumHYY08Yjl0dSpi7TBbROsKTlvU3JqSr1QBLtgO+CbIRDfJEEYmIFrBWaQuDlNLS+wvQBt/wJZbl89VQ8AAA==",
                "AH4_H4sIAAAAAAAACt1XW2/rNgz+K4Ge7S2WbfnylpP1dAXSrqg7bEBxMDA2kwh1rFSSz2lX5L8P8iW1Hfeytg/D3hyK/PiRIkXmkcxKLeagtJqv1iR+JCcFLHOc5TmJtSzRIuZwwQt8Oszao7OMxDSMLHIpuZBcP5DYsciZOrlP8zLD7Els9Pc11rkQ6caAVR/UfFU4LLTI6e56I1FtRJ6R2JlOe8gvQ1cYUdCzmL5KZr4pty0Dz5l6r1BorUSeY6o7hk5Xjb7uVsiMQ/5MSh3KWC+pXmP2lavNyQOqjmN/wNj3e4xZm3S4xWTDV/oL8Iq3EahWkGhIbxWJ/SaNLDzG7aJGDeolaI5Fih0+bGjH+hmkrankf+McdF0KrdehNR3k322srzeQc7hVX+G7kAagJ2jDca2+/ApT8R0liR2TpLFaZqEpgY7DNn9f+PoUtlWgs2Kdo1StE3PZGYndYOodse9Bhfu9RU7utYSm00zmr0XyA3ZnhS655qI4BV60+bAdiyxKieeoFKyRxIRY5KIiQS5EgcSqER52SGKTmBG8hVD63XiXEhWOMyQ2eea89lidP/FJdphqCfm8lBIL/UlRDlA/LdZRtkcRj3qvtOoCSbTYmX7lxTrRuKtexifuTRHN5OdQ7sJVHH4v+F2JBpek1HdDdFPb8ymzPRalNiyzwA7DIFqyzPOXNCN7iyy40r+tjA9F4pu6PE0Ah+YOWOQ9zzHRmOcgJwnkW1FMrgQa0Asht5D/KsStgWnfiz8Qqt91C5pThdrE0TajEV3zLcpBk57z4nBkXoefphY5h/uuLDSyBrJOlucE5sFqnSdaiqJqwEbr4GMFuULro6yiY1IOGyE1dTukFrjGIgP58F5eQ+BfRLnMD4l+BrFnSFl0sOvn6A03QUduIjgKuuviLRGPGF9LvjuKq1EIfOoeVI6ueUxpjERfz7TxbKVRzqFcb/SCb838dOqDYX9Xq1Ip6wFtPjqTaGTcuIEfDefsizuLWXPah7btqSu8K7nELNGgSzPDzR41bLQPVtQn9s+bGuDjlf5aSb+5JP8XtdcWm/cvi60zRZYsTCH1fNulfmR7NPBtyDzXXlFvFaSMrhzqkP23dow0S/7NQVBPkptH0h8pfuA/P1JOc1BqMhcS8t70c17KzVmGheYp5CYhxlGtMNuKsuipkdjzo+HK5vbX59B4KuUKUkxyU4YN7+O+HW6q/t4i/5l/Ok97x7u3DWNsJHOTxroIf8Cu3kFU256XnZWExAQKUfx1E0WR7X6bnPw5sSdXIHEyuytB83RyhUqUMkU1cX/2SRew42Sk0jtViRkL0nAZ2l4YurbHwtQGGqJNnaU7XU5DOg2wqsoatwmwokRfpkQrSh1XkQ8rDJhvpyyjtud5zA59oDYLM5dOIVxmAGT/D47Bq/xQDwAA",
                "AH4_H4sIAAAAAAAACs1X227jNhD9FYPPUivqRklvXjebBnDSIMpiCwSLgpJGNhFZdEhqN9nA/15QF1uSlUuTFO2bTM6cOXM0oxk/onml+IJKJRf5CkWP6KSkSQHzokCREhUYSF8uWQmHy6y7OstQZAehgS4F44KpBxRhA53Jk/u0qDLIDsfaftdgnXOerjVY/WDrpxrHDwx0ur1eC5BrXmQowpY1QH4eusYIycDDepHMYl1tOgYuttwXKHRevCggVT1H3DezXw7LRcZo8YSk2Pb9gahu6/aZyfXJA8heYG/E2PMGjP1OdHoL8Zrl6hNlNW99ILuDWNH0VqLIa2X0g2PcPmrYol5SxaBMocfHH/v5QwXtzlWwn7CgqimFLurY2x7p77Te12taMHorP9PvXGiAwUGXjmMMz68g5d9BoAhrkaZq2Q90CfQCdvp9YqtTuqkTnZerAoTsguiXnaHIIZZ7xH4AFex2Bjq5V4K2naaVv+bxD7o9K1XFFOPlKWVlp4eJDbSsBJyDlHQFKELIQBc1CXTBS0BGg/CwBRRpYSbwllyqN+NdCpAwzRCZ6In7JmJ9f+ATbyFVghaLSggo1QdlOUL9sFwn2R5lPBm9tmoKJFZ8q/uVlatYwbb+Mh64t0U0Fx9DuQ9Xc/hSsrsKNC5Kk5wEmNgmkCQ33cQiJvVsbCau71k0B58ARjsDLZlUf+Q6hkTRTVOeOoF9cxM/dJ/mGCsoCipmMS02vJxdcdCgF1xsaPE757capvtefAVa/25aUN9KUDqPrhn10TXbgBg16Tkr91coCn6xDHRO73tH2NdnLWKjlYuJ/l51sWMleFn3X2u1D5HTQoLxXlLhK0lZTo/UElZQZlQ8vJXXGPg3XiXFXucnEAeOth/u/YYavZyzbR8nbZOjpPshXpPxhPO1YNujvFoD4tnO3uToNU8ZTZEY2bEN8Eqd0/vuNeq+nucKxIJWq7Vaso0eqLi5GDd8vTtVopnY+qE3mibmj0O8cDx4n11i9N7TfXm7JruCu4oJyGJFVaWHul6sxp33zhr7wI56VUu8v/ZfKvJXF+l/WY3/qPa+SFhUUvFNUwhNefRW+n+3LHsDyCFZAoHvmFYCoekS4plhCI5peTi3iJU5QWCj3bduArX/D272B80QunlEw2nkEefpafR1zRTMYkVFUgmpBsMTP6fkWQalYikttEY6WGMw3/CqHJihyPXC8cbnDLfvQEeqRE5TiAtdtJPrvnus7njv9XYG+t/8bzpsMW/eXbSzPlloVZsK/kG3zUYjO20uewsOihAtefnXTRiGpv1tdvLnzJxdUQGz+V1FFUtnVyB5JVKQM/tXD/UBe0Gm2uRQqK4FlDhWZtpgp6aburmZeEDMzEpTTHCCSeDUhdrgtgnWlPDzlHBNqRfK93Iv8ezM9K2Ami4NAzMheWg6xM0dij3qeSHa/Q0NZWdrng8AAA==",
            },
            AmountRequired = 5,
            UniqueFish = true,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Full-blown Bubble"] = new List<uint>()
                {
                    47577,
                },
                ["Glass Coral"] = new List<uint>()
                {
                    47492,
                    47558,
                    47575,
                },
                ["Shallnot Shell"] = new List<uint>()
                {
                    47576,
                },
                ["Skippingway"] = new List<uint>()
                {
                    47491,
                    47557,
                    47574,
                },
                ["White Starburst"] = new List<uint>()
                {
                    47490,
                    47556,
                    47573,
                },
            },
        },
        // Export for Mission [1000] - EX: Elemental-esque River Specimens
        [1000] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XS2/jOAz+K4XO9sLv2L6l2bYTIH2gzmAOxWAh23QiRJZSSe5ji/z3gfxI7DRJd4oCu4e9GST18SNFUvQbGleKT7BUclIsUPyGLhhOKYwpRbESFRhIK2eEwU6Zd6ppjmInjAx0JwgXRL2i2DbQVF68ZLTKId+Jtf2mwbrmPFtqsPrD0V81ThAa6Go9XwqQS05zFNuWNUA+DV1jRKPBCetDMpNlVXYMPNvyPqDQneKUQqZ6B+2+mfOxWy5ygumRlNpOEAyS6rXHLolcXryC7Dn29xj7/oBx0CUdryBZkkKdY1Lz1gLZCRKFs5VEsd+mMQjf4/ZRoxb1DisCLDtWGp5tBfswwTChTockyN8wwaqpjI7E/mln7zrc9vR8iSnBK3mJn7jQAANBF51rDOX3kPEnECi2dc4OlXYQ6oroOezSeU4WV7is4x6zBQUhOyf67nMUuyPLe8d+ABVuNga6eFECDxpvS0Dfy5wnz3g9ZaoiinB2hQnr0mPaBppVAq5BSrwAFCNkoJuaE7rhDFCL8LoGFOs8HcCbcak+jXcnQMJhhshER/SNx1q/45OsIVMC00klBDD1RVHuoX5ZrAfZvov4oPfaqqmXRPG17mbCFomCdT03d9zbmhqLr6Hch3vP9JmUKSbqklAqT+jvKyZvqw7hOyOPFWhmyIfCGvkQmHnqRqYX2IWZukVq+s4oC/Og8N00RBsDzYhUt4VmKVH88Fbz1SnYDrNREDnHozynFZwlCou8kkrj3XBRYvqN85VG6EbRD8CrXTdprQSl4+j6SovmpASx12/XhG1VutP/sAx0jV96MsfSshayybRnO6NQZ7v1nijB2eIr/Ief8j+DBbAci9cPKfQgLF0vf/IqpdvsDSycINoa7AI8ajLgcMBqLsj6mKeR77hbk2O+BkYnvHV2pAReqWv8gmLH1inUzTcuFIgJrhZLNSOlfhPtRrHflfX6U4nm0dUfveekmfR+9H5tOLEB6F2lm4dd9d7DY0UE5InCqtIPsV6Gfquke/c5in6/IvfL4f+COl5Q/3r5dCN5wium+ucMdMvo63cJP5bAbni9YI+fMKH63rrL6o1uKw9dKwsiE3I/Mz1wLDO0LWzaTg5BgS0bRy7a/Oxmd7u5P2wFzfh+eEN7c9yOTszxKk0pYYuzabkGmSkuBm+XfSrB0xyYIhmmOqvaX2MwLnUi+mYo9vxof/9yh6uxnptJJQqcQUJ1Lbf0/cj/YO30Nwb6z/zFJGssoKjoN8zyjoXePv1DPAaV0ybMNerrm+ZzPllCttreYO+HxhqsVp9eSvTh88590zLPeN1sG7Krz/7ygWKEGWd/PdiWZZn2z7MLCiUwhakJ8rGCs3vyBOJMb1ukBCZRH7Hn5UBj9trAC6PC9T3bLFJcmF6YhSYeZYXpOsUoLPwwTAOo26DBbSPsEXP+CbGewyyyIEiLzMxssE0PR7mJwyI1vcAbZZZj+U6Ro80v8Ihm45wPAAA=",
                "AH4_H4sIAAAAAAAACu1XS2/jOAz+K4XO9sLv2L6l2bYToI9Bk8EcisFCtuhEiCKlktzHFvnvC9lRYqdJOjtbYPewN4OkPn6kSIp+Q8NaixFWWo2qGcrf0AXHBYMhYyjXsgYHGeU15bBTEqsaE5QHaeagr5IKSfUryn0HjdXFS8lqAmQnNvbrFutGiHJuwJqPwHw1OEnqoKvVdC5BzQUjKPc9r4d8GrrByAa9E96HZEbzemkZRL4XfUDBnhKMQak7B/2uWfCxWyEJxexISv0gSXpJjTbHLqmaX7yC6jiO9xjHcY9xYpOOFzCZ00qfY9rwNgJlBRONy4VCebxJY5K+x+2iZhvUr1hT4OWx0oh8L9mHSfoJDSySpH/CCOu2MiyJ/dPB3nWEm9PTOWYUL9QlfhLSAPQENrrQ6cvvoRRPIFHum5wdKu0kNRXRcWjTeU5nV3jZxD3kMwZSWSfm7gnKw4EXvWPfg0rXawddvGiJe423JWDuZSomz3g15rqmmgp+hSm36XF9B13XEm5AKTwDlCPkoNuGE7oVHNAG4XUFKDd5OoB3LZT+ZbyvEhQcZohcdETfemz0Oz6TFZRaYjaqpQSuPynKPdRPi/Ug23cRH/TeWLX1MtFiZbqZ8tlEw6qZmzvum5oays+h3IV7z/SZLgtM9SVlTJ3Q39dc3dUW4RunjzUYZijOskEEBbgxZIkbZVnlpkHkuQVOSJREXkS8GK0ddE2VvqsMS4Xyh7eGr0nBdpgNkiw4HuU5q+FsorEktdIG71bIJWZfhFgYBDuKvgNe7LrJaBVoE4ftKyOa0iXIvX67oXyrQrk/+M1z0A1+6cgC38g2kG2mI39gZqF1PtFS8NlnuE8OuPcOuPfCjvtrmAEnWL5+yGAf4XdRF2ybu55FkGRbg118R016HA5YTSVdHfM0iINwa3LMV8/ohDdrR5cgan2DX+wFmtYbVhrkCNezub6mS/Mi+q1ivyeb5aeW7ZNrPjqPSTvn4+z90nDi/Tebip2Gtnbv4bGmEshEY12bZ9isQn+roP9hQf5fUD9fUP96+diBPBI1191zDrrj7PWbgu9z4LeiWa+HT5gyc2/2sjqDmxS4wFkSul5FMjdKA3CzQZi4CYmyMoGqCnGK1j/s5N7s7Q9bQTu8H97Q3hT3sxNTvC4KRvnsbLxcgSq1kL2Xyz+V4DEBrmmJmcmq8dcaDJcmETuzQ4tonO0vY2F/T06N41pWuIQJM6W9iSbO4g920HjtoP/ML81khSVUNfuCObEszCoaH+LRK6RN/kKnuc0xmYrRHMrF9kI7fzdeb8/65Q3FHD637tsOesardvVQ9va6mwjKEeaC//Hge57nBj/OLhgsgWvMXFCPNZzd0yeQZ2b1okvgCnURO14O9GmnKxKIo7hKY7coSOVGYVW5RQqRW2RBUGVJSaIwa7qixd1E2CHm/wyxjsMSE+KVELseeKUbDYrKTWPwXOIHSRhUnlGj9V9zYsptqQ8AAA==",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [1002] - EX: Bubble Bursters Distribution Survey [10/21]
        [1002] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XS2/jOAz+K4HO9sIPybF9SzOdboC0U9RZzALFHBSbToQqVirJnWaK/PeB/EjsPJpB0cMe9qaQ1MePNEUyb2hUajGmSqtxvkDxG7ou6JzDiHMUa1mChYxyygrYK7NWNclQ7IWRhe4lE5LpDYpdC03U9WvKywyyvdjYb2usWyHSpQGrDp45VThBaKGb9WwpQS0Fz1DsOk4P+X3oCiMa9m44F8mMl+WqZYBdB1+g0N4SnEOqOxfdrpl32a2QGaP8TEpdLwh6ScXNta9MLa83oDqOyQFjQnqMgzbp9AmSJcv1FWUVbyNQrSDRNH1SKCZNGoPwGLeLGjWo91QzKFLo8AkO7wX9DHrtVcl+wZjquhRO1VUQHoF5B5/Db8BmS8oZfVJf6YuQBq8naKPzrb78AVLxAhLFrsnZGQq457BN5xVb3NBVFfeoWHCQqnXinUTyhw4+CqaHHG63Frp+1ZI279B8l5lIftL1pNAl00wUN5QVbapt10LTUsItKEUXgGKELHRXcUJ3ogBk1QibNaDY5OkE3lQo/WG8ewkKTjNENjqjrz1W+j2fZA2plpSPSymh0J8U5QHqp8V6ku1RxCe9V1Z1vSRarM1rZsUi0bCu+uaee1NTI/k5lLtwFYd/CvZcgsFFQ/Bx6njEHpJ8buPQx3aYU8+OsiDDhLh+6FK0tdCUKf0tNz4Uih/r8jQB7J7+MIjweY6JBs6pHCSUr0QxeBBgQO+EXFH+txBPBqbtJt+BVr+NXIHePaeccgXt82qUJrz2oTWiOgfYHZou1WImWoqiM9/OXJ+xFciD93tLX3cq0wL+co5cOX7H1RQWUGRUbi56O0T4Iso5Pwy/tvCCaGewj+WsSY/DCauZZOtznobE83cm53z1jN7x1tiZch/lGuSYloulnrKVmUJurTh8B9XCUcp6zJlDp4GPaZECH2kNq7Vuc2lsZlQuoMb8VvDN9yUUd0KPUs1eYJJBoVlq5m194WSHJtHx9H9nkJuVo21rbQU/wHPJJGSJpro089TsNGfK+kKZ/nGF/V9IHyqkj37zTuuMiOs4QZDZ0TB3bezguU2JH9gReJRmjheGOUHbH23vbPbex52gbp+Pb+igj3rR+T56Vc7nHBb0F8hey3ffy82u/k1CjKPaYLQSZdEzQzEm0eGe4vc3ytB4KmVOU0i46WcntzdMInJheSNbC/1n/gvsZ++HJ27yk66NZGyyWiW0O4ObyWuOtXhvdqp0O2UWOCmN0jy1SYgdG2dkaIcBDWziRO7cd7AXwbwqsxq3HbjwXJpeMbj+dzCZDOxBXTmDq1IqDVINvjClJZuXpn8NklK+wKa/GYDn+MTFvp3RPLBxFHl2OA8zO02JkwY4IiTy0PY3A0AOekEOAAA=",
            },
            AmountRequired = 0,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
            },
        },
        // Export for Mission [1005] - EX+: Elemental-esque Chasm Specimens
        [1005] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACu1XbW/jNgz+K4G+Ljr4RX79lsvSLkDaKy4pOqAYBkWmY6GOnUpy73JF/vsgvyR24iaHQ7HbhuWTQ5EPH9KkSL+iUaHyMZVKjuMVCl/RJKPLFEZpikIlChgifTjjGRwOo+ZoGqHQ8oMhuhM8F1xtUWgO0VROvrK0iCA6iLX+rsK6yXOWaLDywdJPJY7rD9H1ZpEIkEmeRig0DaODfB66xAi8joVxkcw4KdaawZ95lm7vJTxwlUwzVXDF86wJsydqYhrkAt3GQ56mwFQTJjENs61mXaaYi4jT9I30m5brdl4Aqc2uuEwmW5Atx84RY8fpMHabF0SfYJ7wWH2kvOStBbIRzBVlTxKFTp1y1z/FbaMGNeodVRwyBi0+7rGd282g1ZgK/g3GVFVl03g9traO8m/X1ouEppw+ySv6kgsN0BE04djDrvwzsPwFBApNnaS+und9XQIth03+PvLVNV2XgY6yVQpCNk70y45QaHsGOWHfgfJ3uyGafFWCdrpyT0C/iEU+/0I3+2K9pjxr0oPNIZoVAm5ASroCFCI0RLclJ3SbZ4BqhO0GUKjz1IM3y6X6Ybw7ARL6GSKM3jivPJbnBz7zDTAlaDouhIBMvVOUR6jvFmsv25OIe72XWlW9zFW+0e3Ls9Vcwaa8VA/c65oaifeh3IYrOdxn/LkAjYuCJY0JiWJs2pRiYtEA+6ZJsWG5nkEdEoFlot0QzbhUn2LtQ6Lw8bX0pgPY97rnBs7bHK90F3yhCsRgJFQi8rQQoHFvc7Gm6W95/qSRmhvkAWj5X8slqH1rxDSV+9u6PtQRNk1Ti6o0ENPTN1ODOVciz1qddtncsFvmM1hBFlGxfQdeJfC9hF/zotZvFCvJhfA7aJarg6zsDiFqlQVfgzi6VW54tj/SF98Ho/MjZ8C/JwE9xgvBNycR1QqeY9l7lS75N5T6SHT1dGeNYgViTItVomZ8rSecWR0ct1y5+BSiGqH6oTUregaC7TnB6U5wZrzrpaW5+5oa/wzPBRcQzRVVhZ6yeiv6yYWvd6S+ctHyo5LxPhhD9ClLtw8JZCOm+AvM05/UQr3dcqktvru4/zVVfC9hXEiVr6uSat8nf0OBt4ZJ7ASOwXwPB8yKMGFmjP14GeDY8wNCA4ta1EW7P5ppUn8mPO4F1UB5fEVHk8Xxzky/LVOcJSrZys4QNM9lcRpBpjijqc6PdlQpjNZ5kXXUUEic4HiRs7tLta89FSKmrO6E3i2eOHo+nl1nnd0Q/WM+nQ7byA/vINpYS8Y6q2VC21tJvYvox0p8UOst8kOZ2R4lxIxMbFm2j4kbMBwsDRcvA4stie3ZLNK7xWkZBW8HMKYbWaSgVDeM/6voP1tFsW2ZkWE72PDcGBN3aWJq+S62vIha4HuUsLi8rCrcmqIeg9Fg8vsvg+l0gAeTFNaQKZpikM8FDMYJleuB3v75GjJ9I7U8+p6xtD0vwMxwbExc4uOlByZmzGWGRRzmRAba/QWB3GBlsBEAAA==",
            },
            AmountRequired = 1,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Encapsulated Impesctor"] = new List<uint>()
                {
                    47661,
                },
            },
        },


        // - - - - - - - - - - 
        // Critical Rank
        // - - - - - - - - - - 
        // Export for Mission [1037] -  Soup Broth Ingredients [10/23]
        [1037] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACs1WS2/iSBD+K6jPtmSbdvtxIyyTRWKyUcxoD9Ee2u4ytGLcTHc7M2zEf1+1H8EmECYRI+3NVFd99VVRrxc0qbSYUqXVNF+h+AXNSpoWMCkKFGtZgYXM44KXcHhk3dOcodgLIwvdSy4k1zsUuxaaq9nPrKgYsIPY6O8brK9CZGsDVn945qvGIaGFbrfLtQS1FgVDses4A+T3oWuMKBhYOBfJTNfV5kxg2HXwBUYdiCgKyHQXCXYdt6/mXWYhJOO06ACIiwcAuFX7wtV6tgPVc+QfMfT9AUPS5Zw+QbLmub6hvOZpBKoTJJpmTwrFfptFEr7F7aNGLeo91RzKDHp8vGM7MsyY15lK/i9MqW4qofNKjqy9o3yPW+vlmhacPqkv9FlIAzAQdOGMraH8ATLxDBLFrknSqVImofnLew67/N3w1S3d1IFOylUBUnVOzJ/LUDwOHPyG/QAq3O8tNPupJW0bzWR+KZIfdDsvdcU1F+Ut5WWXD9u10KKS8BWUoitAMUIWuqtJoDtRArIahN0WUGwScwJvIZT+NN69BAWnGSIbnXlvPNbvBz7JFjItaTGtpIRSXynKI9SrxXqS7ZuIT3qvtZoCSbTYmn7l5SrRsK0H44F7W0QTeR3Kfbiaw7eSf6/A4CISEuZ5eWqnxGE2dt3UphmJ7DClzjgLPUoYQXsLLbjSf+XGh0LxY1OeJoDX5g4C093nOM70GrjcqdENLQqDdyfkhhZ/CvFkELpR8TfQ+reRK9CvTZjTQkHXlO2jiaxrz1bUhI/dwIygDjPRUpS93XXZ3Bn3zBewgpJRubsCrxr4D1GlxaVIB4YeiV7tDtGcVfkVxieMvylYSr5t4ugCaCQfIhv4ngmzsTxHd6D0ccKtuemiSa5BTmm1WusF35j15TYPx+1VHyqVbPaj+egtghPTfhz40dsF/86uNkdGN+e6un6A7xWXwBJNdWVWqLlizhT7heL9aI1errmrFVdf62TBXLUyflMJfPY/781S7FGC09S3AyCejXGU2mkUeDbBjETg5wyAof0/3TBtL93HV0EzTx9f0HCwksA/P1jnJdecav4Mo6UUlR4sAve9BM0ZlJpntDBZMd4ahclGVOVADcXYj46vl/HwkgyNp0rmNIOkMPOvJe9H/oWjzd9b6H9z8x9W8KcXb/KDbo1katJYZ7C/itsFbD4b8UHtVMH2iotlhLEUh3aaYWxjjwR2yEhgZwH1cwzjCAdZXVwNbkvxAdhoUoDUI3uUiGo7upFCr0fzciWBcSi1Gp4DGaZj7AaOHfhhZGOXYJsGHrGpC06eBszx8gzt/wNxz0ugFw4AAA==",
            },
            AmountRequired = 4,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Initiative Trout"] = new List<uint>()
                {
                    47675,
                },
            },
        },
        // Export for Mission [1038] -  Oil-extractable Aquatic Life [10/21]
        [1038] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACs1XS2/jNhD+KwbPEiDqLd0cN5sG8GaDyIsegh7G4sgmwogORe1uGvi/L6hHLDl23E1dtDdlOPPNN+N55YVMay1nUOlqVqxI+kIuS1gKnApBUq1qtIh5nPMSd4+sf7pmJHXjxCK3ikvF9TNJqUWuq8sfuagZsp3Y6G9brM9S5msD1ny45qvBCWOLXG0Wa4XVWgpGUuo4I+T3oRuMJBpZOCfJzNb145HAfOr4Jxj1IFIIzHUfiU8dOlRzT7OQinEQPUBI/RGA36l94tX68hmrgaNgj2EQjBiGfc7hAbM1L/QF8IanEVS9INOQP1QkDboshvFb3CFq0qHeguZY5jjg4+7bheOMub2p4n/hDHRbCb3XcM/a3cu311kv1iA4PFSf4JtUBmAk6MPxrLH8DnP5DRVJqUnSoVIOY/OTDxz2+bvgqyt4bAKdliuBquqdmB+XkdSLHP8N+xFUvN1a5PKHVtA1msn8QmbfYXNd6pprLssr4GWfD5taZF4r/IxVBSskKSEWuWlIkBtZIrFahOcNktQk5gDeXFb6w3i3Cis8zJDY5Mh767F53/HJNphrBWJWK4WlPlOUe6hni/Ug2zcRH/TeaLUFkmm5Mf3Ky1WmcdMMxh33roim6jyUh3ANh68lf6rR4BJ0IgbgF3aS54nt04TZSbiMbKfwGCR5gMEyIVuLzHmlvxTGR0XS+7Y8TQCvzR1FpruPcbzUa+TquZpcgBAG70aqRxC/S/lgEPpR8QdC87eRV6hfm7AAUWHflN2jiaxvz07Uhu/TyIygHjPTSpaD3XXa3PEG5nNcYclAPZ+BVwP8m6yX4lSkI0M3TF7tdtEcVfk7jA8Yf61wofimjaMPoJX8EtkocE2YreUxuiOlXyfcmZsumhYa1Qzq1VrP+aNZX7R92G+v5lCpVbsfzcdgERyY9l4UJG8X/Du72hwZ/Zzr6/oOn2qukGUadG1WqLli9ov9P6/qf16+Z6vTodbB2jtrkf1L1fTR8hmMZRY7UPhFbCcsRtv3qG8vWYg2UEBwgVIvKMj2z34ud0fz/augHc33L2Q8o8MoOj6jFwq44OVqkpXARWEMh1uFvpeia4al5jkIkxfjr1WYPsq6HKmR1A+S/VPIG5+lsfFUqwJyzISpxo5+kAQnLsBga5H/zT8Qu33+4S2efYeNkcxMGpsMDvd6t83NZyveqR0q2UF5geNBQYvYduIksf0kBhuc3LWpDzliHjP03aa8WtyO4h2yyVSg0hN78oULG825mmszPCbTpxo0zydzXpgwhhcGeCGlFG0XaG774Lt2wiC3XXCZ47DAd0NKtj8BmGFHYGoOAAA=",
            },
            AmountRequired = 4,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Trailing Snailfish"] = new List<uint>()
                {
                    47677,
                },
            },
        },
        // Export for Mission [1039] -  Elemental-esque Specimen Acquisition [10/22]
        [1039] = new FishingTools()
        {
            FishingPreset = new List<string>()
            {
                "AH4_H4sIAAAAAAAACs1W227jOAz9lUDPNuCbfHtLs2m3QKZb1B3sQ7EPskQnQhU7leTpdIv8+0K+NHaaNNMiA+ybQ5GHh8wRqVc0rXU1I0qrWbFE6SualyQXMBUCpVrWYCFzuOAl7A5Zf3TNUOrFiYVuJa8k1y8odS10reY/qagZsJ3Z+G9brG9VRVcGrPnwzFeDE8YWutrcrySoVSUYSl3HGSF/DN1gJNEowjlJZraq10cKC1wnOMGoB6mEAKr7SgLXcYdu3mkWlWSciB4gdIMRQNC5XXK1mr+AGiTCewwxHjEM+56TR8hWvNAXhDc8jUH1hkwT+qhQirsuhvF73CFq0qHeEs2hpDDg4+3HheOOeX2o5P/CjOhWCX3WcC/a2+u330Xfr4jg5FFdkh+VNAAjQ1+Ob43td0CrHyBR6pomHZJyGJu/fJCw798FX16RdVPotFwKkKpPYv5chlI/coJ37EdQ8XZroflPLUl30Uzn76vsmWyuS11zzavyivCy74ftWmhRS/gGSpEloBQhC900JNBNVQKyWoSXDaDUNOYA3qJS+st4txIUHGaIbHTkvM3YnO/4ZBugWhIxq6WEUp+pyj3Us9V6kO27ig9mb7xagWS62pj7ystlpmHTDMYd905EU3keykO4hsP3kj/VYHAReIGDWcHsPGfYDkK3sOMQY9vBDIdhTikjAdpaaMGV/qswORRKH1p5mgLeLncUOf5xjpdG9s9Eg5xMpV7JStQSDO5NJddE/FlVjwapHxl/A2l+G7sC/XYZCyIU9JezOzQV9te0M7VtCNzIjKIeM9OyKgc77HS44w/CF7CEkhH5cgZeDfAfVZ2LU5WOAr0weYvbVXPU5VcYHwj+ruBe8k1bR19Aa/kU2Qh7psw28hjdkdPnCXfh5jZNCw1yRurlSi/42qwxtz3Yv2bNg6WW7Z40H4OFcGDq+xFO3i/6D3a2eWz0867X9R081VwCyzTRtVml5jVzROwnxPtZjZ7W3NnENfQ6KJizKuM3SeCr//lgpuYkD/wIYhs7cWAHIWV27sfUdgo/yos8AJcwtP2nH6rdi/fhzdDO1YdXNB6wYewcH7AXooZJponMa6n0aB24H7XnmkGpOSXC9MTkah2m66ouR24oDXCy/4bxx+/J2GSqZUEoZMJMv446TvCJpxveWuh/8/LfLeIvr9/smWyMZWba2HRwuJC7NWw+W/PO7ZBcB9LyEhbTIExsQiGyA88BO3YL16a5W5DAL3BIikZaLW5H8Q7YZCpA6ok9mQtYQ6mJsEE9Gc1sgPI1lJMpfaq5asbW+Ing4iBkLPHsiIZgBzGObZK4xI6pTx1chDnkPtr+Byfpt3wrDgAA",
            },
            AmountRequired = 4,
            UniqueFish = false,
            Baits = new Dictionary<string, List<uint>>()
            {
            },
            RequiredFish = new Dictionary<string, List<uint>>()
            {
                ["Blue Starburst"] = new List<uint>()
                {
                    47680,
                },
            },
        },

    };
}
