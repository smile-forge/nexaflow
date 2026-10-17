using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common;

namespace Nexaflow.Core.Services.ShellAi;

/// <summary>
/// Opens a page/tab in the shell. Valid kinds + params come from <see cref="GetOpenablePagesTool"/>: a parameter the page
/// does not declare is refused, and each one naming a place on disk (<see cref="PageParameter.OnDisk"/>) must be inside what
/// the user has given the assistant — a file open in a tab, or in a folder open in one.
/// </summary>
/// <param name="parameters">The parameters a page kind declares, or null where there is no such kind.</param>
/// <param name="scope">What the user has given the assistant.</param>
public sealed class OpenPageTool(IShellServices shell, Func<string, IReadOnlyList<PageParameter>?> parameters, PathScope scope) : IClientTool
{
    public string Name => "open_page";
    public string Description => "Open a page/tab in the app. get_openable_pages lists the kinds of page and the parameters each takes.";
    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("page_kind", "The page kind to open, e.g. \"Projects\" or \"FileSystem\"."),
        new("params", "Optional page parameters as a JSON object of string values, e.g. {\"path\":\"C:\\\\Temp\\\\notes.pdf\"}. A path is to a file open in a tab, or in a folder open in one.", Required: false, Type: "object"),
    ];
    public ToolSafety Safety => ToolSafety.RequiresApproval;
    public bool Parallelizable => false;

    public Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var kind = Str(arguments, "page_kind") ?? Str(arguments, "pageKind") ?? Str(arguments, "kind");
        if (string.IsNullOrWhiteSpace(kind))
            return Task.FromResult(ToolResult.Error("missing page_kind", "Provide a 'page_kind' to open."));
        if (parameters(kind) is not { } declared)
            return Task.FromResult(ToolResult.Error("no such page", $"There is no '{kind}' page: get_openable_pages lists them."));

        Dictionary<string, string>? dict = null;
        if (arguments["params"] is JsonObject po && po.Count > 0)
        {
            dict = [];
            foreach (var kv in po)
                if (kv.Value is not null) dict[kv.Key] = kv.Value.ToString();
        }

        if (dict is not null && Refusal(kind, declared, dict) is { } refused)
            return Task.FromResult(refused);

        shell.OpenTab(kind, dict);   // self-marshals to the UI thread
        var paramText = dict is { Count: > 0 } ? $" with {string.Join(", ", dict.Select(kv => $"{kv.Key}={kv.Value}"))}" : string.Empty;
        return Task.FromResult(ToolResult.Ok($"opened {kind}", $"Opened the {kind} page{paramText}."));
    }

    /// <summary>Why the page cannot be opened with <paramref name="given"/>: a parameter it does not declare, or a place on
    /// disk outside the scope; null where it can.</summary>
    private ToolResult? Refusal(string kind, IReadOnlyList<PageParameter> declared, Dictionary<string, string> given)
    {
        foreach (var (key, value) in given)
        {
            if (declared.FirstOrDefault(p => p.Name == key) is not { } parameter)
                return ToolResult.Error("no such parameter",
                    $"The {kind} page takes {(declared.Count == 0 ? "no parameters" : string.Join(", ", declared.Select(p => p.Name)))}, not '{key}'.");
            if (!parameter.OnDisk) continue;
            foreach (var place in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!IsWebAddress(place) && scope.Resolve(place) is null)
                    return ToolResult.Error("outside what you were given", Agent.RunScope.Outside(place));
        }
        return null;
    }

    /// <summary>Whether <paramref name="value"/> is a web address, which names no place on disk.</summary>
    private static bool IsWebAddress(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var address) && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps);

    private static string? Str(JsonObject o, string key)
        => o.TryGetPropertyValue(key, out var v) && v is not null ? v.ToString() : null;
}
