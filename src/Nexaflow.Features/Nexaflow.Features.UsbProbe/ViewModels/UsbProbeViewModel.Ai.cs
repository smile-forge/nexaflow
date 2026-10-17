using System.Text;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.UsbProbe.ClientTools;
using Nexaflow.IO.Usb.Browsing;

namespace Nexaflow.Features.UsbProbe.ViewModels;

public sealed partial class UsbProbeViewModel
{
    /// <summary>Most rows the context names; the rest are a <c>usb_list</c> away.</summary>
    const int ContextRows = 60;

    /// <summary>
    /// Where the page is and what is in view: the open location, its rows (each with the path the tools take),
    /// and the selection with its facts — the same place the user is looking at.
    /// </summary>
    public string GetContext()
    {
        var sb = new StringBuilder();
        if (Current is not { } current) return "USB page: reading the device tree.";

        sb.AppendLine($"USB page at '{UsbAgentText.PathOf(current)}' — {current.Name}: {current.Summary}");
        sb.AppendLine($"Listing ({Rows.Count}):");
        foreach (var row in Rows.Take(ContextRows)) sb.AppendLine(UsbAgentText.Line(row.Location));
        if (Rows.Count > ContextRows) sb.AppendLine($"… {Rows.Count - ContextRows} more — usb_list shows them all.");

        if (SelectedRow is { } selected)
        {
            sb.AppendLine();
            sb.Append("Selected: ").Append(UsbAgentText.Describe(selected.Location, _browser, includeFields: false));
        }
        sb.AppendLine("Tools: usb_list, usb_describe (with descriptor fields), usb_find, usb_open, usb_reread.");
        return sb.ToString();
    }

    public IReadOnlyList<IClientTool> GetClientTools() => UsbProbeTools.For(this);

    /// <summary>One machine, one USB tree: every USB page is the same boundary.</summary>
    public string? GetSecurityContext() => "usb";

    public string? GetAiSystemPromptGuidance() =>
        "The USB page shows the machine's USB tree. Locations are paths: '3' is bus 3, '3-10.2' the device on port 2 of the hub " +
        "on port 10 of bus 3, '3-10.2/config1/if0.0/ep0x81' an endpoint inside it. Explore with usb_list and usb_describe; " +
        "usb_describe returns each descriptor's decoded fields with their offsets and bytes. Browsing never sends anything to a device; " +
        "only usb_reread does, and only standard GET_DESCRIPTOR requests.";
}
