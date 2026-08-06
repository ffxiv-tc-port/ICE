using Dalamud.Game.Gui.ContextMenu;
using ICE.Utilities.Cosmic_Helper;
using System.Text;

namespace ICE.Utilities.MechaOps;

/// <summary>
/// 機甲行動的右鍵選單項。
///
/// 🔴🔴 <b>零自動化</b>：這裡的每一項都只做「顯示」或「複製文字」——
/// 不施放技能、不走位、不報名、不選目標、不送任何封包。
/// 設計上也刻意不留自動化的鉤子：唯一被寫入的狀態是
/// <see cref="MechaObjectiveTracker"/> 裡那組**只存在記憶體裡的物件 id**。
///
/// ⚠️ <b>用 Dalamud 既有途徑，不猜原生入口</b>：走 <see cref="IContextMenu.OnMenuOpened"/>。
/// 📌 <b>不同視窗的 agent 不一樣</b>（部隊置物櫃是 <c>AgentContext</c>、道具選單是
/// <c>AgentInventoryContext</c>），所以這裡**明確檢查</b> <see cref="ContextMenuType"/>，
/// 而不是假設 <c>args.Target</c> 一定是 <see cref="MenuTargetDefault"/>。
/// Dalamud 只有在 <see cref="ContextMenuType.Default"/> 時才會建出 <c>MenuTargetDefault</c>，
/// 型別不對就直接不加任何項目。
///
/// 出現條件收得很緊，避免在整個遊戲裡到處長出選單項：
///  1. <c>C.ShowMechaContextMenu</c> 開著；
///  2. 人在宇宙區域（<c>PlayerHelper.IsInCosmicZone()</c>）；
///  3. 選單型別是 Default；
///  4. 有真的指到一個 ObjectTable 裡查得到的物件（釘選項）。
/// </summary>
internal static class MechaContextMenu
{
    /// <summary>遊戲用來表示「沒有目標」的哨兵值。</summary>
    private const ulong NoTarget = 0xE000_0000UL;

    private static bool enabled;

    public static void Enable()
    {
        if (enabled)
            return;
        Svc.ContextMenu.OnMenuOpened += OnMenuOpened;
        enabled = true;
    }

    public static void Disable()
    {
        if (!enabled)
            return;
        // 🔴 卸載順序沒有保證，退訂一律包起來。
        GenericHelpers.Safe(() => Svc.ContextMenu.OnMenuOpened -= OnMenuOpened);
        enabled = false;
    }

    private static void OnMenuOpened(IMenuOpenedArgs args)
    {
        try
        {
            OnMenuOpenedInner(args);
        }
        catch (Exception ex)
        {
            if (EzThrottler.Throttle("MechaContextMenuError", 60_000))
                IceLogging.Error($"機甲右鍵選單失敗：{ex}", "[MechaOps]");
        }
    }

    private static void OnMenuOpenedInner(IMenuOpenedArgs args)
    {
        if (!C.ShowMechaContextMenu)
            return;
        if (!PlayerHelper.IsInCosmicZone())
            return;

        // 📌 只處理預設選單。道具選單走的是 AgentInventoryContext，
        //    在那邊 args.Target 根本不是 MenuTargetDefault。
        if (args.MenuType != ContextMenuType.Default)
            return;
        if (args.Target is not MenuTargetDefault def)
            return;

        AddPinItem(args, def);
        AddCopyDiagnosticsItem(args);
    }

    /// <summary>
    /// 「標示 / 取消標示」——把這個物件加進目的指示疊加層。純顯示。
    ///
    /// 🔴 只記 <c>GameObjectId</c>（8 bytes 的純量），不留 <c>IGameObject</c>、
    /// 更不留 <c>Address</c>；繪製端每一幀都重新去 ObjectTable 查一次。
    /// 物件消失的那一幀就只是「這一幀不畫」，不會有懸空指標。
    /// </summary>
    private static void AddPinItem(IMenuOpenedArgs args, MenuTargetDefault def)
    {
        var id = def.TargetObjectId;
        if (id == 0 || id == NoTarget)
            return;

        // 必須真的在 ObjectTable 裡查得到——這一項的整個意義就是「畫出它在哪」。
        var obj = def.TargetObject;
        if (obj == null)
            return;

        var pinned = MechaObjectiveTracker.IsPinned(id);

        args.AddMenuItem(new MenuItem
        {
            Name = pinned
                ? "ICE: Unmark for mecha overlay".Loc()
                : "ICE: Mark for mecha overlay".Loc(),
            PrefixChar = 'I',
            PrefixColor = 539,
            Priority = 100,
            OnClicked = _ =>
            {
                if (MechaObjectiveTracker.IsPinned(id))
                    MechaObjectiveTracker.Unpin(id);
                else
                    MechaObjectiveTracker.Pin(id);
            },
        });

        if (MechaObjectiveTracker.PinnedCount > 0)
        {
            args.AddMenuItem(new MenuItem
            {
                Name = "ICE: Clear all mecha marks".Loc(),
                PrefixChar = 'I',
                PrefixColor = 539,
                Priority = 101,
                OnClicked = _ => MechaObjectiveTracker.ClearPins(),
            });
        }
    }

    /// <summary>
    /// 「複製機甲事件診斷」——把狀態視窗 tooltip 裡那份原始值倒進剪貼簿。
    ///
    /// 🔴 <b>刻意不含任何角色名</b>：這份文字的用途就是貼給別人看，
    /// 所以只有旗標、時間戳、進度數字與標記座標，一個名字都沒有。
    /// （其他玩家的角色名處理見 <see cref="MechaPrivacy"/>。）
    /// </summary>
    private static void AddCopyDiagnosticsItem(IMenuOpenedArgs args)
    {
        if (!MechaOpsMonitor.EventFlagsValid)
            return;

        args.AddMenuItem(new MenuItem
        {
            Name = "ICE: Copy mecha event info".Loc(),
            PrefixChar = 'I',
            PrefixColor = 539,
            Priority = 102,
            OnClicked = _ =>
            {
                // ⚠️ **不要在這裡直接呼叫 ImGui.SetClipboardText。**
                //    這個 callback 是遊戲開右鍵選單時觸發的，跑在 ImGui frame **之外**；
                //    ImGui 的 API 只保證在 frame 內可用。文字在這裡就先組好
                //    （資料來源都是我們自己的快照），實際寫剪貼簿交給 UiBuilder.Draw
                //    那一側的 FlushPendingCopy()。
                pendingClipboard = BuildDiagnostics();
            },
        });
    }

    /// <summary>右鍵選單按下後待寫入剪貼簿的文字。<c>null</c> ＝ 沒有待處理的。</summary>
    private static string? pendingClipboard;

    /// <summary>
    /// 在 ImGui frame 內把待處理的文字寫進剪貼簿。
    /// 由 <see cref="MechaAoeOverlay.Draw"/> 呼叫——那是掛在 <c>UiBuilder.Draw</c> 上的，
    /// 保證在 frame 內，而且無論疊加層有沒有東西要畫都會被呼叫到。
    /// </summary>
    public static void FlushPendingCopy()
    {
        var text = pendingClipboard;
        if (text == null)
            return;
        pendingClipboard = null;

        ImGui.SetClipboardText(text);
        Notify.Success("Mecha event info copied".Loc());
    }

    private static string BuildDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine("ICE mecha event diagnostics");
        sb.AppendLine($"  Territory = {Svc.ClientState.TerritoryType}");
        sb.AppendLine($"  ModuleFlags = 0x{(uint)MechaOpsMonitor.EventFlags:X} (valid={MechaOpsMonitor.EventFlagsValid})");

        var d = MechaOpsMonitor.EventDetail;
        if (d == null)
        {
            sb.AppendLine("  EventDetail = null (pointer range check did not pass, or no current event)");
        }
        else
        {
            sb.AppendLine($"  DataRowId = {d.DataRowId}");
            sb.AppendLine($"  EventFlags = 0x{(uint)d.Flags:X}");
            sb.AppendLine($"  EventProgress = {d.Progress} / {d.ProgressMax}");
            sb.AppendLine($"  PersonalProgress = {d.PersonalProgress} / {d.PersonalProgressMax}");
            sb.AppendLine($"  Contribution = {d.Contribution}");
            sb.AppendLine($"  EventStart = {d.EventStart}  EventEnd = {d.EventEnd}");
            sb.AppendLine($"  PilotRegistration = {d.RegistrationStart} .. {d.RegistrationEnd}");
            sb.AppendLine($"  TeleportStart = {d.TeleportStart}");
            sb.AppendLine($"  ServerTimeNow = {d.ServerTimeNow} (sampled {d.ServerTimeAtSample})");
        }

        sb.AppendLine($"  Objectives: markers={MechaObjectiveTracker.MarkerCount} " +
                      $"confirmed={MechaObjectiveTracker.ConfirmedCount} " +
                      $"source={(MechaObjectiveTracker.SourceNote.Length == 0 ? "vector" : MechaObjectiveTracker.SourceNote)} " +
                      $"pins={MechaObjectiveTracker.PinnedCount}");
        foreach (var m in MechaObjectiveTracker.Markers)
        {
            sb.AppendLine($"    slot{m.Slot:00} layout={m.LayoutId} type={m.MarkerType} icon={m.MarkerIcon} " +
                          $"mapIcon={m.MapIconId} pos=({m.Position.X:F1}, {m.Position.Y:F1}, {m.Position.Z:F1}) " +
                          $"r={m.Radius:F1} hidden={m.Hidden}");
        }

        // 技能候選只印 id 與名稱（都是遊戲內容，不是個人資訊）。
        foreach (var c in MechaOpsMonitor.ActiveCandidates)
            sb.AppendLine($"    action {c.ActionId} {c.Name} shape={c.Shape.Kind} {c.Shape.Primary:F0}");

        return sb.ToString();
    }
}
