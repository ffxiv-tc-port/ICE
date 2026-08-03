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
