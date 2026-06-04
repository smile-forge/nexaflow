using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Core.Services.ShellAi;

/// <summary>Opens a page/tab in the shell. Valid kinds + params come from the shell context catalogue.</summary>
public sealed class OpenPageTool(IShellServices shell) : IClientTool
{
    public string Name => "open_page";
    public string Description => "Open a page/tab in the app. Use the 'Openable pages' list from context for valid kinds and their params.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("page_kind", "The page kind to open, e.g. \"Projects\" or \"FileSystem\"."),
        new("params", "Optional page parameters as a JSON object of string values, e.g. {\"mode\":\"path\",\"path\":\"C:\\\\Temp\"}.", Required: false, Type: "object"),
    ];
    public ToolSafety Safety => ToolSafety.RequiresApproval;
    public bool Parallelizable => false;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var kind = Str(arguments, "page_kind") ?? Str(arguments, "pageKind") ?? Str(arguments, "kind");
        if (string.IsNullOrWhiteSpace(kind))
            return Task.FromResult(ToolResult.Error("missing page_kind", "Provide a 'page_kind' to open."));

        Dictionary<string, string>? dict = null;
        if (arguments["params"] is JsonObject po && po.Count > 0)
        {
            dict = [];
            foreach (var kv in po)
                if (kv.Value is not null) dict[kv.Key] = kv.Value.ToString();
        }

        shell.OpenTab(kind, dict);   // self-marshals to the UI thread
        var paramText = dict is { Count: > 0 } ? $" with {string.Join(", ", dict.Select(kv => $"{kv.Key}={kv.Value}"))}" : string.Empty;
        return Task.FromResult(ToolResult.Ok($"opened {kind}", $"Opened the {kind} page{paramText}."));
    }

    private static string? Str(JsonObject o, string key)
        => o.TryGetPropertyValue(key, out var v) && v is not null ? v.ToString() : null;
}
