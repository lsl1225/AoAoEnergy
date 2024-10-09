using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System.Runtime.InteropServices;
using Lumina;
using Lumina.Excel.GeneratedSheets;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Penumbra.Api.IpcSubscribers.Legacy;
using Penumbra.Api.Enums;
using System.Diagnostics;
using System.Reflection;

namespace AoAoEnergy
{

    public unsafe class Plugin : IDalamudPlugin
    {
        public string Name => "AoAoEnergy";

        [PluginService]
        internal static IDalamudPluginInterface PluginInterface { get; set; }
        [PluginService]
        internal static ISigScanner Scanner { get; set; }
        [PluginService]
        internal static ICommandManager CommandManager { get; set; }
        [PluginService]
        internal static IClientState ClientState { get; set; }
        [PluginService]
        internal static IChatGui ChatGui { get; set; }
        [PluginService]
        internal static IDataManager DataManager { get; set; }
        [PluginService]
        internal static IGameInteropProvider GameInteropProvider { get; set; }
        [PluginService]
        internal static IFramework Framework { get; set; }
        [PluginService]
        internal static IPluginLog PluginLog { get; set; }
        [PluginService]
        internal static ISigScanner SigScanner { get; set; }
        //private delegate IntPtr GetStatusDataDelegate(uint index);
        //[Signature("E8 ?? ?? ?? ?? 0F B7 37", DetourName = nameof(GetStatusDataDetour))]
        //private Hook<GetStatusDataDelegate> GetActionDataHook;
        //private static object GetStatusDataDetour()
        //{
        //    throw new NotImplementedException();
        //}

        //private delegate IntPtr GetStatusHitEffectDataDelegate(uint index);
        //[Signature("E8 ?? ?? ?? ?? 48 85 C0 74 5D 48 8B 1F ", DetourName = nameof(GetStatusHitEffectDataDetour))]
        //private Hook<GetStatusHitEffectDataDelegate> GetStatusHitEffectDataHook;
        //private static object GetStatusHitEffectDataDetour()
        //{
        //    throw new NotImplementedException();
        //}

        [StructLayout(LayoutKind.Explicit, Size = 0x8)]
        struct ActionResult
        {
            [FieldOffset(0x0)] public byte Type;
            [FieldOffset(0x1)] public byte Arg0;
            [FieldOffset(0x2)] public byte Arg1;
            [FieldOffset(0x3)] public byte Arg2;
            [FieldOffset(0x4)] public byte Arg3;
            [FieldOffset(0x5)] public byte Arg4;
            [FieldOffset(0x6)] public UInt16 Value;
        }

        private delegate void CreateResultVfxDelegate(IntPtr actionResult, Character* cast, Character* target, uint action, ActionResult* result);
        [Signature("48 85 D2 0F 84 ?? ?? ?? ?? 53 55 57", DetourName = nameof(CreateResultVfxDetour))]
        private Hook<CreateResultVfxDelegate> CreateResultVfxHook;
        private void CreateResultVfxDetour(IntPtr a, Character* cast, Character* target, uint action, ActionResult* result)
        {
#if DEBUG
            var status = DataManager.GetExcelSheet<Lumina.Excel.GeneratedSheets.Status>();
            PluginLog.Debug($"{cast->NameString} {target->NameString} {result->Type} {status.GetRow(result->Value).Name}");
#endif
            //PluginLog.Debug($"{cast->NameString} {target->NameString} {result->Type} {result->Value} {IsShowInCarema(cast)}");
            if (result->Type == 14 && result->Value == 49)
            {
                if (((nint)cast != 0 || IsShowInCarema(cast))
                 && ((nint)target != 0 || IsShowInCarema(target)))
                CreateVfx?.Invoke("vfx/common/eff/ev_energydrink_01x_15s.avfx", &cast->GameObject, &target->GameObject, -1, (char)0, 0, (char)0);
            }
            else
            {
                CreateResultVfxHook.Original(a, cast, target, action, result);
            }

        }

        private delegate IntPtr CreateVfxDelegate(string path, GameObject* cast, GameObject* target, float speed, char a5, ushort a6, char a7);
        private CreateVfxDelegate CreateVfx;

        private delegate bool IsShowInCaremaDelegate(Character* chara);
        private IsShowInCaremaDelegate IsShowInCarema;

        private PenumbraService PenumbraService;

        public Plugin()
        {
            GameInteropProvider.InitializeFromAttributes(this);
            this.CreateVfx = Marshal.GetDelegateForFunctionPointer<CreateVfxDelegate>(SigScanner.ScanText("E8 ?? ?? ?? ?? 48 8B D8 48 85 C0 74 27 B2 01"));
            this.IsShowInCarema = Marshal.GetDelegateForFunctionPointer<IsShowInCaremaDelegate>(SigScanner.ScanText("40 53 48 83 EC 20 48 8B 01 48 8B D9 FF 50 ?? 83 F8 08 75 ?? 0F B7 83"));
            CreateResultVfxHook?.Enable();
            InitMod();
        }

        public void InitMod()
        {
            Task.Run(() =>
            {
                Thread.Sleep(1000);
                PenumbraService = new PenumbraService(PluginInterface);
                Thread.Sleep(500);
                var collection = PenumbraService._currentCollection!.Invoke(ApiCollectionType.Current);
                var (ec, modState) = PenumbraService._getCurrentSettings!.Invoke(collection!.Value.Id, this.Name);
                if (ec == PenumbraApiEc.ModMissing)
                {
                    PluginLog.Info($"Installing AoAoEnergy mod");
                    var modPath = Path.Combine(PluginInterface.AssemblyLocation.Directory!.FullName, "AoAoEnergy.pmp");
                    PluginLog.Debug($"{modPath}");
                    ec = PenumbraService._installMod!.Invoke(modPath);
                    if (ec is not PenumbraApiEc.Success)
                        PluginLog.Error("Fail to install mod.");
                }
                Thread.Sleep(1000);
                (ec, modState) = PenumbraService._getCurrentSettings!.Invoke(collection!.Value.Id, this.Name);
                if (modState!.Value.Item1 == false)
                {
                    PluginLog.Info($"Enabling AoAoEnergy mod");
                    PluginLog.Debug($"{collection!.Value.Name}");
                    var modDir = PenumbraService._getModDir!.Invoke();
                    ec = PenumbraService._setMod!.Invoke(collection.Value.Id, modDir, true, modName: this.Name);
                    if (ec is not PenumbraApiEc.Success)
                        PluginLog.Error("Fail to enable mod.");
                };
            });
        }

        public void Dispose()
        {
            //throw new NotImplementedException();
            CreateResultVfxHook?.Dispose();
            PenumbraService?.Dispose();
        }
    }
}
