using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using Penumbra.String;
using Penumbra.String.Classes;
using System.Runtime.InteropServices;
using System.Text;
using CsHandle = FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;

namespace AoAoEnergy
{
    internal unsafe class ResourceLoader : IDisposable
    {
        [StructLayout(LayoutKind.Explicit)]
        public unsafe struct ResourceHandle
        {
            [StructLayout(LayoutKind.Explicit)]
            public struct DataIndirection
            {
                [FieldOffset(0x00)]
                public void** VTable;

                [FieldOffset(0x10)]
                public byte* DataPtr;

                [FieldOffset(0x28)]
                public ulong DataLength;
            }

            public readonly CiByteString FileName()
                => CiByteString.FromSpanUnsafe(CsHandle.FileName.AsSpan(), true);

            public readonly bool GamePath(out Utf8GamePath path)
                => Utf8GamePath.FromSpan(CsHandle.FileName.AsSpan(), MetaDataComputation.All, out path);

            [FieldOffset(0x00)]
            public CsHandle.ResourceHandle CsHandle;

            [FieldOffset(0x00)]
            public void** VTable;

            [FieldOffset(0x08)]
            public ResourceCategory Category;

            [FieldOffset(0x0C)]
            public uint FileType;

            [FieldOffset(0x28)]
            public uint FileSize;

            [FieldOffset(0x48)]
            public byte* FileNameData;

            [FieldOffset(0x58)]
            public int FileNameLength;

            [FieldOffset(0xA9)]
            public byte LoadState;

            [FieldOffset(0xAC)]
            public uint RefCount;


            // Only use these if you know what you are doing.
            // Those are actually only sure to be accessible for DefaultResourceHandles.
            [FieldOffset(0xB0)]
            public DataIndirection* Data;

            [FieldOffset(0xB8)]
            public uint DataLength;

            public (nint Data, int Length) GetData()
                => Data != null
                    ? ((nint)Data->DataPtr, (int)Data->DataLength)
                    : (nint.Zero, 0);

            public bool SetData(nint data, int length)
            {
                if (Data == null)
                    return false;

                Data->DataPtr = length != 0 ? (byte*)data : null;
                Data->DataLength = (ulong)length;
                DataLength = (uint)length;
                return true;
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct GetResourceParameters
        {
            [FieldOffset(16)]
            public uint SegmentOffset;

            [FieldOffset(20)]
            public uint SegmentLength;

            public readonly bool IsPartialRead
                => SegmentLength != 0;
        }

        public delegate void* GetResourceAsyncDelegate(IntPtr resourceManager, uint* categoryId, uint* resourceType,
            int* resourceHash, byte* path, GetResourceParameters* resParams, bool isUnknown);
        [Signature("E8 ?? ?? ?? 00 48 8B D8 EB ?? F0 FF 83 ?? ?? 00 00", DetourName = nameof(GetResourceAsyncDetour))]
        private Hook<GetResourceAsyncDelegate> GetResourceAsyncHook;
        private void* GetResourceAsyncDetour(IntPtr resourceManager, uint* categoryId, uint* resourceType,
            int* resourceHash, byte* path, GetResourceParameters* resParams, bool isUnknown)
        {
            if (!Utf8GamePath.FromPointer(path, MetaDataComputation.None, out var gamePath))
            {
                return GetResourceAsyncHook.Original(resourceManager, categoryId, resourceType, resourceHash, path, resParams, isUnknown);
            }

            var gamePathString = gamePath.ToString();

            var replacedPath = GetReplacePath(gamePathString, out var localPath) ? localPath : null;

            if (replacedPath == null || replacedPath.Length >= 260)
            {
                var unreplaced = GetResourceAsyncHook.Original(resourceManager, categoryId, resourceType, resourceHash, path, resParams, isUnknown);
                //Plugin.PluginLog.Debug($"[GetResourceHandler] ORIGINAL: {gamePathString} -> " + new IntPtr(unreplaced).ToString("X8"));
                return unreplaced;
            }

            var resolvedPath = new FullPath(replacedPath);
            //PathResolved?.Invoke(*resourceType, resolvedPath);

            *resourceHash = ComputeHash(resolvedPath.InternalName, resParams);
            path = resolvedPath.InternalName.Path;

            var replaced = GetResourceAsyncHook.Original(resourceManager, categoryId, resourceType, resourceHash, path, resParams, isUnknown);
            //Plugin.PluginLog.Debug($"[GetResourceHandler] REPLACED: {gamePathString} -> {replacedPath} -> " + new IntPtr(replaced).ToString("X8"));
            return replaced;

        }

        public static int ComputeHash(CiByteString path, GetResourceParameters* resParams)
        {
            if (resParams == null || !resParams->IsPartialRead)
                return path.Crc32;

            // When the game requests file only partially, crc32 includes that information, in format of:
            // path/to/file.ext.hex_offset.hex_size
            // ex) music/ex4/BGM_EX4_System_Title.scd.381adc.30000
            return CiByteString.Join(
                (byte)'.',
                path,
                CiByteString.FromString(resParams->SegmentOffset.ToString("x"), out var s1, MetaDataComputation.None) ? s1 : CiByteString.Empty,
                CiByteString.FromString(resParams->SegmentLength.ToString("x"), out var s2, MetaDataComputation.None) ? s2 : CiByteString.Empty
            ).Crc32;
        }

        [StructLayout(LayoutKind.Explicit)]
        public unsafe struct SeFileDescriptor
        {
            [FieldOffset(0x00)]
            public uint FileMode;

            [FieldOffset(0x30)]
            public void* FileDescriptor;

            [FieldOffset(0x50)]
            public ResourceHandle* ResourceHandle;

            [FieldOffset(0x70)]
            public char Utf16FileName;
        }

        public delegate byte ReadSqpackDelegate(IntPtr fileHandler, SeFileDescriptor* fileDesc, int priority, bool isSync);
        [Signature("40 56 41 56 48 83 EC ?? 0F BE 02", DetourName = nameof(ReadSqpackDetour))]
        private Hook<ReadSqpackDelegate> ReadSqpackHook;
        private byte ReadSqpackDetour(IntPtr fileHandler, SeFileDescriptor* fileDesc, int priority, bool isSync)
        {
            if (fileDesc->ResourceHandle == null)
                return ReadSqpackHook.Original(fileHandler, fileDesc, priority, isSync);

            if (!fileDesc->ResourceHandle->GamePath(out var originalGamePath))
            {
                return ReadSqpackHook.Original(fileHandler, fileDesc, priority, isSync);
            }

            var originalPath = originalGamePath.ToString();
            var isPenumbra = ProcessPenumbraPath(originalPath, out var gameFsPath);

            //Plugin.PluginLog.Debug($"[ReadSqpackHandler] {gameFsPath}");

            var isRooted = Path.IsPathRooted(gameFsPath);

            // looking for refreshed paths, could also be like |default_1|path.avfx
            if (gameFsPath != null && !isRooted)
            {
                var replacementPath = GetReplacePath(gameFsPath, out var localPath) ? localPath : null;
                if (replacementPath != null && Path.IsPathRooted(replacementPath) && replacementPath.Length < 260)
                {
                    gameFsPath = replacementPath;
                    isRooted = true;
                    isPenumbra = false;
                }
            }

            // call the original if it's a penumbra path that doesn't need replacement as well
            if (gameFsPath == null || gameFsPath.Length >= 260 || !isRooted || isPenumbra)
            {
                //Plugin.PluginLog.Debug($"[ReadSqpackHandler] ORIGINAL: {originalPath}");
                return ReadSqpackHook.Original(fileHandler, fileDesc, priority, isSync);
            }

            Plugin.PluginLog.Debug($"[ReadSqpackHandler] REPLACED: {gameFsPath}");

            fileDesc->FileMode = 0;

            ByteString.FromString(gameFsPath, out var gamePath);

            // note: must be utf16
            var utfPath = Encoding.Unicode.GetBytes(gameFsPath);
            Marshal.Copy(utfPath, 0, new IntPtr(&fileDesc->Utf16FileName), utfPath.Length);
            var fd = stackalloc byte[0x20 + utfPath.Length + 0x16];
            Marshal.Copy(utfPath, 0, new IntPtr(fd + 0x21), utfPath.Length);
            fileDesc->FileDescriptor = fd;

            return ReadFile(fileHandler, fileDesc, priority, isSync);
        }

        private Dictionary<string, string> RepalcePaths = new Dictionary<string, string>();
        public void AddReplace(string gameFsPath, string replacePath)
        {
            this.RepalcePaths.Add(gameFsPath, replacePath);
            Plugin.PluginLog.Info($"Add Repalce: {gameFsPath} -> {replacePath}");
        }
        private bool GetReplacePath(string gameFsPath, out string localPath)
        {
            if (RepalcePaths.TryGetValue(gameFsPath, out localPath))
            {
                Plugin.PluginLog.Info($"REPLACED: {gameFsPath} {localPath}");
                return true;
            }
            else
            {
                localPath = null;
                return false;
            }
        }

        private static bool ProcessPenumbraPath(string path, out string outPath)
        {
            outPath = path;
            if (string.IsNullOrEmpty(path))
                return false;
            if (!path.StartsWith('|'))
                return false;

            var split = path.Split("|");
            if (split.Length != 3)
                return false;

            outPath = split[2];
            return true;
        }

        public delegate byte ReadFileDelegate(IntPtr fileHandler, SeFileDescriptor* fileDesc, int priority, bool isSync);
        private ReadFileDelegate ReadFile;

        public ResourceLoader()
        {
            Plugin.GameInteropProvider.InitializeFromAttributes(this);
            ReadFile = Marshal.GetDelegateForFunctionPointer<ReadFileDelegate>(Plugin.SigScanner.ScanText("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 54 41 55 41 56 41 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 48 63 42"));
            this.GetResourceAsyncHook?.Enable();
            this.ReadSqpackHook?.Enable();
        }

        public void Dispose()
        {
            this.GetResourceAsyncHook?.Dispose();
            this.ReadSqpackHook?.Dispose();
        }
    }
}
