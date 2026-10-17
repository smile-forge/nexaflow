using System.Text;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.UsbProbe.ClientTools;

namespace Nexaflow.Features.UsbProbe.ViewModels;

public sealed partial class UsbDeviceViewModel
{
    /// <summary>
    /// The device as the user sees it: what it is, each live tool with what it currently shows and what it can be
    /// asked to do, what no tool covers, and what is wrong.
    /// </summary>
    public string GetContext()
    {
        if (Device is null) return _started ? $"USB device page: no device at '{_target}' right now." : "USB device page: reading the device tree.";

        var sb = new StringBuilder();
        sb.AppendLine($"USB device page for {Title} ({Ids}) at '{Path}'{(IsPresent ? "" : " — unplugged; its tools are stopped until it returns")}.");
        if (Location is { } location) sb.Append(UsbAgentText.Describe(location, _browser, includeFields: false));
        sb.AppendLine();
        sb.Append(UsbDeviceTools.DescribeTools(this));
        if (Problems.Count > 0)
        {
            sb.AppendLine("Diagnostics:");
            foreach (var problem in Problems) sb.AppendLine($"  {problem}");
        }
        sb.AppendLine("Tools: usb_device_tools (each tool and its operations), usb_tool_run (runs one; the user approves the ones that write).");
        return sb.ToString();
    }

    public IReadOnlyList<IClientTool> GetClientTools() => UsbDeviceTools.For(this);

    /// <summary>One machine, one USB tree: the device page is inside the same boundary as the USB page.</summary>
    public string? GetSecurityContext() => "usb";

    public string? GetAiSystemPromptGuidance() =>
        "This page is one USB device. Its live tools — a storage browser, an audio tester, a HID monitor and so on — each have " +
        "operations, listed by usb_device_tools and run by usb_tool_run. Operations that only read run at once; ones that write " +
        "(copying files, playing or recording, sending a HID report) are approved by the user. Name a tool by the key or id usb_device_tools gives.";
}
