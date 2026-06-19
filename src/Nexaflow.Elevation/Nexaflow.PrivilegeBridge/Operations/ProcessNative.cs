using System.Runtime.InteropServices;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>
/// Bridge-local native plumbing for enumerating a process's open handles — there is no managed API. We
/// walk the whole-system handle table with <c>NtQuerySystemInformation(SystemExtendedHandleInformation)</c>,
/// filter to the target PID, then resolve each handle's type and name by <c>DuplicateHandle</c> +
/// <c>NtQueryObject</c>. Only valid when run elevated (the bridge is): <c>SeDebugPrivilege</c> lets us open
/// and duplicate handles from protected / other-user processes.
///
/// The name query (<c>ObjectNameInformation</c>) can block indefinitely on synchronous objects — a named
/// pipe with no pending I/O, some device handles — so it runs on a reusable worker thread guarded by a
/// short timeout. On a hang we abandon that worker (it owns and will close the duplicated handle if the
/// kernel call ever returns) and spin up a fresh one; any genuinely stuck thread is reclaimed when this
/// short-lived bridge process exits. This is the standard Process-Hacker approach.
/// </summary>
internal static class ProcessNative
{
    /// <summary>One resolved handle, shaped to match the host's <c>HandleInfo</c> JSON.</summary>
    public sealed class HandleRecord
    {
        public string Type { get; set; } = "";
        public string Name { get; set; } = "";
        public string HandleValue { get; set; } = "";   // "0x1F4"
        public string Access { get; set; } = "";          // "0x001F0FFF"
    }

    private const int SystemExtendedHandleInformation = 0x40;
    private const int ObjectNameInformation = 1;
    private const int ObjectTypeInformation = 2;

    private const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;

    private const uint PROCESS_DUP_HANDLE = 0x0040;
    private const uint DUPLICATE_SAME_ACCESS = 0x0002;

    private const int NameQueryTimeoutMs = 50;

    /// <summary>Enumerates the open handles held by <paramref name="pid"/>. Never throws — a failure to
    /// open the target or resolve a particular handle degrades to fewer/blanker rows.</summary>
    public static List<HandleRecord> EnumerateHandles(int pid)
    {
        var results = new List<HandleRecord>();

        var entries = QueryHandleTable();
        if (entries.Count == 0) return results;

        IntPtr target = OpenProcess(PROCESS_DUP_HANDLE, false, pid);
        if (target == IntPtr.Zero) return results;   // can't duplicate from it — report nothing rather than throw

        IntPtr self = GetCurrentProcess();
        var namer = new NameWorker();
        try
        {
            foreach (var e in entries)
            {
                if ((int)e.UniqueProcessId != pid) continue;

                if (!DuplicateHandle(target, e.HandleValue, self, out IntPtr dup, 0, false, DUPLICATE_SAME_ACCESS)
                    || dup == IntPtr.Zero)
                {
                    results.Add(new HandleRecord
                    {
                        HandleValue = "0x" + ((long)e.HandleValue).ToString("X"),
                        Access      = "0x" + e.GrantedAccess.ToString("X8"),
                    });
                    continue;
                }

                // Type query is fast and safe on this thread; the name query can hang, so it goes to the worker
                // (which also owns closing the duplicate, whether it completes or is abandoned).
                string type = QueryType(dup);
                var (name, alive) = namer.Query(dup, NameQueryTimeoutMs);
                if (!alive) namer = new NameWorker();   // previous worker is stuck on this handle — replace it

                results.Add(new HandleRecord
                {
                    Type        = type,
                    Name        = name,
                    HandleValue = "0x" + ((long)e.HandleValue).ToString("X"),
                    Access      = "0x" + e.GrantedAccess.ToString("X8"),
                });
            }
        }
        finally
        {
            namer.Shutdown();
            CloseHandle(target);
        }

        return results;
    }

    // ── System handle table ──────────────────────────────────────────────────
    private static List<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX> QueryHandleTable()
    {
        int len = 0x10000;
        IntPtr buf = Marshal.AllocHGlobal(len);
        try
        {
            uint status;
            while ((status = NtQuerySystemInformation(SystemExtendedHandleInformation, buf, len, out int needed))
                   == STATUS_INFO_LENGTH_MISMATCH)
            {
                Marshal.FreeHGlobal(buf);
                len = Math.Max(needed, len * 2);
                buf = Marshal.AllocHGlobal(len);
            }
            if (status != 0) return [];

            nint count = Marshal.ReadIntPtr(buf);                 // ULONG_PTR NumberOfHandles
            int entrySize = Marshal.SizeOf<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
            int offset = IntPtr.Size * 2;                          // skip NumberOfHandles + Reserved
            var list = new List<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>((int)Math.Min(count, 1 << 20));
            for (long i = 0; i < count; i++)
            {
                list.Add(Marshal.PtrToStructure<SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(buf + offset + (int)(i * entrySize)));
            }
            return list;
        }
        catch
        {
            return [];
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    // ── Object type / name ─────────────────────────────────────────────────────
    private static string QueryType(IntPtr handle) => QueryObjectString(handle, ObjectTypeInformation);
    private static string QueryName(IntPtr handle) => QueryObjectString(handle, ObjectNameInformation);

    /// <summary>Both <c>ObjectTypeInformation</c> and <c>ObjectNameInformation</c> begin with a
    /// <c>UNICODE_STRING</c>, so the same reader serves both.</summary>
    private static string QueryObjectString(IntPtr handle, int infoClass)
    {
        int len = 0x800;
        IntPtr buf = Marshal.AllocHGlobal(len);
        try
        {
            uint status = NtQueryObject(handle, infoClass, buf, len, out int needed);
            if (status == STATUS_INFO_LENGTH_MISMATCH && needed > len)
            {
                Marshal.FreeHGlobal(buf);
                len = needed;
                buf = Marshal.AllocHGlobal(len);
                status = NtQueryObject(handle, infoClass, buf, len, out _);
            }
            if (status != 0) return "";

            ushort byteLen = (ushort)Marshal.ReadInt16(buf);          // UNICODE_STRING.Length (bytes)
            IntPtr strPtr  = Marshal.ReadIntPtr(buf, IntPtr.Size);    // UNICODE_STRING.Buffer
            if (byteLen == 0 || strPtr == IntPtr.Zero) return "";
            return Marshal.PtrToStringUni(strPtr, byteLen / 2) ?? "";
        }
        catch
        {
            return "";
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>
    /// A single reusable thread that runs the (potentially hanging) name query. <see cref="Query"/> hands it
    /// a duplicated handle and waits a short timeout; the worker always closes that handle. If it doesn't
    /// answer in time it is abandoned (caller spins up a new one) and the stuck handle/thread are reclaimed
    /// at process exit.
    /// </summary>
    private sealed class NameWorker
    {
        private readonly SemaphoreSlim _go = new(0, 1);
        private readonly SemaphoreSlim _ready = new(0, 1);
        private IntPtr _handle;
        private string _name = "";
        private volatile bool _abandoned;

        public NameWorker()
        {
            new Thread(Loop) { IsBackground = true, Name = "handle-name-query" }.Start();
        }

        private void Loop()
        {
            while (true)
            {
                _go.Wait();
                if (_abandoned) return;
                IntPtr h = _handle;
                string n;
                try { n = QueryName(h); } catch { n = ""; }
                CloseHandle(h);
                if (_abandoned) return;   // timed out on the caller's side; result no longer wanted
                _name = n;
                _ready.Release();
            }
        }

        public (string name, bool alive) Query(IntPtr dup, int timeoutMs)
        {
            _handle = dup;
            _go.Release();
            if (_ready.Wait(timeoutMs)) return (_name, true);
            _abandoned = true;            // worker is stuck inside QueryName; it will close the handle itself
            return ("", false);
        }

        public void Shutdown()
        {
            _abandoned = true;
            _go.Release();                // wake the loop so it can observe _abandoned and exit (no-op if stuck)
        }
    }

    // ── P/Invoke ────────────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public IntPtr Object;
        public IntPtr UniqueProcessId;
        public IntPtr HandleValue;
        public uint   GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint   HandleAttributes;
        public uint   Reserved;
    }

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(
        int systemInformationClass, IntPtr systemInformation, int systemInformationLength, out int returnLength);

    [DllImport("ntdll.dll")]
    private static extern uint NtQueryObject(
        IntPtr handle, int objectInformationClass, IntPtr objectInformation, int objectInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(
        IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess,
        out IntPtr targetHandle, uint desiredAccess, bool inheritHandle, uint options);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
