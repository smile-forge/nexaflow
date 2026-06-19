using System.Diagnostics;
using System.Management;
using System.Text.Json;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>
/// Read-only deep inspection of a process with admin rights. Always enumerates open handles (no managed
/// API — see <see cref="ProcessNative"/>); when <see cref="ElevatedArgs.InspectWhat"/> is "all" (the
/// default) it also re-reads the module list and command line, which a non-elevated host can't read for a
/// protected process. Results are JSON in the per-operation <see cref="ElevatedOperationResult.Data"/>.
/// </summary>
internal sealed class ProcessInspectOperation : ProcessOperationBase
{
    public override string Id => ElevatedOps.ProcessInspect;

    protected override ElevatedOperationResult Run(Process proc, int pid, IReadOnlyDictionary<string, string> args)
    {
        var what = args.GetValueOrDefault(ElevatedArgs.InspectWhat) ?? "all";
        var result = Ok("");

        var handles = ProcessNative.EnumerateHandles(pid);
        result.Data["handles"] = JsonSerializer.Serialize(handles);

        if (string.Equals(what, "all", StringComparison.OrdinalIgnoreCase))
        {
            try { result.Data["modules"] = JsonSerializer.Serialize(ReadModules(proc)); } catch { /* leave absent */ }
            try
            {
                var cmd = ReadCommandLine(pid);
                if (!string.IsNullOrEmpty(cmd)) result.Data["commandLine"] = cmd;
            }
            catch { /* leave absent */ }
        }

        result.Message = $"Inspected {SafeName(proc)} (PID {pid}): {handles.Count} handles.";
        return result;
    }

    private static List<ModuleRecord> ReadModules(Process proc)
    {
        var list = new List<ModuleRecord>();
        foreach (ProcessModule m in proc.Modules)
        {
            try
            {
                list.Add(new ModuleRecord
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
        return list;
    }

    private static string ReadCommandLine(int pid)
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
        foreach (ManagementObject mo in searcher.Get())
            return mo["CommandLine"]?.ToString()?.Trim() ?? "";
        return "";
    }

    /// <summary>Shaped to match the host's <c>ModuleInfo</c> JSON.</summary>
    private sealed class ModuleRecord
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Version { get; set; } = "";
        public string Company { get; set; } = "";
        public long Size { get; set; }
    }
}
