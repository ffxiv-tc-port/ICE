using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.Exd;
using ICE.Utilities.Cosmic_Helper;
using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace ICE.Utilities;

public class PlayerHelper
{
    // A lot of these functions are dupes to what is in Ecommons: GameHelper.Player
    // Which means that a lot of these can get depreciated becuase they are either:
    // -> Safer in how they are grabbed
    // -> Less Reduntant in code
    // -> Just genereally better 

    // 宇宙探索支援的職業＝能工巧匠(8~15)＋大地使者(16~18)，即 CosmicHelper 的兩份清單。
    // 台服 7.20 EXD 實查：Action 43357 的 ClassJobCategory ＝ 35(CRP…FSH)，剛好等於這 11 個職業。
    // 舊寫法 jobId >= 8 || jobId <= 18 是 || 恆為 true＝過濾失效，改用權威清單。
    public static bool UsingSupportedJob()
    {
        var jobId = Player.JobId;
        return CosmicHelper.CrafterJobList.Contains(jobId) || CosmicHelper.GatheringJobList.Contains(jobId);
    }

    public static bool IsInCosmicZone() => IsInSinusArdorum() || IsInPhaenna();
    public static bool IsInSinusArdorum() => IsInZone(1237);
    public static bool IsInPhaenna() => IsInZone(1291);
    public static bool IsInZone(uint zoneID) => Svc.ClientState.TerritoryType == zoneID;
    private static IPlayerCharacter Object => Svc.Objects.LocalPlayer;
    private static unsafe float AnimationLock => *(float*)((nint)ActionManager.Instance() + 8);
    public static bool IsAnimationLocked => AnimationLock > 0;
    public static bool CustomIsBusy => GenericHelpers.IsOccupied() || Object.IsCasting || IsAnimationLocked;

    public static unsafe bool HasStatusId(params uint[] statusIDs)
    {
        if (Svc.Objects.LocalPlayer == null)
            return false;

        var statusID = Svc.Objects.LocalPlayer.StatusList
            .Select(se => se.StatusId)
            .ToList().Intersect(statusIDs)
            .FirstOrDefault();

        return statusID != default;
    }

    public static int GetGp()
    {
        var gp = Svc.Objects.LocalPlayer?.CurrentGp ?? 0;
        return (int)gp;
    }

    public static int MaxGp()
    {
        var maxGp = Svc.Objects.LocalPlayer?.MaxGp ?? 0;
        return (int)maxGp;
    }

    internal static unsafe float GetDistanceToPlayer(Vector3 v3) => Vector3.Distance(v3, Player.GameObject->Position);
    internal static unsafe float GetDistanceToPlayer(IGameObject gameObject) => GetDistanceToPlayer(gameObject.Position);

    /// <summary>
    /// 現在讀得到自己的道具數量嗎。
    /// </summary>
    /// <remarks>
    /// 🔴 傳送／換區途中 <c>InventoryManager.GetInventoryItemCount</c> 會
    /// **一律回 0 而且不報錯**，所以任何「數量是 0 / 數量不夠 → 放棄任務、切換狀態、
    /// 清空佇列」的破壞性判斷，都必須先過這個閘門。<br/>
    /// 2026-08-03 實機事故：使用者身上有 999 個宇宙蛾蛹，卻在被機甲行動傳送走的
    /// 同一毫秒（<c>BetweenAreas=True</c>）被判定成「沒餌」而放棄了任務。<br/>
    /// ⚠️ 這裡只放「真的會讓道具讀不到」的條件。多加其他 ConditionFlag（例如
    /// <c>OccupiedInQuestEvent</c>）會在正常流程中把整條路徑擋住，得不償失。<br/>
    /// ⚠️ 這是 8daada5 在 Task_Fishing 裡建立的模式，提升到這裡共用 ——
    /// 需要同樣的守衛時請呼叫它，<b>不要再各自寫一份</b>。
    /// </remarks>
    public static bool InventoryReadable()
        => Player.Available
           && !Svc.Condition[ConditionFlag.BetweenAreas]
           && !Svc.Condition[ConditionFlag.BetweenAreas51];

    public static unsafe bool GetItemCount(uint itemID, out int count, bool includeHq = true, bool includeNq = true)
    {
        try
        {
            itemID = itemID >= 1_000_000 ? itemID - 1_000_000 : itemID;
            count = 0;
            if (includeHq)
                count += InventoryManager.Instance()->GetInventoryItemCount(itemID, true);
            if (includeNq)
                count += InventoryManager.Instance()->GetInventoryItemCount(itemID, false);
            count += InventoryManager.Instance()->GetInventoryItemCount(itemID + 500_000);
            return true;
        }
        catch
        {
            count = 0;
            return false;
        }
    }
    #region 道具的「已學會／已登錄」狀態

    /// <summary>
    /// 一件道具對應的內容有沒有被學會／登錄過。
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>Unknown = 0</c> 是刻意的：這個列舉會被 <c>default</c> 取到，
    /// 而「不知道」是唯一不會造成破壞性判斷的預設值。
    /// </remarks>
    public enum ItemUnlockState : byte
    {
        /// <summary>問不到答案（還沒登入、EXD 還沒載入、或遊戲回了未定義的值）。</summary>
        Unknown = 0,
        /// <summary>已經學會／已經登錄。</summary>
        Unlocked = 1,
        /// <summary>還沒學會，但學得起來。</summary>
        NotUnlocked = 2,
        /// <summary>這件道具根本沒有「學會」這回事（素材、魔晶石之類）。</summary>
        NotApplicable = 3,
    }

    // ItemUnlockState 的短期快取。查詢本身會進遊戲的 EXD 模組，而呼叫端
    // （採購清單 UI）在繪製路徑上每幀都會問一次，所以壓成每秒最多一次。
    // 只存列舉值，不存任何原生指標。
    private static readonly Dictionary<uint, (long Tick, ItemUnlockState State)> UnlockStateCache = new();
    private const long UnlockStateCacheMs = 1000;

    // 查詢整個爆掉過一次就不再問（見 GetItemUnlockState 裡的 catch）。
    private static bool UnlockQueryUnavailable;

    /// <summary>
    /// 查一件道具的「已學會／已登錄」狀態。
    /// </summary>
    /// <remarks>
    /// 🔴 呼叫端只能把 <see cref="ItemUnlockState.Unlocked"/> 當成「確定已學會」。
    /// 其餘四種（含 <see cref="ItemUnlockState.Unknown"/>）都必須維持原本的行為 ——
    /// 「不確定就不買」在使用者眼裡跟外掛壞掉沒兩樣，而且是靜默的。
    /// </remarks>
    public static ItemUnlockState GetItemUnlockState(uint itemID)
    {
        if (UnlockQueryUnavailable)
            return ItemUnlockState.Unknown;

        itemID = itemID >= 1_000_000 ? itemID - 1_000_000 : itemID;

        var now = Environment.TickCount64;
        if (UnlockStateCache.TryGetValue(itemID, out var cached) && now - cached.Tick < UnlockStateCacheMs)
            return cached.State;

        ItemUnlockState state;
        try
        {
            state = QueryItemUnlockState(itemID);
        }
        catch (Exception e)
        {
            // 🔴 這個 try/catch 只擋一種東西：特徵碼沒解析出來時 FFXIVClientStructs 的
            //    [MemberFunction] 是**擲 InvalidOperationException**（ThrowNullAddress），
            //    不是回 null。而這條路徑會被採購清單在 ImGui 繪製路徑上呼叫 ——
            //    繪製路徑擲一次例外，整個 ICE 介面到重開遊戲前都不會回來。
            //    ⚠️ 它擋不到 AccessViolationException（corrupted-state exception，
            //    在 .NET Core 上 catch 不到）；那一類只能靠上面的判空。
            //    📌 台服 7.20 執行檔離線驗過兩支特徵碼都唯一命中
            //    （ExdModule.GetItemRowById、UIState.IsItemActionUnlocked），
            //    所以這裡預期永遠不會觸發；真的觸發就代表台服改版動到它們了。
            UnlockQueryUnavailable = true;
            IceLogging.Info($"查不到道具的學會狀態，「已學會就不買」不會生效：{e.Message}", "[PlayerHelper]");
            return ItemUnlockState.Unknown;
        }

        // Unknown 不進快取：登入前問一次就把整局釘死在「不知道」會很難查。
        if (state == ItemUnlockState.Unknown)
            UnlockStateCache.Remove(itemID);
        else
            UnlockStateCache[itemID] = (now, state);

        return state;
    }

    /// <remarks>
    /// 📌 <c>UIState.IsItemActionUnlocked</c> 的回傳值對照表來自 FFXIVClientStructs 的
    /// **散文註解**（1 已學會／2 還沒學會／3 資料未載入／4 沒有學會狀態），不是被驗證過的
    /// 欄位偏移。所以這裡只信「1 ＝ 已學會」這一格：其他值全部走「不擋購買」那條路，
    /// 註解就算錯了也只會退回現行行為，不會多買也不會少買錯東西。
    /// </remarks>
    private static unsafe ItemUnlockState QueryItemUnlockState(uint itemID)
    {
        if (!Player.Available)
            return ItemUnlockState.Unknown;

        var uiState = UIState.Instance();
        if (uiState == null)
            return ItemUnlockState.Unknown;

        // GetItemRowById 查無此列會回 null（不擲例外）。null 解參考是 AVE，try/catch 攔不到。
        var itemRow = ExdModule.GetItemRowById(itemID);
        if (itemRow == null)
            return ItemUnlockState.Unknown;

        return uiState->IsItemActionUnlocked(itemRow) switch
        {
            1 => ItemUnlockState.Unlocked,
            2 => ItemUnlockState.NotUnlocked,
            4 => ItemUnlockState.NotApplicable,
            _ => ItemUnlockState.Unknown,
        };
    }

    #endregion

    public static bool HasFoodRunning()
    {
        if (!C.UseGatheringFood || C.GatheringFood == 0)
            return true;

        var foodBuff = Svc.Objects.LocalPlayer.StatusList.FirstOrDefault(x => x.StatusId == 48 && x.RemainingTime > 10f);
        if (foodBuff == null)
            return false;
        if (Svc.Data.GetExcelSheet<Item>().TryGetRow(C.GatheringFood, out var itemInfo))
        {
            var desiredFood = itemInfo.ItemAction.Value;
            if (foodBuff.Param == desiredFood.DataHQ[1] + 10000)
                return true;
            if (foodBuff.Param == desiredFood.Data[1])
                return true;
        }

        return false;
    }
    public static unsafe bool NeedsRepair(float below = 0)
    {
        var im = InventoryManager.Instance();
        if (im == null)
        {
            Svc.Log.Error("InventoryManager was null");
            return false;
        }

        var equipped = im->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipped == null)
        {
            Svc.Log.Error("InventoryContainer was null");
            return false;
        }

        if (!equipped->IsLoaded)
        {
            Svc.Log.Error($"InventoryContainer is not loaded");
            return false;
        }

        for (var i = 0; i < equipped->Size; i++)
        {
            var item = equipped->GetInventorySlot(i);
            if (item == null)
                continue;

            var itemCondition = Convert.ToInt32(Convert.ToDouble(item->Condition) / 30000.0 * 100.0);

            if (itemCondition <= below)
            {
                IceLogging.Debug($"Found an item that needed repair. Condition: {itemCondition}");
                return true;
            }
        }

        return false;
    }

    public class ManipInfo
    {
        public uint ActionId { get; set; }
        public bool HasUnlocked { get; set; }
    }

    public static Dictionary<uint, ManipInfo> ManipClassInfo = new()
    {
        [8] = new ManipInfo { ActionId = 4574, HasUnlocked = true },
        [9] = new ManipInfo { ActionId = 4575, HasUnlocked = true },
        [10] = new ManipInfo { ActionId = 4576, HasUnlocked = true },
        [11] = new ManipInfo { ActionId = 4577, HasUnlocked = true },
        [12] = new ManipInfo { ActionId = 4578, HasUnlocked = true },
        [13] = new ManipInfo { ActionId = 4579, HasUnlocked = true },
        [14] = new ManipInfo { ActionId = 4580, HasUnlocked = true },
        [15] = new ManipInfo { ActionId = 4581, HasUnlocked = true },
    };

    // 技能 ID → Action.UnlockLink 的快取。資料是靜態的，查一次就夠；
    // UpdateHasManip 有一個呼叫點在除錯視窗的繪製迴圈裡（每幀都會進來）。
    private static readonly Dictionary<uint, uint> ManipUnlockLinks = new();

    /// <summary>
    /// 用「技能的解鎖連結有沒有解開」判斷這個職業學會工程管理了沒。
    /// 回 <c>null</c> ＝ 判不出來（查不到技能列，或該技能沒有解鎖連結），呼叫端要當成「不知道」。
    /// </summary>
    private static unsafe bool? IsManipUnlockedByUnlockLink(uint actionId)
    {
        if (!ManipUnlockLinks.TryGetValue(actionId, out var unlockLink))
        {
            if (!Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>().TryGetRow(actionId, out var action))
                return null;

            unlockLink = action.UnlockLink.RowId;
            ManipUnlockLinks[actionId] = unlockLink;
        }

        // 0 ＝ 這個技能沒有解鎖連結，那就不是這條路能回答的問題。
        if (unlockLink == 0)
            return null;

        // 📌 UIState.Instance() 沒有判空：CS 宣告是 [StaticAddress(sig, 3)] **沒有** isPointer:true
        //    ⇒ 解出來的就是物件本身的位址（lea），永不為 null，判空是死碼。
        //    （上游那一版寫了 `uiState != null`，那一行在我方這份 CS pin 上不會有任何作用。）
        return UIState.Instance()->IsUnlockLinkUnlockedOrQuestCompleted(unlockLink, 0);
    }

    // 「這個職業學會工程管理了嗎」的兩套判定並跑。
    //
    // 舊判定（**目前仍是權威**）：GetActionStatus 回 574 或 586 就算已解鎖。
    //   那兩個是「你現在又沒在製作」這一類的狀態碼 —— 也就是說「不能用的唯一理由是時機不對」
    //   ⇒ 技能本身是學過的；沒學過會回別的碼。
    //   ⚠️ 它依賴 Player.IsBusy 為 false（忙碌時狀態碼會變成別的東西），
    //      所以下面那個提前 return **不能拿掉**（上游那一版把它刪了）。
    //   ⚠️ 574／586 這兩個數字是照國際服寫死的，台服對不對從來沒有被證明過。
    //
    // 新判定（cycleapple `65a5806`）：UIState.IsUnlockLinkUnlockedOrQuestCompleted(Action.UnlockLink)。
    //   不吃寫死的狀態碼，也與忙碌狀態無關。
    //   ✅ 台服離線驗過：exd-tc/7.20 的 Action 4574~4581 八列全在、名稱都是「掌握」、
    //      UnlockLink 全部非 0（67969~68153）⇒ 這條路在台服有真資料可用。
    //   ✅ CS 特徵碼離線驗過：IsUnlockLinkUnlockedOrQuestCompleted 的
    //      `E8 ?? ?? ?? ?? 84 C0 74 A2` 在台服執行檔 .text 上**唯一命中**（不是歧義也不是斷裂）。
    //
    // 🔑 這一版**刻意還不切換**：兩套一起算，只在**不一致**時印一行 Information
    //    （使用者跑 LogLevel 2，Debug／Verbose 收不到）。
    //    直接切過去的風險是——新判定若在台服也不對，失敗形式是「以為沒學會工程管理」，
    //    製作流程就靜默地少一個技能，不會有任何錯誤訊息，跟「本來就沒學」分不出來。
    //    等實機跑過一輪、log 裡沒有不一致（或確認新的才對），再把權威換成新判定並移掉舊的。
    public static unsafe void UpdateHasManip()
    {
        if (Player.IsBusy)
            return;

        foreach (var jobId in CosmicHelper.CrafterJobList)
        {
            if (ManipClassInfo.TryGetValue(jobId, out var info))
            {
                var byActionStatus = ActionManager.Instance()->GetActionStatus(ActionType.Action, info.ActionId, checkRecastActive: false, checkCastingActive: false) is 574 or 586;
                info.HasUnlocked = byActionStatus;

                var byUnlockLink = IsManipUnlockedByUnlockLink(info.ActionId);
                if (byUnlockLink is { } viaLink && viaLink != byActionStatus)
                {
                    // 節流 key 帶 jobId：八個職業各自獨立，共用一個 key 會讓其中七個永遠印不出來。
                    if (EzThrottler.Throttle($"ICE: manip unlock mismatch {jobId}", 60000))
                        IceLogging.Info(
                            $"工程管理解鎖判定不一致：職業 {jobId}（技能 {info.ActionId}）—— " +
                            $"GetActionStatus 判定 {byActionStatus}、UnlockLink 判定 {viaLink}。" +
                            $"目前採用 GetActionStatus 的結果。請把這一行回報給開發者。",
                            "[Manip Unlock Check]");
                }
            }
        }
    }
}
