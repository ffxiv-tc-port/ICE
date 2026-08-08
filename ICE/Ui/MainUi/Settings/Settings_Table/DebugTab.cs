using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ICE.Ui.MainUi.Settings.Settings_Table
{
    // 🔴 死碼：全 repo 零呼叫端（靜態掃描；本 repo 沒有任何反射式 UI 探索 ——
    //    唯一的反射是 IceLogging 的 StackFrame.GetMethod()，與 UI 無關，所以靜態掃描即權威）。
    //    ⚠️ 與 /ice d 開的 DebugWindow 無關 —— 那個走 Ui/DebugWindow.cs ＋ Ui/DebugWindowTabs/，
    //       是另一套完全不同的東西。不要因為名字像就把兩者當同一個。
    //    保留不刪（使用者裁決：死碼只要確認真的死，不用刪）。
    //
    // 📌 兩件與「這是不是唯一 UI」有關的事實，逐條查過：
    //    ① SequenceMissionPriority / WeatherMissionPriority / TimedMissionPriority 這三個設定鍵
    //       除了 ConfigMigrator 的搬遷賦值以外沒有任何消費端。而且**連這一頁也沒有畫出它們**：
    //       下面的 missionMap / sorted 兩個區域變數建完就沒被用過。
    //       ⇒ 正確說法是「這三個鍵目前沒有任何 UI」，不是「唯一的 UI 在這裡」。
    //    ② ShowDebugGatherInfo 相反：它有真實消費端（Ui/OverlayWindow.cs:89），
    //       但唯一的開關就是本頁第 57 行 ⇒ 這個功能目前**無法從 UI 開啟**。
    //       這是既有狀態，本批不動它。
    internal class DebugTab
    {
        public static void Draw()
        {
            ImGui.Checkbox("Force OOM Main".Loc(), ref SchedulerMain.DebugOOMMain);
            ImGui.Checkbox("Force OOM Sub".Loc(), ref SchedulerMain.DebugOOMSub);
            // ImGui.Checkbox("Legacy Failsafe WKSRecipe Select", ref C.FailsafeRecipeSelect);

            var missionMap = new List<(string name, Func<byte> get, Action<byte> set)>
                {
                    ("Sequence Missions", new Func<byte>(() => C.SequenceMissionPriority), new Action<byte>(v => { C.SequenceMissionPriority = v; C.Save(); })),
                    ("Timed Missions", new Func<byte>(() => C.TimedMissionPriority), new Action<byte>(v => { C.TimedMissionPriority = v; C.Save(); })),
                    ("Weather Missions", new Func<byte>(() => C.WeatherMissionPriority), new Action<byte>(v => { C.WeatherMissionPriority = v; C.Save(); }))
                };

            var sorted = missionMap
                .Select((m, i) => new { Index = i, Name = m.name, Priority = m.get() })
                .OrderBy(m => m.Priority)
                .ToList();

            if (ImGui.Button("Get Sinus Forecast".Loc()))
            {
                List<WeatherForecast> forecast = WeatherForecastHandler.GetTerritoryForecast(1237);
                Func<WeatherForecast, string> formatTime = (forecast) => WeatherForecastHandler.FormatForecastTime(forecast.Time);

                Svc.Chat.Print(new Dalamud.Game.Text.XivChatEntry()
                {
                    Message = $"Sinus Ardorum Weather - {forecast[0].Name}",
                    Type = Dalamud.Game.Text.XivChatType.Echo,
                });
                for (int i = 1; i < forecast.Count; i++)
                {
                    Svc.Chat.Print(new Dalamud.Game.Text.XivChatEntry()
                    {
                        Message = $"{forecast[i].Name} In {formatTime(forecast[i])}",
                        Type = Dalamud.Game.Text.XivChatType.Echo,
                    });
                }
            }

            using (ImRaii.Disabled(!PlayerHelper.IsInCosmicZone()))
            {
                if (ImGui.Button("Refresh Forecast".Loc()))
                {
                    WeatherForecastHandler.GetForecast();
                }
            }
            bool gatherDebug = C.ShowDebugGatherInfo;
            if (ImGui.Checkbox("Show Gather Debug Info".Loc(), ref gatherDebug))
            {
                C.ShowDebugGatherInfo = gatherDebug;
                C.Save();
            }
        }
    }
}
