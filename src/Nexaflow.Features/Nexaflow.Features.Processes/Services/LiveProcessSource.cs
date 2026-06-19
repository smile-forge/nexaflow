using System.Diagnostics;
using System.Management;
using Nexaflow.Features.Processes.Models;

namespace Nexaflow.Features.Processes.Services;

/// <summary>
/// The real <see cref="IProcessSource"/>. Enumerates processes via a cheap Toolhelp snapshot (the only
/// source of parent-PID), derives CPU% from <see cref="Process.TotalProcessorTime"/> deltas between
/// samples, and reads memory off cached <see cref="Process"/> handles. Image metadata (description /
/// company / path / version) is read once per PID and cached — it never changes for a live process.
/// All per-process reads are wrapped: a protected or cross-bitness process degrades to blanks +
/// <see cref="ProcessSample.AccessDenied"/> rather than throwing.
/// </summary>
public sealed class LiveProcessSource : IProcessSource, IDisposable
{
    private readonly Dictionary<int, Process> _procs = new();
    private readonly Dictionary<int, (TimeSpan cpu, long ts)> _cpu = new();
    private readonly Dictionary<int, ProcMeta> _meta = new();
    private (ulong idle, ulong kernel, ulong user)? _prevSys;

    public ProcessSnapshot Snapshot()
    {
        long now = Stopwatch.GetTimestamp();
        int cores = Math.Max(1, Environment.ProcessorCount);
        var entries = NativeMethods.SnapshotProcesses();
        var live = new HashSet<int>(entries.Count);
        var samples = new List<ProcessSample>(entries.Count);

        foreach (var (pid, parent, name) in entries)
        {
            live.Add(pid);
            double cpu = 0;
            long priv = 0, ws = 0;
            bool denied = false;

            var proc = GetOrOpen(pid);
            if (proc is null)
            {
                denied = true;
            }
            else
            {
                try { proc.Refresh(); priv = proc.PrivateMemorySize64; ws = proc.WorkingSet64; }
                catch { denied = true; }
                try { cpu = CpuDelta(pid, proc.TotalProcessorTime, now, cores); }
                catch { denied = true; }
            }

            var meta = GetMeta(pid, proc);
            samples.Add(new ProcessSample
            {
                Pid          = pid,
                ParentPid    = parent,
                Name         = name,
                CpuPercent   = cpu,
                PrivateBytes = priv,
                WorkingSet   = ws,
                Description  = meta.Desc,
                Company      = meta.Company,
                Path         = meta.Path,
                AccessDenied = denied || meta.Denied,
                StartTime    = meta.Start,
            });
        }

        PruneDead(live);

        var (load, total, avail) = NativeMethods.ReadMemoryStatus();
        return new ProcessSnapshot
        {
            Processes          = samples,
            SystemCpuPercent   = ComputeSystemCpu(),
            MemoryLoadPercent  = load,
            TotalPhysicalBytes = total,
            UsedPhysicalBytes  = total - avail,
            ProcessorCount     = cores,
        };
    }

    public ProcessDetail? ReadDetail(int pid)
    {
        Process proc;
        try { proc = Process.GetProcessById(pid); }
        catch { return null; }

        using (proc)
        {
            try { proc.Refresh(); } catch { /* keep going with what we have */ }

            int cores = Math.Max(1, Environment.ProcessorCount);

            // Parent + names come from the Toolhelp snapshot (no handle needed).
            var byPid = NativeMethods.SnapshotProcesses().ToDictionary(e => e.Pid);
            int parentPid   = byPid.TryGetValue(pid, out var me) ? me.ParentPid : 0;
            string name     = byPid.TryGetValue(pid, out var self) ? self.Name : SafeName(proc);
            string parentNm = byPid.TryGetValue(parentPid, out var par) ? par.Name : "";

            var meta = ReadMeta(proc);

            IntPtr handle = IntPtr.Zero;
            try { handle = proc.Handle; } catch { /* protected */ }
            var (gdi, usr) = NativeMethods.ReadGuiResources(handle);
            string arch = NativeMethods.GetArchitecture(handle);

            double cpu = 0;
            try { cpu = CpuDelta(pid, proc.TotalProcessorTime, Stopwatch.GetTimestamp(), cores); } catch { }

            var (cmdLine, user) = ReadWmi(pid);
            var (threads, threadsDenied) = ReadThreads(proc);
            var (modules, modulesDenied) = ReadModules(proc);

            return new ProcessDetail
            {
                Pid           = pid,
                Name          = name,
                ParentPid     = parentPid,
                ParentName    = parentNm,
                Path          = meta.Path,
                CommandLine   = cmdLine,
                User          = user,
                Description   = meta.Desc,
                Company       = meta.Company,
                FileVersion   = meta.Version,
                Architecture  = arch,
                PriorityClass = SafePriority(proc),
                StartTime     = meta.Start,
                CpuPercent    = cpu,
                PrivateBytes  = SafeLong(() => proc.PrivateMemorySize64),
                WorkingSet    = SafeLong(() => proc.WorkingSet64),
                PeakWorkingSet = SafeLong(() => proc.PeakWorkingSet64),
                ThreadCount   = threads.Count,
                HandleCount   = SafeInt(() => proc.HandleCount),
                GdiObjects    = gdi,
                UserObjects   = usr,
                Threads       = threads,
                ThreadsDenied = threadsDenied,
                Modules       = modules,
                ModulesDenied = modulesDenied,
            };
        }
    }

    // ── CPU delta (shared by Snapshot + ReadDetail) ──────────────────────────
    private double CpuDelta(int pid, TimeSpan cpuNow, long nowTicks, int cores)
    {
        double cpu = _cpu.TryGetValue(pid, out var prev)
            ? CpuMath.Percent(prev.cpu, cpuNow, prev.ts, nowTicks, Stopwatch.Frequency, cores)
            : 0;
        _cpu[pid] = (cpuNow, nowTicks);
        return cpu;
    }

    private double ComputeSystemCpu()
    {
        var (idle, kernel, user) = NativeMethods.ReadSystemTimes();
        double result = 0;
        if (_prevSys is { } prev)
        {
            ulong dIdle  = idle - prev.idle;
            ulong dTotal = (kernel + user) - (prev.kernel + prev.user);
            if (dTotal > 0) result = Math.Clamp((1.0 - (double)dIdle / dTotal) * 100.0, 0, 100);
        }
        _prevSys = (idle, kernel, user);
        return result;
    }

    // ── Process-handle cache ──────────────────────────────────────────────────
    private Process? GetOrOpen(int pid)
    {
        if (_procs.TryGetValue(pid, out var p)) return p;
        try
        {
            var np = Process.GetProcessById(pid);
            _procs[pid] = np;
            return np;
        }
        catch { return null; }
    }

    private void PruneDead(HashSet<int> live)
    {
        foreach (var k in _procs.Keys.Where(k => !live.Contains(k)).ToList())
        {
            try { _procs[k].Dispose(); } catch { /* may have vanished */ }
            _procs.Remove(k);
        }
        foreach (var k in _cpu.Keys.Where(k => !live.Contains(k)).ToList())  _cpu.Remove(k);
        foreach (var k in _meta.Keys.Where(k => !live.Contains(k)).ToList()) _meta.Remove(k);
    }

    // ── Metadata (cached per PID) ─────────────────────────────────────────────
    private ProcMeta GetMeta(int pid, Process? proc)
    {
        if (_meta.TryGetValue(pid, out var m)) return m;
        m = ReadMeta(proc);
        _meta[pid] = m;
        return m;
    }

    private static ProcMeta ReadMeta(Process? proc)
    {
        if (proc is null) return new ProcMeta { Denied = true };
        var meta = new ProcMeta();
        try { meta.Start = proc.StartTime; } catch { /* protected */ }
        try
        {
            var mm = proc.MainModule;
            if (mm is not null)
            {
                meta.Path = mm.FileName ?? "";
                var fvi = mm.FileVersionInfo;
                meta.Desc    = fvi.FileDescription?.Trim() ?? "";
                meta.Company = fvi.CompanyName?.Trim() ?? "";
                meta.Version = fvi.FileVersion?.Trim() ?? "";
            }
        }
        catch { meta.Denied = true; }
        return meta;
    }

    // ── WMI: command line + owner (degrade to blanks; often null without elevation) ──
    private static (string CommandLine, string User) ReadWmi(int pid)
    {
        string cmd = "", user = "";
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementObject mo in searcher.Get())
            {
                cmd = mo["CommandLine"]?.ToString()?.Trim() ?? "";
                try
                {
                    var outParams = mo.InvokeMethod("GetOwner", null, null);
                    var domain = outParams?["Domain"]?.ToString();
                    var u      = outParams?["User"]?.ToString();
                    if (!string.IsNullOrEmpty(u))
                        user = string.IsNullOrEmpty(domain) ? u : $"{domain}\\{u}";
                }
                catch { /* GetOwner denied */ }
                break;
            }
        }
        catch { /* WMI unavailable / denied */ }
        return (cmd, user);
    }

    private static (IReadOnlyList<ThreadInfo>, bool denied) ReadThreads(Process proc)
    {
        try
        {
            var list = new List<ThreadInfo>();
            foreach (ProcessThread t in proc.Threads)
            {
                try
                {
                    list.Add(new ThreadInfo
                    {
                        Tid                = t.Id,
                        State              = t.ThreadState.ToString(),
                        Priority           = SafeEnum(() => t.PriorityLevel.ToString()),
                        TotalProcessorTime = SafeTime(() => t.TotalProcessorTime),
                        StartTime          = SafeNullableDate(() => t.StartTime),
                    });
                }
                catch { list.Add(new ThreadInfo { Tid = t.Id }); }
            }
            return (list, false);
        }
        catch { return ([], true); }
    }

    private static (IReadOnlyList<ModuleInfo>, bool denied) ReadModules(Process proc)
    {
        try
        {
            var list = new List<ModuleInfo>();
            foreach (ProcessModule m in proc.Modules)
            {
                try
                {
                    list.Add(new ModuleInfo
                    {
                        Name    = m.ModuleName ?? "",
                        Path    = m.FileName ?? "",
                        Version = m.FileVersionInfo.FileVersion?.Trim() ?? "",
                        Company = m.FileVersionInfo.CompanyName?.Trim() ?? "",
                        Size    = m.ModuleMemorySize,
                    });
                }
                catch { /* skip a module we can't read */ }
            }
            return (list, false);
        }
        catch { return ([], true); }
    }

    // ── Small guards ──────────────────────────────────────────────────────────
    private static string SafeName(Process p) { try { return p.ProcessName; } catch { return ""; } }
    private static string SafePriority(Process p) { try { return p.PriorityClass.ToString(); } catch { return ""; } }
    private static string SafeEnum(Func<string> f) { try { return f(); } catch { return ""; } }
    private static long SafeLong(Func<long> f) { try { return f(); } catch { return 0; } }
    private static int SafeInt(Func<int> f) { try { return f(); } catch { return 0; } }
    private static TimeSpan SafeTime(Func<TimeSpan> f) { try { return f(); } catch { return TimeSpan.Zero; } }
    private static DateTime? SafeNullableDate(Func<DateTime> f) { try { return f(); } catch { return null; } }

    public void Dispose()
    {
        foreach (var p in _procs.Values) { try { p.Dispose(); } catch { } }
        _procs.Clear();
        _cpu.Clear();
        _meta.Clear();
    }

    private sealed class ProcMeta
    {
        public string Desc = "";
        public string Company = "";
        public string Path = "";
        public string Version = "";
        public DateTime? Start;
        public bool Denied;
    }
}
