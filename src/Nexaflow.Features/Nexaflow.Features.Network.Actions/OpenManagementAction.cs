using Nexaflow.IO.Network.Actions;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Plugins;

namespace Nexaflow.Features.Network.Actions;

/// <summary>
/// Opens whatever web interface the device has told us about.
/// </summary>
/// <remarks>
/// <para>
/// <b>Offered only where there is one, and it never guesses.</b> A UPnP device answering SSDP hands over a
/// LOCATION — the address of its own description document — and that host is, in practice, where its web
/// interface lives. So this appears for devices that said so and stays away from devices that did not,
/// which is the whole reason <see cref="IDeviceAction.AppliesTo"/> exists: a button that cannot work is
/// worse than no button.
/// </para>
/// <para>
/// Trying port 80 and 8080 on everything would find more and would be a different thing — a scan, aimed at
/// devices that never invited it, and something the user should agree to rather than discover by clicking.
/// The honest version of "find the web interface" reads the device's own <c>presentationURL</c> out of the
/// description document at LOCATION, which needs a guarded stream that is not built yet.
/// </para>
/// </remarks>
[Subfeature("network", "open-management",
    DisplayName = "Open web interface",
    Description = "Opens the device's own web page, for devices that published one. Nothing is guessed: "
                + "this appears only where the device told us where to look.",
    Order = 1)]
public sealed class OpenManagementAction : IDeviceAction
{
    public string ActionId => "network.openManagement";
    public string DisplayName => "Open web interface";
    public string Icon => "🌐";

    public string Description =>
        "Open the device's own web page in a tab. Only offered for devices that published an address for "
      + "it — nothing here is guessed by trying ports.";

    /// <summary>Costs the network nothing; the shell does the connecting.</summary>
    public ProbeCost Cost => ProbeCost.Passive;

    public bool AppliesTo(DeviceNode device) => Where(device) is not null;

    public async Task<DeviceActionResult> PerformAsync(DeviceNode device, IDeviceActionHost host,
                                                       CancellationToken ct)
    {
        if (Where(device) is not { } url)
            return DeviceActionResult.Failed("This device has not published a web address.");

        await host.OpenAsync(url, ct).ConfigureAwait(false);
        return DeviceActionResult.Worked($"Opened {url}");
    }

    /// <summary>
    /// The device's own web address, from the service URL it advertised.
    /// </summary>
    /// <remarks>
    /// Reduced to scheme and authority, because LOCATION points at a description document — an XML file a
    /// person has no use for — served by the same host that serves the interface. Reading the real
    /// <c>presentationURL</c> out of that document is the better answer and needs a fetch this cannot do.
    /// </remarks>
    private static string? Where(DeviceNode device)
    {
        foreach (var fact in device.AllOf(new FactKey("svc", "url")))
        {
            if (!Uri.TryCreate(fact.Value.Text, UriKind.Absolute, out var uri)) continue;
            if (uri.Scheme is not ("http" or "https")) continue;

            return $"{uri.Scheme}://{uri.Authority}/";
        }
        return null;
    }
}
