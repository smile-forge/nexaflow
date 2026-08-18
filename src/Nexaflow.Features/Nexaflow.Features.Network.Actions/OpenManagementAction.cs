using Nexaflow.IO.Network.Actions;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Plugins;

namespace Nexaflow.Features.Network.Actions;

/// <summary>
/// Opens the address a device published for itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is the service URL, and calling it anything else was wrong.</b> This was "Open web interface",
/// which promised something it cannot deliver: what SSDP hands over is LOCATION, the address of the
/// device's own <i>description document</i>, and a device is free to serve that from a port that hosts
/// nothing else. Both devices on the first real network did exactly that — the address does not answer a
/// browser, inside this app or outside it — so the button was named after a hope.
/// </para>
/// <para>
/// The honest version of "take me to its web page" reads <c>presentationURL</c> out of the description
/// document, which the device states and which is the only authority on the question. That needs a guarded
/// stream to fetch, which is not built. Until then this opens what was actually advertised, says so, and
/// leaves the user to judge — which is better than a button that claims to know.
/// </para>
/// <para>
/// What it will never do is try port 80 and 8080 and see what happens. That finds more and is a different
/// thing: a scan, aimed at devices that never invited one, and something to be agreed to rather than
/// discovered by clicking.
/// </para>
/// </remarks>
[Subfeature("network", "open-service-url",
    DisplayName = "Open service URL",
    Description = "Opens the address the device published for itself. That address is usually its UPnP "
                + "description document rather than a web page, so it may not render as anything useful.",
    Order = 1)]
public sealed class OpenManagementAction : IDeviceAction
{
    public string ActionId => "network.openServiceUrl";
    public string DisplayName => "Service URL";
    public string Icon => "🔗";

    public string Description =>
        "Open the address this device published for itself. It is usually the device's UPnP description "
      + "document — an XML file — rather than a web page, because that is what SSDP advertises. Nothing "
      + "here is guessed by trying ports.";

    /// <summary>Costs the network nothing; the shell does the connecting.</summary>
    public ProbeCost Cost => ProbeCost.Passive;

    public bool AppliesTo(DeviceNode device) => Where(device) is not null;

    public async Task<DeviceActionResult> PerformAsync(DeviceNode device, IDeviceActionHost host,
                                                       CancellationToken ct)
    {
        if (Where(device) is not { } url)
            return DeviceActionResult.Failed("This device has not published an address.");

        await host.OpenAsync(url, ct).ConfigureAwait(false);

        return DeviceActionResult.Worked(
            $"Opened {url} — the address the device advertised. If it shows nothing, that is the device "
          + "serving a description document rather than a page, which is normal.");
    }

    /// <summary>
    /// The address the device advertised, whole.
    /// </summary>
    /// <remarks>
    /// The full URL rather than its host, and that changed once a real device was asked: reducing it to
    /// scheme and authority was a guess that the interface lives at the root of whatever served the
    /// description, and on both devices tested nothing is there. What was advertised is the one address we
    /// have any evidence for.
    /// </remarks>
    private static string? Where(DeviceNode device)
    {
        foreach (var fact in device.AllOf(new FactKey("svc", "url")))
        {
            if (!Uri.TryCreate(fact.Value.Text, UriKind.Absolute, out var uri)) continue;
            if (uri.Scheme is not ("http" or "https")) continue;

            return uri.ToString();
        }
        return null;
    }
}
