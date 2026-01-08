using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System.Runtime.InteropServices;
using static FFXIVClientStructs.FFXIV.Client.Game.Character.ActionEffectHandler;

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

        internal const string AoAoVfxPath = "vfx/common/eff/ev_energydrink_01x_30s.avfx";
        private delegate void CreateResultVfxDelegate(ActionEffectHandler* actionHandler, Character* cast, Character* target, uint action, Effect* result);
        [Signature("48 85 D2 0F 84 ?? ?? ?? ?? 53 55 57", DetourName = nameof(CreateResultVfxDetour))]
        private Hook<CreateResultVfxDelegate> CreateResultVfxHook;

        private void CreateResultVfxDetour(ActionEffectHandler* actionHandler, Character* cast, Character* target, uint action, Effect* result)
        {
            if (result->Type == 14 && result->Value == 49)
            {
                if (((nint)cast != 0 || IsShowInCarema(cast)) && ((nint)target != 0 || IsShowInCarema(target)))
                {
                    CreateVfx?.Invoke(AoAoVfxPath, &cast->GameObject, &target->GameObject, -1, (char)0, 0, (char)0);
                }
            }
            else
            {
                CreateResultVfxHook.Original(actionHandler, cast, target, action, result);
            }

        }

        private delegate IntPtr CreateVfxDelegate(string path, GameObject* cast, GameObject* target, float speed, char a5, ushort a6, char a7);
        private CreateVfxDelegate CreateVfx;

        private delegate bool IsShowInCaremaDelegate(Character* chara);
        private IsShowInCaremaDelegate IsShowInCarema;

        private ResourceLoader ResourceLoader;

        public Plugin()
        {
            GameInteropProvider.InitializeFromAttributes(this);
            ResourceLoader = new ResourceLoader();
            ResourceLoader.AddReplace(AoAoVfxPath, Path.Combine(PluginInterface.AssemblyLocation.Directory!.FullName, "ev_energydrink_01x_30s.avfx"));
            this.CreateVfx = Marshal.GetDelegateForFunctionPointer<CreateVfxDelegate>(SigScanner.ScanText("E8 ?? ?? ?? ?? 48 8B D8 48 85 C0 74 27 B2 01"));
            this.IsShowInCarema = Marshal.GetDelegateForFunctionPointer<IsShowInCaremaDelegate>(SigScanner.ScanText("40 53 48 83 EC 20 48 8B 01 48 8B D9 FF 50 ?? 83 F8 08 75 ?? 0F B7 83"));
            CreateResultVfxHook?.Enable();
        }

        public void Dispose()
        {
            CreateResultVfxHook?.Dispose();
            ResourceLoader?.Dispose();
        }
    }
}
