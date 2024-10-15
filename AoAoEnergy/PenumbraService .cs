//using Dalamud.Plugin;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//namespace AoAoEnergy
//{
//    public class PenumbraService : IDisposable
//    {
//        public const int RequiredPenumbraBreakingVersion = 5;
//        public const int RequiredPenumbraFeatureVersion = 0;

//        private readonly IDalamudPluginInterface _pluginInterface;

//        public global::Penumbra.Api.IpcSubscribers.GetCollection? _currentCollection;
//        public global::Penumbra.Api.IpcSubscribers.GetCurrentModSettings? _getCurrentSettings;
//        public global::Penumbra.Api.IpcSubscribers.TrySetMod? _setMod;
//        public global::Penumbra.Api.IpcSubscribers.GetModDirectory? _getModDir;
//        public global::Penumbra.Api.IpcSubscribers.InstallMod? _installMod;

//        public bool Available { get; private set; }
//        public int CurrentMajor { get; private set; }
//        public int CurrentMinor { get; private set; }
//        public DateTime AttachTime { get; private set; }

//        public PenumbraService(IDalamudPluginInterface pi)
//        {
//            _pluginInterface = pi;
//            Reattach();
//        }

//        public void Reattach()
//        {
//            try
//            {
//                Unattach();

//                AttachTime = DateTime.UtcNow;
//                try
//                {
//                    (CurrentMajor, CurrentMinor) = new global::Penumbra.Api.IpcSubscribers.ApiVersion(_pluginInterface).Invoke();
//                }
//                catch
//                {
//                    try
//                    {
//                        (CurrentMajor, CurrentMinor) = new global::Penumbra.Api.IpcSubscribers.Legacy.ApiVersions(_pluginInterface).Invoke();
//                    }
//                    catch
//                    {
//                        CurrentMajor = 0;
//                        CurrentMinor = 0;
//                        throw;
//                    }
//                }

//                if (CurrentMajor != RequiredPenumbraBreakingVersion || CurrentMinor < RequiredPenumbraFeatureVersion)
//                    throw new Exception(
//                        $"Invalid Version {CurrentMajor}.{CurrentMinor:D4}, required major Version {RequiredPenumbraBreakingVersion} with feature greater or equal to {RequiredPenumbraFeatureVersion}.");

//                _currentCollection = new global::Penumbra.Api.IpcSubscribers.GetCollection(_pluginInterface);
//                _getCurrentSettings = new global::Penumbra.Api.IpcSubscribers.GetCurrentModSettings(_pluginInterface);
//                _setMod = new global::Penumbra.Api.IpcSubscribers.TrySetMod(_pluginInterface);
//                _getModDir = new global::Penumbra.Api.IpcSubscribers.GetModDirectory(_pluginInterface);
//                _installMod = new global::Penumbra.Api.IpcSubscribers.InstallMod(_pluginInterface);
//                Available = true;
//            }
//            catch (Exception e)
//            {
//                Unattach();
//                Plugin.PluginLog.Debug($"Could not attach to Penumbra:\n{e}");
//            }
//        }

//        /// <summary> Unattach from the currently running Penumbra IPC provider. </summary>
//        public void Unattach()
//        {
//            if (Available)
//            {
//                _currentCollection = null;
//                _getCurrentSettings = null;
//                _setMod = null;
//                Available = false;
//                Plugin.PluginLog.Debug("AoAoEnergy detached from Penumbra.");
//            }
//        }

//        public void Dispose()
//        {
//            Unattach();
//        }
//    }
//}
