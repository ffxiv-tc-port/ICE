using ECommons.Automation.NeoTaskManager;
using ECommons.Configuration;
using ECommons.GameHelpers;
using ICE.Config;
using ICE.IPC;
using ICE.Ui;
using ICE.Ui.MainUi;
using ICE.Utilities.Cosmic_Helper;
using ICE.Utilities.GatheringHelper;
using ICE.Utilities.MechaOps;
using Pictomancy;
using System.Collections.Generic;
using static ICE.Utilities.CosmicHelper;

namespace ICE;

public sealed partial class ICE : IDalamudPlugin
{
    public static string Name => "ICE";

    internal static ICE P = null!;
    private readonly Configuration Config;
    private static MissionConfigs missionConfigs;
    public MissionTimer MissionTimer { get; private set; }

    public static Configuration OldConfig => P.Config;
    public static MissionConfigs C => missionConfigs ??= LoadConfig<MissionConfigs>();

    // Yaml Config Loaders. For both loading a yaml in the config folder, and for embedded
    private static T LoadConfig<T>() where T : IYamlConfig, new()
    {
        var path = typeof(T).GetProperty("ConfigPath")!.GetValue(null)!.ToString()!;
        var config = YamlConfig.Load<T>(path);

        if (config == null)
        {
            PluginLog.Warning($"[{typeof(T).Name}] Config was null. Creating new default.");
            config = new T();
            YamlConfig.SaveSync(config, path); // Use synchronous save for initialization
        }

        PluginLog.Information($"[{typeof(T).Name}] Loaded from {path}");
        return config;
    }

    // Window's that I use, base window to the settings... need these to actually show shit 
    internal WindowSystem windowSystem;
    internal MainWindow mainWindow;
    internal OverlayWindow overlayWindow;
    internal DebugWindow debugWindow;
    internal InfoWindow infoWindow;
    internal MechaOpsWindow mechaOpsWindow;

    // Taskmanager from Ecommons
    internal TaskManager TaskManager;

    // Internal IPC's that I use for... well plugins. 
    internal LifestreamIPC Lifestream;
    internal NavmeshIPC Navmesh;
    internal PandoraIPC Pandora;
    internal ArtisanIPC Artisan;
    internal VislandIPC Visland;
    internal AutoHookIPC AutoHook;
    internal IceCosmicExplorationIPC IceIpc;

    public ICE(IDalamudPluginInterface pi)
    {
        P = this;
        ECommonsMain.Init(pi, P, Module.DalamudReflector, ECommons.Module.ObjectFunctions);
        // 讓「呼叫了對方沒有的 IPC 方法」不再完全靜默。
        // 訂閱越早越好：事件只在 IPC **呼叫**當下才被查閱，在這裡訂閱就涵蓋往後所有呼叫。
        EzIpcFailureLog.Enable();
        ECommons.LanguageHelpers.Localization.Init("ChineseTraditional");
        PictoService.Initialize(pi);

        EzConfig.Migrate<Configuration>();
        Config = EzConfig.Init<Configuration>();

        //IPC's that are used
        Lifestream = new();
        Navmesh = new();
        Pandora = new();
        Artisan = new();
        Visland = new();
        AutoHook = new();
        IceIpc = new();

        // all the windows
        windowSystem = new();
        mainWindow = new();
        overlayWindow = new();
        debugWindow = new();
        infoWindow = new();
        mechaOpsWindow = new();

        // timer stuff
        MissionTimer = new MissionTimer();

        EzCmd.Add("/icecosmic", OnCommand, """
            Open plugin interface
            /ice help - shows all commands
            /ice clear - removes all missions
            /ice stop - stops ICE
            /ice start - Starts ICE
            /ice add | remove | toggle | only
            /ice flag [id] - Opens the map and marks where the area of gathering is.
            """.Loc());
        EzCmd.Add("/ice", OnCommand);
        EzCmd.Add("/IceCosmic", OnCommand);

        // 把使用者設定的日誌寫入門檻套進 IceLogging。
        // 📌 預設值是 Verbose（全部寫入）＝現行行為；setter 會把值夾在 Info 以下，
        //    所以不論設定檔被改成什麼，Information 以上的診斷都關不掉。
        IceLogging.MinimumLevel = C.LogMinimumLevel;

        Init();
        Svc.Framework.Update += Tick;

        // 同步上游：離開宇宙探索區時自動關閉浮動視窗（見 OnTerritoryChange）。
        // 🔴 本艦隊 Dalamud pin 的 TerritoryChanged 委派是 Action<ushort>（上游用 uint 會對不上型別）。
        Svc.ClientState.TerritoryChanged += OnTerritoryChange;

        // 🔴 任務逾時目前在 log 裡查不到「是哪一步逾時」：ECommons 丟的
        //    TaskTimeoutException 訊息是空的（只剩 e.LogWarning() 的堆疊），
        //    唯一帶任務名稱的那行在 TaskManager.Tick 裡被 ShowDebug = false 關掉，
        //    而且就算打開也是 Debug 級 —— 使用者跑 LogLevel 2 收不到。
        // ⚠️ 事件一定要在 new TaskManager(...) **之前**掛好：建構子做的是
        //    `new TaskManagerConfiguration{...}.With(defaultConfiguration)`，
        //    事件被複製進另一個物件，事後再對這個區域變數指派完全沒有效果。
        var taskManagerConfiguration = new TaskManagerConfiguration(showDebug: false);
        taskManagerConfiguration.OnTaskTimeout = (TaskManagerTask task, ref long remainingTimeMS) =>
            IceLogging.Error($"任務逾時：{task.Name}@{task.Location}（remainingTimeMS = {remainingTimeMS}）", "[Task Manager]");
        TaskManager = new(taskManagerConfiguration);
        Svc.PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        // 機甲行動技能範圍（P1）：PctDrawList 只能在 ImGui frame 內用，必須掛 UiBuilder.Draw。
        Svc.PluginInterface.UiBuilder.Draw += MechaAoeOverlay.Draw;
        // 機甲行動右鍵選單：純顯示項目，出現條件收在宇宙區域內（見 MechaContextMenu）。
        MechaContextMenu.Enable();
        Svc.PluginInterface.UiBuilder.OpenMainUi += () =>
        {
            mainWindow.IsOpen = true;
        };
        Svc.PluginInterface.UiBuilder.OpenConfigUi += () =>
        {
            mainWindow.IsOpen = true;
            // 🔴 這個字串必須是 MainWindow.MainBody() 那個 switch 裡真的存在的 case ——
            //    打錯不會編譯失敗，只會讓「外掛清單的齒輪鈕」開出一片空白頁。
            //    選「設定」是因為它是唯一保證不會被 C.Show_Page_* 藏起來的一級項，
            //    所以無論使用者把什麼藏掉了，這個入口都一定落在看得到東西的地方。
            SelectableSidebar.currentSelection = "page_Settings";
        };
        DictionaryCreation();
        // 要放在 DictionaryCreation() 之後：它依賴 ExcelHelper 的 sheet 已經取好。
        GatheringUtil.BackfillAmountRequiredFromSheet();
        Task_Gamba.EnsureGambaWeightsInitialized();
        ConfigMigrator.UpdateConfigMissionList();
        ConfigMigrator.MigrateConfigv1();
        ConfigMigrator.CheckMissions();
        GatheringUtil.UpdateCriticalWeather();
        TestLoadRoutes();

        // ⚠️ 一定要放在最後：CriticalLocations 由上一行的 UpdateCriticalWeather() 填，
        //    採集路線由 TestLoadRoutes() 觸發載入，提早呼叫會量到「全部都對得上」的假陰性。
        ReportHardcodedTableCoverage();
    }

    private static void Init()
    {
        ExcelHelper.Init();
        ConsumableInfo.Init();
    }

    private void Tick(object _)
    {
        if (Player.Available)
        {
            // 每幀先把「已經從 addon 清單消失」的視窗按壓紀錄清掉，讓守衛觀察得到「消失」那幾幀
            //（呼叫端多半被 500ms 節流擋著，光靠呼叫時才掃會漏看）。表空時第一行就回去。
            AddonPressGuard.Tick();
            PlayerHandlers.Tick();
            if (SchedulerMain.State != IceState.Idle)
                SchedulerMain.Tick();
            WeatherForecastHandler.Tick();
            // 機甲行動偵察（P0）＋繪製快照：遊戲結構只在 Framework 執行緒讀。
            MechaOpsMonitor.Tick();
            // 機甲事件錄製（debug，/ice d）。🔑 一定要排在 monitor 之後——它只讀 monitor
            // 已經發布的快照，排前面會慢一幀。錄製沒開時第一行就 return。
            MechaEventRecorder.Tick();
        }
        else
        {
            if (SchedulerMain.State != IceState.Idle)
                PlayerHandlers.DisablePlugin();
        }
        GenericManager.Tick();
        TextAdvancedManager.Tick();
        YesAlreadyManager.Tick();
    }

    // 同步上游：離開宇宙探索區就把浮動視窗關掉，避免離開後視窗殘留在畫面上。
    // ⚠️ 回呼裡不保存任何原生指標；每次呼叫都用 IsInCosmicZone() 重判（它只讀 TerritoryType）。
    // 📌 開窗仍交給 PlayerHandlers.Tick 的既有慣例（含 UsingSupportedJob 與 C.ShowOverlay 條件），
    //    這裡只補上游新增的「離開就關」，不重複開窗邏輯以免改動既有開窗條件。
    private void OnTerritoryChange(ushort territoryId)
    {
        if (!PlayerHelper.IsInCosmicZone() && P.overlayWindow.IsOpen)
            P.overlayWindow.IsOpen = false;
    }

    public void Dispose()
    {
        GenericHelpers.Safe(() => Svc.Framework.Update -= Tick);
        GenericHelpers.Safe(() => Svc.ClientState.TerritoryChanged -= OnTerritoryChange);
        GenericHelpers.Safe(() => Svc.PluginInterface.UiBuilder.Draw -= windowSystem.Draw);
        GenericHelpers.Safe(() => Svc.PluginInterface.UiBuilder.Draw -= MechaAoeOverlay.Draw);
        GenericHelpers.Safe(MechaContextMenu.Disable);
        GenericHelpers.Safe(TextAdvancedManager.UnlockTA);
        GenericHelpers.Safe(YesAlreadyManager.Unlock);
        // 守衛的 AddonLifecycle 監聽器：本 pin 卸載時會自動拆，但仍主動拆乾淨、不留指向本組件的委派。
        GenericHelpers.Safe(AddonPressGuard.ForceTeardown);
        GenericHelpers.Safe(EzIpcFailureLog.Disable);
        ECommonsMain.Dispose();
        PictoService.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        var subcommands = args.Split(' ');

        if (subcommands.Length == 0 || args == "")
        {
            mainWindow.IsOpen = !mainWindow.IsOpen;
            return;
        }

        var firstArg = subcommands[0];

        if (firstArg.ToLower() == "d" || firstArg.ToLower() == "debug")
        {
            debugWindow.IsOpen = true;
            return;
        }
        else if (firstArg.ToLower() == "i")
        {
            infoWindow.IsOpen = true;
            return;
        }
        else if (firstArg.ToLower() == "s" || firstArg.ToLower() == "settings")
        {
            mainWindow.IsOpen = true;
            // 🔴 同上：必須對得上 MainBody() 的 case，打錯是空白頁不是編譯錯誤。
            SelectableSidebar.currentSelection = "page_Settings";
            return;
        }
        else if (firstArg.ToLower() == "clear")
        {
            foreach (var mission in C.MissionConfig)
            {
                mission.Value.Enabled = false;
            }
            C.Save();
        }
        else if (firstArg.ToLower() == "stop")
        {
            SchedulerMain.DisablePlugin();
        }
        else if (firstArg.ToLower() == "start")
        {
            SchedulerMain.EnablePlugin();
        }
        else if (firstArg.ToLower() == "add")
        {
            uint[] ids = [.. subcommands.Skip(1).Select(uint.Parse)];
            var idSet = new HashSet<uint>(ids);
            if (ids.Length == 0) return;

            foreach (var id in idSet)
            {
                if (C.MissionConfig.TryGetValue(id, out var mission))
                {
                    mission.Enabled = true;
                }
            }
            C.Save();
        }
        else if (firstArg.ToLower() == "remove")
        {
            uint[] ids = [.. subcommands.Skip(1).Select(uint.Parse)];
            var idSet = new HashSet<uint>(ids);
            if (ids.Length == 0) return;

            foreach (var id in idSet)
            {
                if (C.MissionConfig.TryGetValue(id, out var mission))
                {
                    mission.Enabled = false;
                }
            }
            C.Save();
        }
        else if (firstArg.ToLower() == "toggle")
        {
            uint[] ids = [.. subcommands.Skip(1).Select(uint.Parse)];
            var idSet = new HashSet<uint>(ids);
            if (ids.Length == 0) return;

            foreach (var id in idSet)
            {
                if (C.MissionConfig.TryGetValue(id, out var mission))
                {
                    mission.Enabled = !mission.Enabled;
                }
            }
            C.Save();
        }
        else if (firstArg.ToLower() == "only")
        {
            uint[] ids = [.. subcommands.Skip(1).Select(uint.Parse)];
            var idSet = new HashSet<uint>(ids);
            if (ids.Length == 0) return;

            foreach (var mission in C.MissionConfig.Where(x => x.Value.Enabled))
            {
                mission.Value.Enabled = false;
            }
            foreach (var id in idSet)
            {
                if (C.MissionConfig.TryGetValue(id, out var mission))
                {
                    mission.Enabled = true;
                }
            }
        }
        else if (firstArg.ToLower() == "flag")
        {
            if (subcommands.Length != 2) return;
            if (!PlayerHelper.IsInCosmicZone()) return;

            int missionId = int.Parse(subcommands[1]);
            var info = SheetMissionDict.FirstOrDefault(mission => mission.Key == missionId);
            if (info.Value == default) return;
            if (info.Value.MarkerId == 0) return;

            Utils.SetGatheringRing(info.Value.TerritoryId, (int)info.Value.MapPosition.X, (int)info.Value.MapPosition.Y, info.Value.Radius, info.Value.Name);
        }
        else if (firstArg.ToLower() == "help")
        {
            string helpMessage = ("- - ICE Commands Help - - \n" +
                                 "/ice help - show all available commands\n" +
                                 "/ice -> opens the main settings\n" +
                                 "/ice s -> opens the settings menu\n" +
                                 " - - - Mission specific - - - \n" +
                                 "/ice stop - Stops ICE\n" +
                                 "/ice start - starts ICE \n" +
                                 "The rest of the commands work by doing a single id/multiple in a row \n" +
                                 "EX. /ice add 10 155 185\n" +
                                 "/ice add (ids) - enables select missions\n" +
                                 "/ice remove (ids) - removes/disables select missions\n" +
                                 "/ice toggle (ids) - toggles select mission ids" +
                                 "/ice only (ids) - makes only select missions enabled" +
                                 "/ice flag (id) - opens the map and flags the mission (if it has one).\n").Loc();
            Svc.Chat.Print(helpMessage);
        }
    }

    public void TestLoadRoutes()
    {
        try
        {
            // Clear cache first to force reload
            GatheringRouteLoader.ClearCache();

            var routes = GatheringRouteLoader.LoadAllRoutes();

            IceLogging.Info($"Successfully loaded {routes.Count} zones with {routes.Sum(x => x.Value.Count)} total routes");

            // Test getting a specific route
            var testRoute = GatheringRouteLoader.GetRoute(1237, new Vector2(-690f, -752f));
            if (testRoute != null)
            {
                IceLogging.Info($"Test route loaded successfully with {testRoute.Count} nodes");
            }
        }
        catch (Exception ex)
        {
            IceLogging.Error($"Failed to load routes: {ex.Message}");
            IceLogging.Error(ex.StackTrace ?? "No stack trace");
        }
    }
}
