using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Processes.Models;
using Nexaflow.Visuals.Common.Converters;
using Nexaflow.Features.Processes.ViewModels;

namespace Nexaflow.Features.Processes.ClientTools;

/// <summary>
/// The client tools the process page exposes to the AI: read-only queries (auto-run) plus the two
/// approval-gated mutations, which escalate through the privilege bridge on access denial. All read from
/// the page's live snapshot, so they reflect exactly what the user sees.
/// </summary>
internal static class ProcessTools
{
    public static IReadOnlyList<IClientTool> For(ProcessesViewModel vm) =>
    [
        new DelegateClientTool(
            "list_processes",
            "List running processes. Optional args: sort_by (cpu|memory|name|pid), top (max rows), filter (name substring).",
            [
                new ClientToolParameter("sort_by", "cpu | memory | name | pid (default cpu).", Required: false),
                new ClientToolParameter("top", "Maximum number of rows to return.", Required: false, Type: "number"),
                new ClientToolParameter("filter", "Only processes whose name contains this text.", Required: false),
            ],
            ToolSafety.SafeOperation,
            (args, _) => Task.FromResult(ListProcesses(vm, args)),
            parallelizable: true),

        new DelegateClientTool(
            "get_top_consumers",
            "List the top processes by a resource. Args: metric (cpu|memory), count (default 5).",
            [
                new ClientToolParameter("metric", "cpu | memory.", Required: false),
                new ClientToolParameter("count", "How many to return (default 5).", Required: false, Type: "number"),
            ],
            ToolSafety.SafeOperation,
            (args, _) => Task.FromResult(TopConsumers(vm, args)),
            parallelizable: true),

        new DelegateClientTool(
            "get_process_details",
            "Full details for one process (path, command line, user, CPU, memory, threads, handles, priority). Arg: process (name or PID).",
            [new ClientToolParameter("process", "Process name or PID.")],
            ToolSafety.SafeOperation,
            (args, _) => Task.FromResult(Details(vm, args)),
            parallelizable: true),

        new DelegateClientTool(
            "get_process_modules",
            "List the DLLs / modules loaded by a process. Arg: process (name or PID).",
            [new ClientToolParameter("process", "Process name or PID.")],
            ToolSafety.SafeOperation,
            (args, _) => Task.FromResult(Modules(vm, args)),
            parallelizable: true),

        new DelegateClientTool(
            "get_process_tree",
            "Show the parent/child process tree, optionally rooted at one process. Arg: process (name or PID, optional).",
            [new ClientToolParameter("process", "Root process name or PID; omit for the whole tree.", Required: false)],
            ToolSafety.SafeOperation,
            (args, _) => Task.FromResult(Tree(vm, args)),
            parallelizable: true),

        new DelegateClientTool(
            "kill_process",
            "Terminate a process. Args: process (name or PID), tree (true to also kill its children). May prompt for admin.",
            [
                new ClientToolParameter("process", "Process name or PID."),
                new ClientToolParameter("tree", "true to also terminate child processes.", Required: false, Type: "boolean"),
            ],
            ToolSafety.RequiresApproval,
            (args, _) => Kill(vm, args)),

        new DelegateClientTool(
            "set_process_priority",
            "Change a process's priority. Args: process (name or PID), priority (Idle|BelowNormal|Normal|AboveNormal|High|RealTime). May prompt for admin.",
            [
                new ClientToolParameter("process", "Process name or PID."),
                new ClientToolParameter("priority", "Idle | BelowNormal | Normal | AboveNormal | High | RealTime."),
            ],
            ToolSafety.RequiresApproval,
            (args, _) => SetPriority(vm, args)),
    ];

    // ── Read tools ─────────────────────────────────────────────────────────────
    private static ToolResult ListProcesses(ProcessesViewModel vm, JsonObject args)
    {
        var rows = vm.RowsSnapshot();
        var filter = ToolArgs.Str(args, "filter");
        if (!string.IsNullOrWhiteSpace(filter))
            rows = rows.Where(r => r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        rows = SortRows(rows, ToolArgs.Str(args, "sort_by")).ToList();
        var top = ToolArgs.IntOrNull(args, "top");
        if (top is > 0) rows = rows.Take(top.Value).ToList();

        if (rows.Count == 0) return ToolResult.Ok("No matching processes", "No matching processes.");

        var sb = new StringBuilder();
        foreach (var r in rows) sb.AppendLine(Line(r));
        return ToolResult.Ok($"{rows.Count} processes", sb.ToString().TrimEnd());
    }

    private static ToolResult TopConsumers(ProcessesViewModel vm, JsonObject args)
    {
        var metric = (ToolArgs.Str(args, "metric") ?? "cpu").ToLowerInvariant();
        int count = ToolArgs.IntOrNull(args, "count") is > 0 and var c ? c : 5;
        var rows = vm.RowsSnapshot();
        rows = (metric.StartsWith("mem")
                   ? rows.OrderByDescending(r => r.WorkingSet)
                   : rows.OrderByDescending(r => r.CpuPercent))
               .Take(count).ToList();

        var sb = new StringBuilder($"Top {rows.Count} by {(metric.StartsWith("mem") ? "memory" : "CPU")}:");
        sb.AppendLine();
        foreach (var r in rows) sb.AppendLine(Line(r));
        return ToolResult.Ok($"Top {rows.Count} by {metric}", sb.ToString().TrimEnd());
    }

    private static ToolResult Details(ProcessesViewModel vm, JsonObject args)
    {
        if (Resolve(vm, args) is not { } row) return NotFound(args);
        var d = vm.ReadDetail(row.Pid);
        if (d is null) return ToolResult.Error("Gone", $"Process {row.Pid} is no longer running.");

        var sb = new StringBuilder();
        sb.AppendLine($"Name: {d.Name} (PID {d.Pid})");
        sb.AppendLine($"Parent: {(d.ParentName.Length > 0 ? $"{d.ParentName} (PID {d.ParentPid})" : d.ParentPid.ToString())}");
        sb.AppendLine($"Description: {Dash(d.Description)}");
        sb.AppendLine($"Company: {Dash(d.Company)}");
        sb.AppendLine($"Version: {Dash(d.FileVersion)}    Architecture: {Dash(d.Architecture)}");
        sb.AppendLine($"Path: {Dash(d.Path)}");
        sb.AppendLine($"Command line: {Dash(d.CommandLine)}");
        sb.AppendLine($"User: {Dash(d.User)}");
        sb.AppendLine($"Started: {(d.StartTime?.ToString("g") ?? "—")}    Priority: {Dash(d.PriorityClass)}");
        sb.AppendLine($"CPU: {d.CpuPercent:0.0}%    Private bytes: {BytesToTextConverter.Format(d.PrivateBytes)}    " +
                      $"Working set: {BytesToTextConverter.Format(d.WorkingSet)}");
        sb.AppendLine($"Threads: {d.ThreadCount}    Handles: {d.HandleCount}    GDI: {d.GdiObjects}    USER: {d.UserObjects}");
        return ToolResult.Ok($"Details for {d.Name}", sb.ToString().TrimEnd());
    }

    private static ToolResult Modules(ProcessesViewModel vm, JsonObject args)
    {
        if (Resolve(vm, args) is not { } row) return NotFound(args);
        var d = vm.ReadDetail(row.Pid);
        if (d is null) return ToolResult.Error("Gone", $"Process {row.Pid} is no longer running.");
        if (d.ModulesDenied)
            return ToolResult.Ok($"Modules unavailable for {d.Name}",
                $"The modules for {d.Name} (PID {d.Pid}) require administrator rights to read.");
        if (d.Modules.Count == 0)
            return ToolResult.Ok($"No modules for {d.Name}", $"No modules reported for {d.Name} (PID {d.Pid}).");

        var sb = new StringBuilder($"{d.Modules.Count} modules in {d.Name} (PID {d.Pid}):");
        sb.AppendLine();
        foreach (var m in d.Modules)
            sb.AppendLine($"  {m.Name}  {Dash(m.Version)}  {BytesToTextConverter.Format(m.Size)}  {m.Path}");
        return ToolResult.Ok($"{d.Modules.Count} modules", sb.ToString().TrimEnd());
    }

    private static ToolResult Tree(ProcessesViewModel vm, JsonObject args)
    {
        var rows = vm.RowsSnapshot();
        var sb = new StringBuilder();
        var process = ToolArgs.Str(args, "process");
        if (!string.IsNullOrWhiteSpace(process))
        {
            if (Resolve(vm, args) is not { } root) return NotFound(args);
            Walk(root, 0, sb);
        }
        else
        {
            foreach (var r in rows.Where(r => r.Parent is null).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
                Walk(r, 0, sb);
        }
        return ToolResult.Ok("Process tree", sb.ToString().TrimEnd());

        static void Walk(ProcessRowViewModel r, int depth, StringBuilder sb)
        {
            sb.AppendLine($"{new string(' ', depth * 2)}{r.Name} (PID {r.Pid})");
            foreach (var c in r.Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
                Walk(c, depth + 1, sb);
        }
    }

    // ── Act tools ─────────────────────────────────────────────────────────────
    private static async Task<ToolResult> Kill(ProcessesViewModel vm, JsonObject args)
    {
        if (Resolve(vm, args) is not { } row) return NotFound(args);
        bool tree = ToolArgs.Bool(args, "tree");
        var msg = await vm.KillByToolAsync(row.Pid, tree);
        return ToolResult.Ok(msg);
    }

    private static async Task<ToolResult> SetPriority(ProcessesViewModel vm, JsonObject args)
    {
        if (Resolve(vm, args) is not { } row) return NotFound(args);
        var priority = ToolArgs.Str(args, "priority");
        if (string.IsNullOrWhiteSpace(priority))
            return ToolResult.Error("No priority", "The 'priority' argument is required.");
        var msg = await vm.SetPriorityByToolAsync(row.Pid, priority);
        return ToolResult.Ok(msg);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────
    private static ProcessRowViewModel? Resolve(ProcessesViewModel vm, JsonObject args)
    {
        var q = ToolArgs.Str(args, "process");
        return string.IsNullOrWhiteSpace(q) ? null : vm.FindByNameOrPid(q);
    }

    private static ToolResult NotFound(JsonObject args) =>
        ToolResult.Error("Not found", $"No running process matches '{ToolArgs.Str(args, "process")}'.");

    private static IEnumerable<ProcessRowViewModel> SortRows(IReadOnlyList<ProcessRowViewModel> rows, string? by) =>
        (by?.ToLowerInvariant()) switch
        {
            "memory" or "mem" => rows.OrderByDescending(r => r.WorkingSet),
            "name"            => rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            "pid"             => rows.OrderBy(r => r.Pid),
            _                 => rows.OrderByDescending(r => r.CpuPercent),
        };

    private static string Line(ProcessRowViewModel r) =>
        $"PID {r.Pid,-6} {r.Name,-28} CPU {r.CpuPercent,5:0.0}%  Priv {BytesToTextConverter.Format(r.PrivateBytes),-9}  " +
        $"WS {BytesToTextConverter.Format(r.WorkingSet),-9}  {r.Company}";

    private static string Dash(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
}
