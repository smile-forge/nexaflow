using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.UsbProbe.ViewModels;
using Nexaflow.IO.Usb.Tools;

namespace Nexaflow.Features.UsbProbe.ClientTools;

/// <summary>
/// The agent's hands on a device's live tools. Every tool publishes its own operations; <c>usb_device_tools</c> lists
/// them and <c>usb_tool_run</c> runs any of them, its safety decided per call by the operation it names.
/// </summary>
public static class UsbDeviceTools
{
    public static IReadOnlyList<IClientTool> For(UsbDeviceViewModel page) =>
    [
        new DelegateClientTool("usb_device_tools",
            "Lists the device's live tools: what each works on, what it shows right now, and the operations it offers with their arguments.",
            [],
            ToolSafety.SafeOperation,
            (_, _) => Task.FromResult(ToolResult.Ok($"{page.Tiles.OfType<UsbToolTile>().Count()} tool(s) on {page.Title}", DescribeTools(page))),
            parallelizable: true),
        new UsbToolRunTool(page),
    ];

    /// <summary>Every tile: its key and id, what it works on, its state, its operations; then what no tool covers.</summary>
    public static string DescribeTools(UsbDeviceViewModel page)
    {
        var sb = new StringBuilder();
        var tiles = page.Tiles.OfType<UsbToolTile>().ToList();
        sb.AppendLine(tiles.Count == 0 ? "No live tools on this device." : $"Live tools ({tiles.Count}):");
        foreach (var tile in tiles)
        {
            var tool = tile.Tool;
            sb.AppendLine($"- {tool.Title} — key '{tool.Key}', id '{tool.ToolId}'{(tile.FunctionName is { } f ? $", on {f}" : "")}: {tool.Status}");
            foreach (var line in tool.Describe().Split('\n', StringSplitOptions.RemoveEmptyEntries)) sb.AppendLine($"    {line.TrimEnd()}");
            foreach (var op in tool.Operations)
            {
                string args = string.Join(", ", op.Arguments.Select(a => a.Required ? a.Name : a.Name + "?"));
                sb.AppendLine($"    op {op.Name}({args}){(op.ChangesSomething ? " — asks the user first" : "")}: {op.Description}");
                foreach (var a in op.Arguments) sb.AppendLine($"      {a.Name}: {a.Description}");
            }
        }
        foreach (var uncovered in page.Tiles.OfType<UsbUncoveredTile>().SelectMany(t => t.Functions))
            sb.AppendLine($"- Not covered: {uncovered.Name} ({uncovered.ClassName}) — {uncovered.Guidance}");
        return sb.ToString();
    }
}

/// <summary>
/// Runs one live tool's operation. An operation that only reads — listing a folder, reading a feature report, reading
/// the levels — runs at once; one that changes something — a copy, a sound, a recording, a report sent — is put to the
/// user first. The operation named decides, call by call, which is why this is one tool and not two.
/// </summary>
public sealed class UsbToolRunTool(UsbDeviceViewModel page) : ClientToolBase, IClientTool
{
    public override string Name => "usb_tool_run";

    public override string Description =>
        "Runs one of the device's live-tool operations, as usb_device_tools lists them. Operations that write — copying, playing, recording, " +
        "sending a report — are approved by the user; the rest run at once.";

    public override IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("tool", "The tool's key or id, as usb_device_tools lists them (e.g. 'usb.storage')."),
        new("operation", "The operation's name."),
        new("arguments", "The operation's arguments as a JSON object, e.g. {\"path\":\"E:\\\\Photos\"}. Omit when it takes none.", Required: false),
    ];

    /// <summary>The worst an operation can be; <see cref="SafetyOf"/> says what this call actually is.</summary>
    public override ToolSafety Safety => ToolSafety.RequiresApproval;

    public ToolSafety SafetyOf(JsonObject arguments) =>
        Find(arguments, out _, out var op, out _) && !op!.ChangesSomething ? ToolSafety.SafeOperation : ToolSafety.RequiresApproval;

    bool Find(JsonObject args, out UsbToolTile? tile, out UsbToolOperation? op, out string? error)
    {
        op = null;
        var name = ToolArgs.Str(args, "tool") ?? "";
        tile = page.Tile(name);
        if (tile is null) { error = $"No tool '{name}' on this device. usb_device_tools lists them."; return false; }
        var opName = ToolArgs.Str(args, "operation") ?? "";
        op = tile.Tool.Operations.FirstOrDefault(o => o.Name == opName);
        error = op is null ? $"{tile.Tool.Title} has no operation '{opName}'. It has: {string.Join(", ", tile.Tool.Operations.Select(o => o.Name))}." : null;
        return op is not null;
    }

    public override async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        if (!page.IsPresent) return ToolResult.Error($"{page.Title} is unplugged; its tools run again when it returns.");
        if (!Find(arguments, out var tile, out var op, out var error)) return ToolResult.Error(error!);

        Dictionary<string, string> values;
        try { values = Values(ToolArgs.Raw(arguments, "arguments")); }
        catch (JsonException e) { return ToolResult.Error($"The arguments are not a JSON object: {e.Message}"); }
        if (op!.Arguments.FirstOrDefault(a => a.Required && !values.ContainsKey(a.Name)) is { } missing)
            return ToolResult.Error($"'{op.Name}' needs '{missing.Name}': {missing.Description}");

        try
        {
            return ToolResult.Ok($"{tile!.Tool.Title}: {op.Name}", await op.RunAsync(values, ct));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return ToolResult.Error($"{tile!.Tool.Title} could not {op.Name}: {e.Message}");
        }
    }

    /// <summary>The arguments object — given as an object or as its JSON text — as names to text values.</summary>
    static Dictionary<string, string> Values(string? json)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return values;
        var node = JsonNode.Parse(json);
        if (node is JsonValue text && text.TryGetValue<string>(out var inner)) node = JsonNode.Parse(inner);
        if (node is not JsonObject obj) throw new JsonException("expected an object");
        foreach (var (key, value) in obj)
            if (value is not null) values[key] = value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value.ToJsonString();
        return values;
    }
}
