using ECommons.EzIpcManager;

#nullable disable
namespace ICE.IPC
{
    public class LifestreamIPC
    {
        public const string Name = "Lifestream";
        public const string Repo = "https://raw.githubusercontent.com/ffxiv-tc-port/DalamudPluginsTC/main/repo.json";
        public LifestreamIPC() => EzIPC.Init(this, Name, SafeWrapper.AnyException);

        [EzIPC] public Func<string, bool> AethernetTeleport; //
        [EzIPC] public Func<uint, byte, bool> Teleport;
        [EzIPC] public Func<bool> TeleportToHome;
        [EzIPC] public Func<bool> TeleportToFC;
        [EzIPC] public Func<bool> TeleportToApartment;
        [EzIPC] public Func<bool> IsBusy;
        [EzIPC] public Action<string> ExecuteCommand;
    }
}