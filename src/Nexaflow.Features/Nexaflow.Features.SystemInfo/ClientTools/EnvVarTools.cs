using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.ViewModels;

namespace Nexaflow.Features.SystemInfo.ClientTools;

/// <summary>
/// AI tools for the Environment Variables page. Read tools auto-run; set/delete require approval and then
/// route by scope — User in-process, Machine through the elevated bridge (UAC).
/// </summary>
public static class EnvVarTools
{
    private const int ListValueCap = 300;

    private static bool TryScope(string? s, out EnvScope scope)
    {
        switch (s?.Trim().ToLowerInvariant())
        {
            case "user":    scope = EnvScope.User;    return true;
            case "machine": case "system": scope = EnvScope.Machine; return true;
            default:        scope = EnvScope.User;    return false;
        }
    }

    private static IEnumerable<EnvScope> ScopesFor(string? s) =>
        TryScope(s, out var scope) ? [scope] : [EnvScope.User, EnvScope.Machine];

    public sealed class GetEnvironmentVariables(EnvironmentVariablesViewModel vm) : IClientTool
    {
        public string Name => "get_environment_variables";
        public string Description => "List environment variables for a scope (user or machine) or both. " +
                                     "Long values are truncated — use get_environment_variable for the full value.";
        public IReadOnlyList<ClientToolParameter> Parameters =>
        [
            new("scope", "Optional: user or machine. Omit for both scopes.", Required: false),
        ];
        public ToolSafety Safety => ToolSafety.ReadOnly;
        public bool Parallelizable => true;

        public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            var scopeArg = ToolArgs.Str(arguments, "scope", "target");
            var sb = new StringBuilder();
            var count = 0;
            foreach (var scope in ScopesFor(scopeArg))
            {
                var rows = vm.ScopeRows(scope);
                if (rows.Count == 0) continue;
                sb.Append("── ").Append(scope).Append(" ──\n");
                foreach (var r in rows)
                {
                    var v = r.Value.Length > ListValueCap ? r.Value[..ListValueCap] + "… (truncated)" : r.Value;
                    sb.Append(r.Name).Append(" = ").AppendLine(v);
                    count++;
                }
            }
            return Task.FromResult(count == 0
                ? ToolResult.Ok("no variables", "No environment variables found.")
                : ToolResult.Ok($"{count} variable(s)", sb.ToString().TrimEnd()));
        }
    }

    public sealed class GetEnvironmentVariable(EnvironmentVariablesViewModel vm) : IClientTool
    {
        public string Name => "get_environment_variable";
        public string Description => "Get the full value of one environment variable (e.g. PATH).";
        public IReadOnlyList<ClientToolParameter> Parameters =>
        [
            new("name", "Variable name (e.g. PATH)."),
            new("scope", "Optional: user or machine. Omit to search both scopes.", Required: false),
        ];
        public ToolSafety Safety => ToolSafety.ReadOnly;
        public bool Parallelizable => true;

        public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            var name = ToolArgs.Str(arguments, "name", "variable");
            if (string.IsNullOrWhiteSpace(name))
                return Task.FromResult(ToolResult.Error("No variable name provided."));

            var scopeArg = ToolArgs.Str(arguments, "scope", "target");
            foreach (var scope in ScopesFor(scopeArg))
            {
                var row = vm.ScopeRows(scope).FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (row is null) continue;

                var body = row.IsPathLike
                    ? $"{row.Name} ({scope}) — {row.Entries.Count} entries:\n" + string.Join('\n', row.Entries)
                    : $"{row.Name} ({scope}) = {row.Value}";
                return Task.FromResult(ToolResult.Ok($"{row.Name} ({scope})", body));
            }
            return Task.FromResult(ToolResult.Error($"Variable '{name}' not found."));
        }
    }

    public sealed class SetEnvironmentVariable(EnvironmentVariablesViewModel vm) : IClientTool
    {
        public string Name => "set_environment_variable";
        public string Description => "Create or update an environment variable. User scope is immediate; " +
                                     "machine scope requires admin approval.";
        public IReadOnlyList<ClientToolParameter> Parameters =>
        [
            new("name", "Variable name."),
            new("value", "New value."),
            new("scope", "user (default) or machine.", Required: false),
        ];
        public ToolSafety Safety => ToolSafety.RequiresApproval;
        public bool Parallelizable => false;

        public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            var name  = ToolArgs.Str(arguments, "name", "variable");
            var value = ToolArgs.Str(arguments, "value") ?? "";
            if (string.IsNullOrWhiteSpace(name)) return ToolResult.Error("No variable name provided.");

            TryScope(ToolArgs.Str(arguments, "scope", "target"), out var scope);
            var ok = await vm.SetVariableAsync(scope, name, value);
            return ok
                ? ToolResult.Ok($"Set {scope} variable {name}.")
                : ToolResult.Error($"Could not set {scope} variable {name} (declined or failed).");
        }
    }

    public sealed class DeleteEnvironmentVariable(EnvironmentVariablesViewModel vm) : IClientTool
    {
        public string Name => "delete_environment_variable";
        public string Description => "Delete an environment variable. User scope is immediate; machine scope requires admin approval.";
        public IReadOnlyList<ClientToolParameter> Parameters =>
        [
            new("name", "Variable name."),
            new("scope", "user (default) or machine.", Required: false),
        ];
        public ToolSafety Safety => ToolSafety.RequiresApproval;
        public bool Parallelizable => false;

        public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
        {
            var name = ToolArgs.Str(arguments, "name", "variable");
            if (string.IsNullOrWhiteSpace(name)) return ToolResult.Error("No variable name provided.");

            TryScope(ToolArgs.Str(arguments, "scope", "target"), out var scope);
            var ok = await vm.DeleteVariableAsync(scope, name);
            return ok
                ? ToolResult.Ok($"Deleted {scope} variable {name}.")
                : ToolResult.Error($"Could not delete {scope} variable {name} (declined or failed).");
        }
    }
}
