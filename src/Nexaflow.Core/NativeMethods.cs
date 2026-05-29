using Microsoft.Win32.SafeHandles;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nexaflow.Core
{
    public static class NativeMethods
    {
        // ── SSD/HDD detection via IOCTL_STORAGE_QUERY_PROPERTY ───────────────

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice, uint dwIoControlCode,
            ref STORAGE_PROPERTY_QUERY lpInBuffer, uint nInBufferSize,
            out DEVICE_SEEK_PENALTY_DESCRIPTOR lpOutBuffer, uint nOutBufferSize,
            out uint lpBytesReturned, IntPtr lpOverlapped);

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_PROPERTY_QUERY
        {
            public uint PropertyId;          // 7 = StorageDeviceSeekPenaltyProperty
            public uint QueryType;           // 0 = PropertyStandardQuery
            public byte AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            [MarshalAs(UnmanagedType.U1)]
            public bool IncursSeekPenalty;
        }

        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
        private const uint FILE_SHARE_READ_WRITE = 0x00000001 | 0x00000002;
        private const uint OPEN_EXISTING = 3;

        /// <summary>
        /// Returns true when the fixed drive at <paramref name="driveRoot"/> (e.g. "C:\")
        /// does not incur a seek penalty — i.e. it is an SSD or NVMe device.
        /// Returns false for HDDs or when detection fails.
        /// </summary>
        public static bool IsNoSeekPenalty(string driveRoot)
        {
            try
            {
                // "C:\" → @"\\.\C:"
                string letter = driveRoot.TrimEnd('\\', '/');
                if (letter.Length < 2 || letter[1] != ':') return false;

                using var handle = CreateFile(@"\\.\" + letter, 0, FILE_SHARE_READ_WRITE,
                    IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (handle.IsInvalid) return false;

                var query = new STORAGE_PROPERTY_QUERY { PropertyId = 7, QueryType = 0 };
                bool ok = DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY,
                    ref query, (uint)Marshal.SizeOf(query),
                    out var descriptor, (uint)Marshal.SizeOf<DEVICE_SEEK_PENALTY_DESCRIPTOR>(),
                    out _, IntPtr.Zero);

                return ok && !descriptor.IncursSeekPenalty;
            }
            catch { return false; }
        }

        // ── Shell execute (properties dialog) ────────────────────────────────

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpVerb;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpFile;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpParameters;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        private const int SW_SHOW = 5;
        private const uint SEE_MASK_INVOKEIDLIST = 12;

        public static bool ShowFileProperties(string Filename)
        {
            SHELLEXECUTEINFO info = new SHELLEXECUTEINFO();
            info.cbSize = Marshal.SizeOf(info);
            info.lpVerb = "properties";
            info.lpFile = Filename;
            info.nShow = SW_SHOW;
            info.fMask = SEE_MASK_INVOKEIDLIST;
            return ShellExecuteEx(ref info);
        }

        // ── Clipboard file operations ─────────────────────────────────────────
        // Windows uses CF_HDROP + "Preferred DropEffect" to distinguish cut vs copy.
        // DragDropEffects.Move (2) = cut, DragDropEffects.Copy (1) = copy.

        private const string PreferredDropEffect = "Preferred DropEffect";

        /// <summary>Places <paramref name="paths"/> on the clipboard as a copy operation.</summary>
        public static void ClipboardCopyFiles(IReadOnlyList<string> paths)
            => SetClipboardFiles(paths, DragDropEffects.Copy);

        /// <summary>Places <paramref name="paths"/> on the clipboard as a cut (move) operation.</summary>
        public static void ClipboardCutFiles(IReadOnlyList<string> paths)
            => SetClipboardFiles(paths, DragDropEffects.Move);

        private static void SetClipboardFiles(IReadOnlyList<string> paths, DragDropEffects effect)
        {
            var list = new StringCollection();
            list.AddRange([.. paths]);

            var data = new DataObject();
            data.SetFileDropList(list);
            // Encode the preferred drop effect as a 4-byte little-endian stream
            data.SetData(PreferredDropEffect, new MemoryStream(BitConverter.GetBytes((int)effect)));
            Clipboard.SetDataObject(data, copy: true);
        }

        /// <summary>
        /// Returns <c>true</c> when the clipboard contains a file-drop list
        /// that can be pasted into the file system.
        /// </summary>
        public static bool ClipboardHasFiles()
            => Clipboard.ContainsFileDropList();

        /// <summary>
        /// Pastes clipboard files into <paramref name="destinationFolder"/>.
        /// Moves files when the clipboard effect is <see cref="DragDropEffects.Move"/>,
        /// otherwise copies them.
        /// </summary>
        public static void ClipboardPasteFiles(string destinationFolder)
        {
            var data = Clipboard.GetDataObject();
            if (data is null) return;

            var list = Clipboard.GetFileDropList();
            if (list is null || list.Count == 0) return;

            // Determine cut vs copy
            bool isCut = false;
            if (data.GetData(PreferredDropEffect) is MemoryStream ms)
            {
                var bytes = new byte[4];
                ms.Position = 0;
                if (ms.Read(bytes, 0, 4) == 4)
                    isCut = (DragDropEffects)BitConverter.ToInt32(bytes, 0) == DragDropEffects.Move;
            }

            foreach (string source in list)
            {
                bool   sourceIsDir = Directory.Exists(source);
                string sourceTrimmed = source.TrimEnd(Path.DirectorySeparatorChar,
                                                      Path.AltDirectorySeparatorChar);
                string name = Path.GetFileName(sourceTrimmed);

                // Guard: refuse to copy a folder into itself or any of its descendants.
                if (!isCut && sourceIsDir)
                {
                    string destNorm = destinationFolder.TrimEnd(Path.DirectorySeparatorChar);
                    if (string.Equals(destNorm, sourceTrimmed, StringComparison.OrdinalIgnoreCase) ||
                        destNorm.StartsWith(sourceTrimmed + Path.DirectorySeparatorChar,
                                            StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Same-parent copy: use "Copy of <name>" so the result is distinct.
                bool isSameParent = !isCut &&
                    string.Equals(Path.GetDirectoryName(sourceTrimmed), destinationFolder,
                                  StringComparison.OrdinalIgnoreCase);

                string dest = isSameParent
                    ? CopyOfDestination(destinationFolder, name, sourceIsDir)
                    : UniqueDestination(Path.Combine(destinationFolder, name), sourceIsDir);

                if (sourceIsDir)
                {
                    if (isCut) Directory.Move(source, dest);
                    else       CopyDirectory(source, dest);
                }
                else
                {
                    if (isCut) File.Move(source, dest);
                    else       File.Copy(source, dest, overwrite: false);
                }
            }

            // After a cut-paste the clipboard contents are consumed — clear it
            // to match Windows Explorer behaviour.
            if (isCut)
                Clipboard.Clear();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a path that does not already exist, appending " (2)", " (3)" etc.
        /// </summary>
        private static string UniqueDestination(string path, bool isDirectory)
        {
            if (isDirectory)
            {
                if (!Directory.Exists(path)) return path;
                string parent = Path.GetDirectoryName(path) ?? path;
                string name   = Path.GetFileName(path);
                for (int i = 2; ; i++)
                {
                    string candidate = Path.Combine(parent, $"{name} ({i})");
                    if (!Directory.Exists(candidate)) return candidate;
                }
            }
            else
            {
                if (!File.Exists(path)) return path;
                string dir  = Path.GetDirectoryName(path) ?? string.Empty;
                string stem = Path.GetFileNameWithoutExtension(path);
                string ext  = Path.GetExtension(path);
                for (int i = 2; ; i++)
                {
                    string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
                    if (!File.Exists(candidate)) return candidate;
                }
            }
        }

        /// <summary>
        /// Returns a "Copy of &lt;name&gt;" path (then "Copy of &lt;name&gt; (2)" etc.)
        /// used when pasting into the same directory as the source.
        /// </summary>
        private static string CopyOfDestination(string folder, string name, bool isDirectory)
        {
            string candidate = Path.Combine(folder, $"Copy of {name}");
            if (isDirectory ? !Directory.Exists(candidate) : !File.Exists(candidate))
                return candidate;

            for (int i = 2; ; i++)
            {
                candidate = Path.Combine(folder, $"Copy of {name} ({i})");
                if (isDirectory ? !Directory.Exists(candidate) : !File.Exists(candidate))
                    return candidate;
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: false);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        // ── CPUID (x64) for instruction bits not exposed by System.Runtime.Intrinsics ──
        // .NET surfaces AVX/AVX2/FMA via System.Runtime.Intrinsics.X86 but has no F16C
        // class, so we execute a tiny CPUID stub from executable memory. Best-effort:
        // any failure (alloc denied by DEP/AV, non-x64, …) returns false.

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CpuIdDelegate(IntPtr regs, uint leaf);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr addr, UIntPtr size, uint allocType, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFree(IntPtr addr, UIntPtr size, uint freeType);

        private const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, MEM_RELEASE = 0x8000;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;

        // x64 stub: void cpuid(int* regs /*rcx*/, uint leaf /*edx*/)
        //   push rbx; mov r8,rcx; mov eax,edx; xor ecx,ecx; cpuid;
        //   store eax/ebx/ecx/edx → regs[0..3]; pop rbx; ret
        private static readonly byte[] CpuIdStubX64 =
        [
            0x53,                   // push rbx
            0x49, 0x89, 0xC8,       // mov  r8, rcx
            0x89, 0xD0,             // mov  eax, edx
            0x31, 0xC9,             // xor  ecx, ecx
            0x0F, 0xA2,             // cpuid
            0x41, 0x89, 0x00,       // mov  [r8],    eax
            0x41, 0x89, 0x58, 0x04, // mov  [r8+4],  ebx
            0x41, 0x89, 0x48, 0x08, // mov  [r8+8],  ecx
            0x41, 0x89, 0x50, 0x0C, // mov  [r8+12], edx
            0x5B,                   // pop  rbx
            0xC3,                   // ret
        ];

        /// <summary>
        /// True when the CPU reports F16C support (CPUID leaf 1, ECX bit 29).
        /// Returns false on any failure or when not running as an x64 process.
        /// </summary>
        public static bool HasF16C()
        {
            if (!Environment.Is64BitProcess) return false;

            IntPtr mem = IntPtr.Zero;
            try
            {
                mem = VirtualAlloc(IntPtr.Zero, (UIntPtr)CpuIdStubX64.Length,
                                   MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
                if (mem == IntPtr.Zero) return false;

                Marshal.Copy(CpuIdStubX64, 0, mem, CpuIdStubX64.Length);
                var cpuid = Marshal.GetDelegateForFunctionPointer<CpuIdDelegate>(mem);

                var regs   = new int[4];   // eax, ebx, ecx, edx
                var handle = GCHandle.Alloc(regs, GCHandleType.Pinned);
                try { cpuid(handle.AddrOfPinnedObject(), 1u); }
                finally { handle.Free(); }

                return (regs[2] & (1 << 29)) != 0;   // ECX bit 29 = F16C
            }
            catch { return false; }
            finally
            {
                if (mem != IntPtr.Zero) VirtualFree(mem, UIntPtr.Zero, MEM_RELEASE);
            }
        }

        // ── DWM (Desktop Window Manager) ─────────────────────────────────────

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static void EnableDarkMode(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int dark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }

        // ── Recycle bin deletion via SHFileOperation ──────────────────────────

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint   wFunc;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool   fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        private const uint FO_DELETE  = 0x0003;
        private const ushort FOF_ALLOWUNDO         = 0x0040; // send to recycle bin
        private const ushort FOF_NOCONFIRMATION    = 0x0010; // no shell "are you sure?" dialog
        private const ushort FOF_NOERRORUI         = 0x0400;
        private const ushort FOF_SILENT            = 0x0004; // no progress dialog

        /// <summary>
        /// Moves <paramref name="paths"/> to the Recycle Bin via <c>SHFileOperation</c>.
        /// Multiple paths are packed into a single null-separated, double-null-terminated string.
        /// Returns <c>true</c> when all items were recycled successfully.
        /// </summary>
        public static bool RecycleFiles(IReadOnlyList<string> paths)
        {
            // pFrom must be double-null-terminated; use \0 to separate entries.
            string packed = string.Join("\0", paths) + "\0\0";

            var op = new SHFILEOPSTRUCT
            {
                wFunc  = FO_DELETE,
                pFrom  = packed,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
            };

            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
        }

        /// <summary>
        /// Permanently deletes <paramref name="paths"/> without a Recycle Bin entry.
        /// </summary>
        public static bool DeleteFilesPermanently(IReadOnlyList<string> paths)
        {
            string packed = string.Join("\0", paths) + "\0\0";

            var op = new SHFILEOPSTRUCT
            {
                wFunc  = FO_DELETE,
                pFrom  = packed,
                fFlags = FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
            };

            return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
        }
    }
}
