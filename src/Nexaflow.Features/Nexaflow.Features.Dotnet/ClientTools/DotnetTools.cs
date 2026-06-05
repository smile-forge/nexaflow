using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Dotnet.ViewModels;

namespace Nexaflow.Features.Dotnet.ClientTools;

internal static class DotnetToolArgs
{
    public static string? Str(JsonObject o, string key)
        => o.TryGetPropertyValue(key, out var v) && v is not null ? v.GetValue<string>() : null;
}

/// <summary>
/// Runs one dotnet verb (build/test/restore/clean) against the viewlet's selected target and feeds the
/// captured output back to the model. Mutating (writes build artefacts / runs code) → requires approval.
/// </summary>
public sealed class DotnetVerbTool(
    DotnetViewletViewModel vm, string name, string verb, string description) : IClientTool
{
    public string Name => name;
    public string Description => $"{description} (runs `dotnet {verb}`) and return its output.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("target", "Optional target display name to act on when the folder has several (a .sln or a project). Defaults to the selected target.", Required: false),
    ];
    public ToolSafety Safety => ToolSafety.RequiresApproval;
    public bool Parallelizable => false;   // shares build state; the VM also guards with IsBusy

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var targetName = DotnetToolArgs.Str(arguments, "target");
        if (!string.IsNullOrWhiteSpace(targetName))
            vm.SelectedTarget = vm.ResolveTarget(targetName);

        if (vm.SelectedTarget is null)
            return ToolResult.Error($"No .NET target available to {verb}.");
        if (vm.IsBusy)
            return ToolResult.Error("A dotnet command is already running; try again once it finishes.");

        var result = await vm.RunVerbAsync(verb, ct);
        if (result is null)
            return ToolResult.Error($"Could not start dotnet {verb}.");

        var tail = DotnetViewletViewModel.Tail(result.Output, 200);
        return result.Succeeded
            ? ToolResult.Ok($"dotnet {verb} succeeded",
                            string.IsNullOrWhiteSpace(tail) ? $"dotnet {verb} succeeded (exit 0)." : tail)
            : ToolResult.Error($"dotnet {verb} failed (exit {result.ExitCode})", tail);
    }
}

/// <summary>Read-only: lists outdated NuGet packages for the selected target (name, current, latest).</summary>
public sealed class DotnetOutdatedPackagesTool(DotnetViewletViewModel vm) : IClientTool
{
    public string Name => "dotnet_check_outdated_packages";
    public string Description => "Check the .NET target for outdated NuGet packages, returning each package's current and latest version.";
    public IReadOnlyList<ClientToolParameter> Parameters => [];
    public ToolSafety Safety => ToolSafety.ReadOnly;
    public bool Parallelizable => true;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (vm.SelectedTarget is null)
            return ToolResult.Error("No .NET target available.");

        var updates = await vm.CheckOutdatedAsync(ct);
        if (updates.Count == 0)
            return ToolResult.Ok("All packages up to date",
                                 "All NuGet packages are up to date (or the target hasn't been restored yet).");

        var sb = new StringBuilder();
        foreach (var u in updates)
            sb.AppendLine($"{u.Name}: {u.Current} → {u.Latest}");
        return ToolResult.Ok($"{updates.Count} package update(s) available", sb.ToString().TrimEnd());
    }
}
