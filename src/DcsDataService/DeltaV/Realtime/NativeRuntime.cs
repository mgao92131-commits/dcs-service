using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DcsRealtime;

namespace DcsDataService.DeltaV.Realtime
{
    internal sealed class NativeFailure : Exception
    {
        internal readonly ReadError Error;
        internal NativeFailure(ReadError error) : base(error.message) { Error = error; }
    }
    internal sealed class NativeRuntime : IRealtimeRuntime
    {
        private string Stage = "request";
        private static readonly string[] Files = { "bss.dll", "Sec.dll", "oss.dll", "RtStringTable.dll", "roc.dll" };
        private static readonly string[] Hashes = {
            "34c3d031c215242e6edb1a0edf8c68da832c3665a03ef93501d5608136f7a20c",
            "b533ff4cc5e610a99a72cb49aa1a333ad35f4d28e99d5fd692c0c8128d468756",
            "f3807c27582ca0928d11dd7c18afd43ca87e98bae1d3340a0a6639935cfa425d",
            "b55356bef5e6de73abe0924d4620c96b66e98ff866c025037f692de2e5334d68",
            "b33a4f2890dbe3e76106aa1e42bb22e729abccd1b9439608931e92406188378f" };
        private readonly string _directory;
        private readonly TimeZoneInfo _zone;
        private readonly IntPtr[] _libraries = new IntPtr[Files.Length];
        private Construct _pathCtor, _dataCtor;
        private Destroy _pathDtor, _textDestroy;
        private Locate _locate;
        private StatusText _statusText;
        private PathSize _pathSize;
        private PathToString _pathToString;
        private const int MaximumValueCharacters = 8192;
        internal NativeRuntime(string zone)
        {
            _directory = DeltaVInstallation.BinDirectory; _zone = TimeZoneInfo.FindSystemTimeZoneById(zone);
        }
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool SetDllDirectory(string directory);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true)] private static extern IntPtr GetProcAddress(IntPtr module, string symbol);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitHawk(byte mode);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Locate(IntPtr outRole, IntPtr path, uint timeout);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr StatusText(int status);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate IntPtr Construct(IntPtr self, IntPtr text);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate void Destroy(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate IntPtr DeleteRole(IntPtr self, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate ushort PathSize(IntPtr self, ushort level);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate void PathToString(IntPtr self, IntPtr output, ushort level);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int ReadField(IntPtr self, IntPtr data, IntPtr text, IntPtr field, short index1, short index2);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int ItemType(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int ItemText(IntPtr self, ref IntPtr text);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int ItemFloat(IntPtr self, out float value);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int ItemDouble(IntPtr self, out double value);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int ItemInt(IntPtr self, out int value);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int ItemUInt(IntPtr self, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int ItemShort(IntPtr self, out short value);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int ItemUShort(IntPtr self, out ushort value);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int ItemByte(IntPtr self, out byte value);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] private delegate int ItemSByte(IntPtr self, out sbyte value);

        private void At(string stage) { Stage = stage; }
        private static T Function<T>(IntPtr address) where T : class
        {
            if (address == IntPtr.Zero) throw new InvalidOperationException("Null native function pointer.");
            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }
        private T Bind<T>(int library, string symbol) where T : class
        {
            IntPtr address = GetProcAddress(_libraries[library], symbol);
            if (address == IntPtr.Zero) throw new InvalidOperationException("Missing native symbol: " + symbol);
            return Function<T>(address);
        }
        private static T Virtual<T>(IntPtr item, int slot) where T : class { return Function<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(item), slot)); }
        private static IntPtr Buffer() { IntPtr p = Marshal.AllocHGlobal(256); Marshal.Copy(new byte[256], 0, p, 256); return p; }
        private static string NativeText(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("Native text is null.");
            for (int i = 0; i <= MaximumValueCharacters; i++)
                if (Marshal.ReadInt16(pointer, i * 2) == 0) return Marshal.PtrToStringUni(pointer, i);
            throw new InvalidOperationException("Native text exceeds the supported value length.");
        }
        private void Check(int status, string code)
        {
            if (status == 0) return;
            string name = _statusText == null ? null : NativeText(_statusText(status));
            if (code == "realtime_read_failed" && name != null && (name.IndexOf("NO_BLOCK", StringComparison.Ordinal) >= 0 || name.IndexOf("NOT_FOUND", StringComparison.Ordinal) >= 0 || name == "RT_INVALID_ATTRIBUTE" || name == "RT_INVALID_FIELD" || name == "RT_INVALID_HAWK_PATH")) code = "parameter_not_found";
            throw new NativeFailure(new ReadError { code = code, stage = Stage, nativeStatus = status, nativeStatusName = name, message = "Native status=" + status.ToString(CultureInfo.InvariantCulture) + " (0x" + status.ToString("X8", CultureInfo.InvariantCulture) + ") " + name });
        }
        public void Initialize()
        {
            if (IntPtr.Size != 4) throw new InvalidOperationException("Native realtime reads require x86.");
            At("verify_files");
            for (int i = 0; i < Files.Length; i++)
            {
                string hash;
                using (FileStream stream = File.OpenRead(Path.Combine(_directory, Files[i])))
                using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (hash != Hashes[i]) throw new NativeFailure(new ReadError { code = "realtime_abi_mismatch", stage = Stage, message = "Unsupported DeltaV binary: " + Files[i] });
            }
            At("load_runtime");
            if (!SetDllDirectory(_directory)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            for (int i = 0; i < Files.Length; i++)
            {
                _libraries[i] = LoadLibrary(Path.Combine(_directory, Files[i]));
                if (_libraries[i] == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot load " + Files[i]);
            }
            using (Process process = Process.GetCurrentProcess())
                foreach (ProcessModule module in process.Modules)
                    for (int index = 0; index < Files.Length; index++)
                        if (String.Equals(module.ModuleName, Files[index], StringComparison.OrdinalIgnoreCase))
                        {
                            string actualHash;
                            using (FileStream stream = File.OpenRead(module.FileName))
                            using (SHA256 sha = SHA256.Create()) actualHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                            if (actualHash != Hashes[index]) throw new NativeFailure(new ReadError { code = "realtime_abi_mismatch", stage = Stage, message = "Loaded DeltaV binary does not match the supported ABI: " + module.ModuleName });
                        }
            _pathCtor = Bind<Construct>(0, "??0RtPath@@QAE@PB_W@Z");
            _pathDtor = Bind<Destroy>(0, "??1RtPath@@QAE@XZ");
            _dataCtor = Bind<Construct>(0, "??0RtDataItemUnicodeStringPtr@@QAE@PB_W@Z");
            _textDestroy = Bind<Destroy>(0, "?destroy@RtDataItemUnicodeStringPtr@@UAEXXZ");
            _pathSize = Bind<PathSize>(0, "?pathSize@RtPath@@QBEGG@Z");
            _pathToString = Bind<PathToString>(0, "?toWStr@RtPath@@QBEXPA_WG@Z");
            _locate = Bind<Locate>(1, "?Locate@RtSession@@SA?AW4BSS_STATUS@@AAPAVRtBaseModuleRole@@AAVRtPath@@I@Z");
            _statusText = Bind<StatusText>(3, "?GetString@RtStatusStrings@@SAPB_WW4BSS_STATUS@@@Z");
            At("initialize_hawk"); Check(Bind<InitHawk>(2, "?InitializeHawk@@YAJE@Z")(1), "realtime_unavailable");
        }
        private string RenderPath(IntPtr path)
        {
            int length = _pathSize(path, 0);
            IntPtr text = Marshal.AllocHGlobal((length + 2) * 2);
            try { Marshal.Copy(new byte[(length + 2) * 2], 0, text, (length + 2) * 2); _pathToString(path, text, 0); return NativeText(text); }
            finally { Marshal.FreeHGlobal(text); }
        }
        private ReadField Reader(IntPtr role)
        {
            // ABI validated against WatchIt and RtSecureModule/RtModuleProxyC vtables, not base exports.
            IntPtr address = Marshal.ReadIntPtr(Marshal.ReadIntPtr(role), 0x264);
            bool valid = false;
            using (Process process = Process.GetCurrentProcess())
                foreach (ProcessModule module in process.Modules)
                {
                    long start = module.BaseAddress.ToInt64();
                    if (address.ToInt64() >= start && address.ToInt64() < start + module.ModuleMemorySize)
                        foreach (string name in Files) if (String.Equals(module.ModuleName, name, StringComparison.OrdinalIgnoreCase)) valid = true;
                }
            if (!valid) throw new InvalidOperationException("Read target is outside the pinned runtime modules.");
            return Function<ReadField>(address);
        }
        private object Scalar(IntPtr data, int type, string text, out string valueType, out string typeName)
        {
            int status = 0; object value = text; valueType = "text"; typeName = "UNKNOWN";
            // Type IDs recovered from this bss.dll's RTTI/type getters; slots from RtDataItem vtables.
            switch (type)
            {
                case 1: byte boolean; status = Virtual<ItemByte>(data, 0x20)(data, out boolean); value = boolean != 0; valueType = "boolean"; typeName = "BOOLEAN"; break;
                case 2: sbyte i8; status = Virtual<ItemSByte>(data, 0x2c)(data, out i8); value = i8; valueType = "integer"; typeName = "INT8"; break;
                case 3: short i16; status = Virtual<ItemShort>(data, 0x28)(data, out i16); value = i16; valueType = "integer"; typeName = "INT16"; break;
                case 4: int i32; status = Virtual<ItemInt>(data, 0x24)(data, out i32); value = i32; valueType = "integer"; typeName = "INT32"; break;
                case 5: byte u8; status = Virtual<ItemByte>(data, 0x20)(data, out u8); value = u8; valueType = "integer"; typeName = "UINT8"; break;
                case 6: ushort u16; status = Virtual<ItemUShort>(data, 0x1c)(data, out u16); value = u16; valueType = "integer"; typeName = "UINT16"; break;
                case 7: uint u32; status = Virtual<ItemUInt>(data, 0x18)(data, out u32); value = u32; valueType = "integer"; typeName = "UINT32"; break;
                case 8: float f32; status = Virtual<ItemFloat>(data, 0x14)(data, out f32); value = (double)f32; valueType = "number"; typeName = "FLOAT32"; break;
                case 15: double f64; status = Virtual<ItemDouble>(data, 0x10)(data, out f64); value = f64; valueType = "number"; typeName = "FLOAT64"; break;
                case 70: typeName = "ASCII_STRING"; break;
                case 71: typeName = "UNICODE_STRING"; break;
                case 66: typeName = "ENUM"; break;
                case 68: typeName = "RT_TIME"; break;
                case 80: typeName = "USER_DEFINED_ENUM"; break;
                case 81: typeName = "ENUM16"; break;
                case 119: typeName = "OPTION_BIT_STRING"; break;
                case 125: typeName = "INTERZONE_ENUM"; break;
                case 127: typeName = "INTERZONE_EVENT_TYPE"; break;
            }
            Check(status, "realtime_conversion_failed");
            if (value is double && (Double.IsNaN((double)value) || Double.IsInfinity((double)value))) { value = text; valueType = "nonfinite"; }
            return value;
        }
        public ReadItem Read(string tag)
        {
            IntPtr path = IntPtr.Zero, data = IntPtr.Zero, outRole = IntPtr.Zero, input = IntPtr.Zero, relative = IntPtr.Zero, role = IntPtr.Zero, textOwner = IntPtr.Zero;
            bool pathReady = false, dataReady = false, textReady = false;
            try
            {
                try
                {
                    path = Buffer(); data = Buffer(); outRole = Buffer(); input = Marshal.StringToHGlobalUni(tag);
                    At("construct_path"); _pathCtor(path, input); pathReady = true;
                    if (Marshal.ReadByte(path) == 0) Check(2, "parameter_not_found");
                    At("locate_module"); int locateStatus = _locate(outRole, path, 1000);
                    role = Marshal.ReadIntPtr(outRole); Check(locateStatus, "realtime_read_failed");
                    if (role == IntPtr.Zero) throw new InvalidOperationException("Locate returned a null module role.");
                    string remaining = RenderPath(path);
                    relative = Marshal.StringToHGlobalUni(remaining);
                    At("construct_value"); _dataCtor(data, IntPtr.Zero); dataReady = true;
                    At("read_parameter"); Check(Reader(role)(role, data, relative, IntPtr.Zero, 0, 0), "realtime_read_failed");
                    DateTime observed = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zone);
                    At("convert_value");
                    int type = Virtual<ItemType>(data, 0x60)(data);
                    if (type == 0) Check(34, "realtime_conversion_failed");
                    // Unicode getter returns a vendor-owned allocation, even for a Unicode source.
                    // Transfer it to a pinned Unicode data-item owner and let the vendor destroy it.
                    textOwner = Buffer(); _dataCtor(textOwner, IntPtr.Zero); textReady = true;
                    IntPtr converted = IntPtr.Zero; int textStatus = Virtual<ItemText>(data, 8)(data, ref converted);
                    Marshal.WriteIntPtr(textOwner, 4, converted);
                    Check(textStatus, "realtime_conversion_failed");
                    string text = NativeText(converted);
                    string valueType, typeName; object value = Scalar(data, type, text, out valueType, out typeName);
                    return new ReadItem { tag = tag, ok = true, value = value, valueText = text, valueType = valueType, nativeDataType = type, nativeTypeName = typeName, observedAt = observed.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture), sourceTimeZone = _zone.Id };
                }
                catch (NativeFailure ex) { return new ReadItem { tag = tag, ok = false, error = ex.Error, sourceTimeZone = _zone.Id }; }
            }
            finally
            {
                string previousStage = Stage; At("cleanup");
                try
                {
                    try
                    {
                        try { if (textReady) _textDestroy(textOwner); }
                        finally { if (dataReady) Virtual<Destroy>(data, 4)(data); }
                    }
                    finally
                    {
                        // Locate's role factories allocate a concrete role for the caller.
                        // Its virtual deleting destructor frees both vendor state and storage.
                        try { if (role != IntPtr.Zero) Virtual<DeleteRole>(role, 0)(role, 1); }
                        finally { if (pathReady) _pathDtor(path); }
                    }
                }
                finally
                {
                    if (textOwner != IntPtr.Zero) Marshal.FreeHGlobal(textOwner);
                    if (relative != IntPtr.Zero) Marshal.FreeHGlobal(relative);
                    if (input != IntPtr.Zero) Marshal.FreeHGlobal(input);
                    if (outRole != IntPtr.Zero) Marshal.FreeHGlobal(outRole);
                    if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
                    if (path != IntPtr.Zero) Marshal.FreeHGlobal(path);
                    Stage = previousStage;
                }
            }
        }
    }
}
