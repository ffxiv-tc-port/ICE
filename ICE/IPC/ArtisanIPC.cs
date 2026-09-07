using ECommons.EzIpcManager;

namespace ICE.IPC
{
    public class ArtisanIPC
    {
        public const string Name = "Artisan";
        public ArtisanIPC() => EzIPC.Init(this, Name, SafeWrapper.AnyException);

        [EzIPC] public Func<bool> IsBusy;
        [EzIPC] public Func<bool> GetEnduranceStatus;
        [EzIPC] public Action<bool> SetEnduranceStatus;
        [EzIPC] public Func<bool> IsListRunning;
        [EzIPC] public Func<bool> IsListPaused;
        [EzIPC] public Action<bool> SetListPause;
        [EzIPC] public Func<bool> GetStopRequest;
        [EzIPC] public Action<bool> SetStopRequest;
        [EzIPC] public Action<ushort, int> CraftItem;

        // 臨時求解器（宇宙製作用）。Artisan 端的臨時設定活在它自己的記憶體裡、
        // 不寫進使用者的設定檔，所以 ICE 停止／任務結束／卸載時一定要還回去。
        // 🔴 舊版 Artisan 沒有前兩個端點：SafeWrapper.AnyException 會吞掉
        //    IpcNotReadyError 並回傳 default（string[] -> null、bool -> false），
        //    呼叫端 CosmicSolverOverride 就是靠這個分辨「端點不在」與「求解器不可用」。
        [EzIPC] public Func<uint, string[]> GetAvailableSolverTypes;
        [EzIPC] public Func<uint, string, bool> SetTemporarySolverByType;
        [EzIPC] public Action<uint> ClearTemporaryRecipeSettings;

        /// <summary>Artisan 專家求解器的<b>定義類別</b>全名。</summary>
        /// <remarks>
        /// 🔴 這是 <c>ISolverDefinition</c> 的實作類別，不是 <c>Solver</c> 的實作類別 ——
        /// Artisan 的 <c>RecipeConfig.SolverType</c> 存的就是 <c>Desc.Def.GetType().FullName</c>
        /// （見 Artisan <c>IPC/IPC.cs</c> 的 <c>SetTemporarySolverCore</c>）。
        /// </remarks>
        public const string ExpertSolverTypeName = "Artisan.CraftingLogic.Solvers.ExpertSolverDefinition";

        /// <summary>Artisan Raphael 求解器的<b>定義類別</b>全名。</summary>
        /// <remarks>
        /// ⚠️ 類別名的 <c>Defintion</c> 是<b>上游原本就有的拼字錯誤</b>，不要「訂正」成
        /// <c>Definition</c> —— 這個字串要跟 <c>GetType().FullName</c> 逐字相同，
        /// 改了就永遠對不上，而失敗形式只是「照原樣製作」＋一行記錄，不會報錯。
        /// </remarks>
        public const string RaphaelSolverTypeName = "Artisan.CraftingLogic.Solvers.RaphaelSolverDefintion";

        // ⚠️ 這裡原本還有一個 [EzIPC] Action<ushort, uint, uint, uint, uint> AssignRecipie，
        //    以及包裝它的 AssignArtisanRecipe()。已於 2026-08-03 移除，因為那是死碼：
        //    Artisan 用的是純原生 IPC（Artisan/IPC/IPC.cs），總共只註冊 9 個方法
        //    （IsBusy / GetEnduranceStatus / SetEnduranceStatus / IsListRunning / IsListPaused /
        //     SetListPause / GetStopRequest / SetStopRequest / CraftItem），
        //    從來沒有 AssignRecipie。加上這裡帶的是 SafeWrapper.AnyException，
        //    呼叫下去只會被吞掉、回傳 default，連一行 log 都不會有。
        //    留著一個「宣告了但永遠靜默失敗」的訂閱端，等於留一顆壞掉的安全閥給下一個人踩。
        //    Artisan 若日後真的加了這個 IPC，再依實際簽名重新宣告即可。
    }
}
