using System.Text.Json;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.Models;

namespace Nexaflow.Features.Processes.Services;

/// <summary>
/// Drives the elevated <c>process.inspect</c> operation: enumerates a process's open handles (and, in "all"
/// mode, re-reads its modules + command line) through the privilege bridge — one UAC prompt — and parses the
/// JSON the bridge returns. Read-only counterpart to <see cref="ProcessActions"/>; never throws (a bad
/// payload degrades to empty), so the view-model stays thin.
/// </summary>
internal static class ProcessInspect
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    internal sealed record InspectResult(
        bool Success, bool Declined, string Message,
        IReadOnlyList<HandleInfo> Handles,
        IReadOnlyList<ModuleInfo>? Modules,   // null = not returned
        string? CommandLine);

    public static async Task<InspectResult> InspectAsync(IShellServices shell, int pid, string what = "all")
    {
        var res = await shell.RunElevatedAsync(ElevatedRequest.Single(ElevatedOps.ProcessInspect,
            (ElevatedArgs.ProcessId, pid.ToString()),
            (ElevatedArgs.InspectWhat, what)));

        if (res.WasDeclined)
            return new InspectResult(false, true, res.Message, [], null, null);
        if (!res.Success)
            return new InspectResult(false, false, res.Message, [], null, null);

        var data = res.Operations.Count > 0 ? res.Operations[0].Data : new();
        var handles = Deserialize<List<HandleInfo>>(data.GetValueOrDefault("handles")) ?? [];
        var modules = data.ContainsKey("modules") ? Deserialize<List<ModuleInfo>>(data["modules"]) : null;
        var cmdLine = data.GetValueOrDefault("commandLine");

        return new InspectResult(true, false, res.Message, handles, modules, cmdLine);
    }

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, JsonOpts); }
        catch { return null; }
    }
}
