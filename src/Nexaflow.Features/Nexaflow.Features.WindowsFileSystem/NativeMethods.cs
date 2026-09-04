using Microsoft.Win32.SafeHandles;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nexaflow.Features.WindowsFileSystem
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

        // ── Known-folder resolution (SHGetKnownFolderPath) ───────────────────
        // Honours folder redirection; used to offer named Windows folders (Downloads, …)
        // in the ribbon's "add page" dropdown. Mirrors Core's KnownFolderService — duplicated
        // here because features must not depend on Core.

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
        private static extern void SHGetKnownFolderPath(
            [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

        /// <summary>Resolves a known-folder GUID to its path, or "" if the call fails.</summary>
        public static string GetKnownFolderPath(Guid folderId)
        {
            IntPtr ptr = IntPtr.Zero;
            try
            {
                SHGetKnownFolderPath(folderId, 0, IntPtr.Zero, out ptr);
                return Marshal.PtrToStringUni(ptr) ?? string.Empty;
            }
            catch { return string.Empty; }
            finally { if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr); }
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

        // ── "Open with…" picker (SHOpenWithDialog) ───────────────────────────

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OPENASINFO
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string  pcszFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string? pcszClass;
            public int oaifInFlags;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHOpenWithDialog(IntPtr hwndParent, ref OPENASINFO oainfo);

        private const int OAIF_ALLOW_REGISTRATION = 0x0001;
        private const int OAIF_EXEC               = 0x0004;

        /// <summary>
        /// Shows the Windows "Open with" picker for <paramref name="path"/> and launches the chosen
        /// app. Modal — call on the STA UI thread. Returns true when the user picked an app.
        /// </summary>
        public static bool ShowOpenWithDialog(string path)
        {
            var info = new OPENASINFO
            {
                pcszFile    = path,
                pcszClass   = null,
                oaifInFlags = OAIF_ALLOW_REGISTRATION | OAIF_EXEC,
            };
            try { return SHOpenWithDialog(IntPtr.Zero, ref info) == 0; }
            catch { return false; }
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
        /// Reads what a paste would act on: the file list, and whether it was cut rather than copied.
        /// <para>
        /// This used to do the transfer too, on the UI thread, with its own conflict rules that drag-drop
        /// did not share. It now only reads — the destinations and the transfer belong to
        /// <c>FileOperationDestinations</c> and the operation queue, which is what makes paste and drop
        /// behave the same way by construction.
        /// </para>
        /// </summary>
        public static (IReadOnlyList<string> Paths, bool IsCut) ClipboardReadDrop()
        {
            var data = Clipboard.GetDataObject();
            if (data is null) return ([], false);

            var list = Clipboard.GetFileDropList();
            if (list is null || list.Count == 0) return ([], false);

            bool isCut = false;
            if (data.GetData(PreferredDropEffect) is MemoryStream ms)
            {
                var bytes = new byte[4];
                ms.Position = 0;
                if (ms.Read(bytes, 0, 4) == 4)
                    isCut = (DragDropEffects)BitConverter.ToInt32(bytes, 0) == DragDropEffects.Move;
            }

            var paths = new List<string>(list.Count);
            foreach (string? path in list)
                if (!string.IsNullOrEmpty(path)) paths.Add(path);

            return (paths, isCut);
        }

        /// <summary>Empties the clipboard after a cut has actually landed. Must run on the UI thread.</summary>
        public static void ClipboardClear()
        {
            try { Clipboard.Clear(); } catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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
