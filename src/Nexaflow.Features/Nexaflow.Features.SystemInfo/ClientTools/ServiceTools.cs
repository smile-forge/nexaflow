using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.ViewModels;

namespace Nexaflow.Features.SystemInfo.ClientTools;

/// <summary>
/// AI tools for the Windows Services page. Read tools auto-run; the mutating tools require approval and
/// then run through the elevated bridge (so an AI-driven change is gated twice: tool approval + UAC).
/// </summary>
public static class ServiceTools
{
    private static string Describe(ServiceRow r) =>
        $"{r.DisplayName} ({r.Name}) — {r.Status}, startup: {r.StartMode}";

    private static ToolResult FromElevated(ElevatedResult res)
    {
        if (res.WasDeclined) return ToolResult.Error("Administrator approval was declined.", res.Message);
        return res.Success ? ToolResult.Ok(res.Message) : ToolResult.Error(res.Message);
    }

    // ── Read ──────────────────────────────────────────────────────────────────────────────────
    public sealed class GetServices(ServicesViewModel vm) : ClientToolBase
    {
        public override string Name => "get_services";
        public override string Description => "List Windows services, optionally filtered by state (running, stopped, paused).";
        public override IReadOnlyList<ClientToolParameter> Parameters =>
        [
            new("state", "Optional state filter: running, stopped, or paused.", Required: false),
        ];
        public override bool Parallelizable => true;

        public override Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            var state = ToolArgs.Str(arguments, "state", "status");
            var rows = vm.Snapshot();
            if (!string.IsNullOrWhiteSpace(state))
                rows = rows.Where(r => r.Status.Equals(state, StringComparison.OrdinalIgnoreCase)).ToList();

            if (rows.Count == 0)
                return Task.FromResult(ToolResult.Ok("no services", "No matching services."));

            var sb = new StringBuilder($"{rows.Count} service(s):\n");
            foreach (var r in rows) sb.Append("• ").AppendLine(Describe(r));
            return Task.FromResult(ToolResult.Ok($"{rows.Count} service(s)", sb.ToString().TrimEnd()));
        }
    }

    public sealed class FindService(ServicesViewModel vm) : ClientToolBase
    {
        public override string Name => "find_service";
        public override string Description => "Find services whose name or display name contains the query.";
        public override IReadOnlyList<ClientToolParameter> Parameters =>
        [
            new("query", "Substring to match against the service name or display name."),
        ];
        public override bool Parallelizable => true;

        public override Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            var q = ToolArgs.Str(arguments, "query", "name", "service");
            if (string.IsNullOrWhiteSpace(q))
                return Task.FromResult(ToolResult.Error("No query provided."));

            var matches = vm.Snapshot()
                .Where(r => r.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
                         || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
                return Task.FromResult(ToolResult.Ok("no matches", $"No services match '{q}'."));

            var sb = new StringBuilder($"{matches.Count} match(es) for '{q}':\n");
            foreach (var r in matches) sb.Append("• ").AppendLine(Describe(r));
            return Task.FromResult(ToolResult.Ok($"{matches.Count} match(es)", sb.ToString().TrimEnd()));
        }
    }

    public sealed class GetService(ServicesViewModel vm) : ClientToolBase
    {
        public override string Name => "get_service";
        public override string Description => "Get one service's status, startup type and description.";
        public override IReadOnlyList<ClientToolParameter> Parameters =>
        [
            new("name", "Service name or display name."),
        ];
        public override bool Parallelizable => true;

        public override Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            var name = ToolArgs.Str(arguments, "name", "service", "query");
            if (string.IsNullOrWhiteSpace(name))
                return Task.FromResult(ToolResult.Error("No service name provided."));
            if (vm.Find(name) is not { } r)
                return Task.FromResult(ToolResult.Error($"Service '{name}' not found."));

            var text = $"{Describe(r)}\nCan pause/continue: {r.CanPauseAndContinue}\n" +
                       $"Description: {(string.IsNullOrWhiteSpace(r.Description) ? "—" : r.Description)}";
            return Task.FromResult(ToolResult.Ok(Describe(r), text));
        }
    }

    // ── Act (approval-gated → bridge → UAC) ─────────────────────────────────────────────────────
    // Parallelizable stays at the base default (false) so UAC prompts never stack.
    public abstract class ControlToolBase(ServicesViewModel vm, string op) : ClientToolBase
    {
        protected readonly ServicesViewModel Vm = vm;
        public override IReadOnlyList<ClientToolParameter> Parameters => [new("name", "Service name or display name.")];
        public override ToolSafety Safety => ToolSafety.RequiresApproval;

        public override async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            var name = ToolArgs.Str(arguments, "name", "service");
            if (string.IsNullOrWhiteSpace(name))
                return ToolResult.Error("No service name provided.");
            if (Vm.Find(name) is not { } row)
                return ToolResult.Error($"Service '{name}' not found.");
            return FromElevated(await Vm.ControlAsync(row, op));
        }
    }

    public sealed class StartService(ServicesViewModel vm) : ControlToolBase(vm, ElevatedOps.ServiceStart)
    {
        public override string Name => "start_service";
        public override string Description => "Start a Windows service (requires admin approval).";
    }

    public sealed class StopService(ServicesViewModel vm) : ControlToolBase(vm, ElevatedOps.ServiceStop)
    {
        public override string Name => "stop_service";
        public override string Description => "Stop a Windows service, and any services depending on it (requires admin approval).";
    }

    public sealed class RestartService(ServicesViewModel vm) : ControlToolBase(vm, ElevatedOps.ServiceRestart)
    {
        public override string Name => "restart_service";
        public override string Description => "Restart a Windows service (requires admin approval).";
    }

    public sealed class SetServiceStartMode(ServicesViewModel vm) : ClientToolBase
    {
        public override string Name => "set_service_start_mode";
        public override string Description => "Change a service's startup type: Automatic, AutomaticDelayed, Manual or Disabled (requires admin approval).";
        public override IReadOnlyList<ClientToolParameter> Parameters =>
        [
            new("name", "Service name or display name."),
            new("start_mode", "One of: Automatic, AutomaticDelayed, Manual, Disabled."),
        ];
        public override ToolSafety Safety => ToolSafety.RequiresApproval;

        public override async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            var name = ToolArgs.Str(arguments, "name", "service");
            var mode = ToolArgs.Str(arguments, "start_mode", "startMode", "mode");
            if (string.IsNullOrWhiteSpace(name)) return ToolResult.Error("No service name provided.");
            if (string.IsNullOrWhiteSpace(mode)) return ToolResult.Error("No start mode provided.");
            if (vm.Find(name) is not { } row) return ToolResult.Error($"Service '{name}' not found.");
            return FromElevated(await vm.SetStartModeAsync(row, mode));
        }
    }
}
