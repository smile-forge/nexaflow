using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.UsbProbe.ViewModels;
using Nexaflow.IO.Usb.Browsing;
using Nexaflow.IO.Usb.Descriptors;

namespace Nexaflow.Features.UsbProbe.ClientTools;

/// <summary>How a location reads to the agent: one line in a list, or everything about it.</summary>
public static class UsbAgentText
{
    public static string PathOf(UsbLocation location) => location.Path.Length == 0 ? "usb:" : location.Path;

    public static string Line(UsbLocation location) => $"- {PathOf(location)} | {location.Name} | {location.Summary}";

    /// <summary>
    /// The location's facts, then — when a descriptor describes it — every decoded field with its offset in the
    /// blob, its size, its value and its meaning, and the descriptor's bytes; then what lies beneath it.
    /// </summary>
    public static string Describe(UsbLocation location, UsbBrowser browser, bool includeFields)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{location.Name} [{location.Kind}] at '{PathOf(location)}' — {location.Summary}");
        foreach (var group in UsbLocationFacts.Of(location))
        {
            sb.AppendLine($"{group.Title}:");
            foreach (var fact in group.Facts)
                sb.AppendLine(fact.Label.Length == 0 ? $"  {fact.Value}" : $"  {fact.Label}: {fact.Value.Replace("\n", ", ")}");
        }

        if (includeFields && location.Map is { } map && (location.Entry ?? (map.Entries.Count > 0 ? map.Entries[0] : null)) is { } entry)
        {
            sb.AppendLine($"Descriptor: {entry.Kind}, {entry.Length} bytes at offset {entry.Offset} of {map.Title} ({map.Bytes.Length} bytes)");
            foreach (var field in entry.Fields)
                sb.AppendLine($"  +{field.Offset - entry.Offset} {field.Name} ({field.Size}) = {field.Value}{(field.Meaning is { } m ? $" — {m}" : "")}");
            sb.AppendLine($"  bytes: {UsbHex.Format(map.Bytes.AsSpan(entry.Offset, entry.Length))}");
            foreach (var problem in map.Problems) sb.AppendLine($"  problem: {problem}");
        }

        var children = browser.Children(location);
        if (children.Count > 0) sb.AppendLine($"Contains {children.Count} item(s) — usb_list '{PathOf(location)}'.");
        return sb.ToString();
    }
}

/// <summary>
/// The agent's way round the USB tree: the same places the page shows, by the same paths. All of them read what
/// the inventory already holds, except <c>usb_reread</c>, which asks one device for its descriptors again.
/// </summary>
public static class UsbProbeTools
{
    const string LocationHelp =
        "A USB location path: '' or 'usb:' for every bus, '3' for bus 3, '3-10.2' for the device or port at port 2 of the hub on port 10 of bus 3, " +
        "'3-10.2/config1', '3-10.2/config1/if0.0' (interface 0, alt 0), '3-10.2/config1/if0.0/ep0x81', '3-10.2/bos'. Defaults to the page's open location.";

    public static IReadOnlyList<IClientTool> For(UsbProbeViewModel page) =>
    [
        new DelegateClientTool("usb_list",
            "Lists what is inside a USB location — buses, ports and devices, or a device's configurations, interfaces, endpoints and class descriptors — with each item's path.",
            [new ClientToolParameter("location", LocationHelp, Required: false)],
            ToolSafety.SafeOperation,
            (args, _) => Task.FromResult(List(page, args)),
            parallelizable: true),

        new DelegateClientTool("usb_describe",
            "Everything about one USB location: identity, connection, driver and Windows function facts, and every decoded descriptor field with its offset, size, value and meaning, plus the raw bytes.",
            [new ClientToolParameter("location", LocationHelp, Required: false)],
            ToolSafety.SafeOperation,
            (args, _) => Task.FromResult(Describe(page, args)),
            parallelizable: true),

        new DelegateClientTool("usb_find",
            "Finds attached USB devices by words in their name, vendor, product, serial number, VID:PID (e.g. '046d:0843'), port path, COM port, class or driver.",
            [new ClientToolParameter("query", "Words that must all appear, e.g. 'logitech webcam', '0403:6001', 'COM5', 'audio'.")],
            ToolSafety.SafeOperation,
            (args, _) => Task.FromResult(Find(page, args)),
            parallelizable: true),

        new DelegateClientTool("usb_open",
            "Shows a USB location on the page, as if the user had navigated there.",
            [new ClientToolParameter("location", LocationHelp)],
            ToolSafety.SafeOperation,
            (args, _) => Task.FromResult(Open(page, args))),

        new DelegateClientTool("usb_reread",
            "Asks one device for its descriptors and strings again, waking it if it is asleep. Sends only standard GET_DESCRIPTOR requests.",
            [new ClientToolParameter("location", "The device's location path (anything inside it will do). " + LocationHelp, Required: false)],
            ToolSafety.SafeOperation,
            (args, ct) => ReReadAsync(page, args, ct)),
    ];

    static UsbLocation? Resolve(UsbProbeViewModel page, JsonObject args, out string? error)
    {
        error = null;
        if (ToolArgs.Str(args, "location") is not { } path) return page.Current ?? page.Browser.Root;
        if (page.Browser.Resolve(path) is { } location) return location;
        error = $"No USB location '{path}'. usb_list '' shows the buses; usb_find searches devices by name.";
        return null;
    }

    static ToolResult List(UsbProbeViewModel page, JsonObject args)
    {
        if (Resolve(page, args, out var error) is not { } location) return ToolResult.Error(error!);
        var children = page.Browser.Children(location);
        var sb = new StringBuilder($"{location.Name} at '{UsbAgentText.PathOf(location)}' holds {children.Count} item(s):\n");
        foreach (var child in children) sb.AppendLine(UsbAgentText.Line(child));
        return ToolResult.Ok($"Listed {children.Count} item(s) in {location.Name}", sb.ToString());
    }

    static ToolResult Describe(UsbProbeViewModel page, JsonObject args) =>
        Resolve(page, args, out var error) is { } location
            ? ToolResult.Ok($"Described {location.Name}", UsbAgentText.Describe(location, page.Browser, includeFields: true))
            : ToolResult.Error(error!);

    static ToolResult Find(UsbProbeViewModel page, JsonObject args)
    {
        var query = ToolArgs.Str(args, "query") ?? "";
        var found = page.Browser.Find(query);
        if (found.Count == 0) return ToolResult.Ok($"No device matches '{query}'", $"No attached USB device matches '{query}'.");
        var sb = new StringBuilder($"{found.Count} match(es):\n");
        foreach (var location in found) sb.AppendLine(UsbAgentText.Line(location));
        return ToolResult.Ok($"Found {found.Count} device(s)", sb.ToString());
    }

    static ToolResult Open(UsbProbeViewModel page, JsonObject args)
    {
        var path = ToolArgs.Str(args, "location") ?? "";
        return page.Navigate(path)
            ? ToolResult.Ok($"Showing {page.Current!.Name}", $"The page now shows '{UsbAgentText.PathOf(page.Current!)}'.")
            : ToolResult.Error($"No USB location '{path}'.");
    }

    static async Task<ToolResult> ReReadAsync(UsbProbeViewModel page, JsonObject args, CancellationToken ct)
    {
        if (Resolve(page, args, out var error) is not { } location) return ToolResult.Error(error!);
        if (location.Device is not { } device) return ToolResult.Error($"'{UsbAgentText.PathOf(location)}' is not a device or inside one.");
        var fresh = await page.ReReadDeviceAsync(device.InstanceId, ct);
        if (fresh is null) return ToolResult.Error($"{device.DisplayName} is no longer attached.");
        var again = page.Browser.Resolve(fresh.Location.PortPath)!;
        return ToolResult.Ok($"Re-read {fresh.DisplayName}", UsbAgentText.Describe(again, page.Browser, includeFields: false));
    }
}
