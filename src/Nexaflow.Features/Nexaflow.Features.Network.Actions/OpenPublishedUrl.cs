using Nexaflow.IO.Network.Actions;
using Nexaflow.IO.Network.Model;
using Nexaflow.IO.Network.Probes;
using Nexaflow.Plugins;

namespace Nexaflow.Features.Network.Actions;

/// <summary>
/// Opens one of the addresses a device published about itself.
/// </summary>
/// <remarks>
/// <para>
/// A UPnP description offers up to three, and they go to different places: <c>presentationURL</c> is the
/// device's own web interface, <c>modelURL</c> is a page about the product, <c>manufacturerURL</c> is the
/// company. One button each, and each present only where the device gave that one — which is the whole
/// point of <see cref="IDeviceAction.AppliesTo"/> and why they are three actions rather than one with a
/// dropdown.
/// </para>
/// <para>
/// None of these is guessed. Trying port 80 would find more and would be a scan aimed at devices that
/// never invited one.
/// </para>
/// </remarks>
public abstract class OpenPublishedUrl(FactKey key) : IDeviceAction
{
    public abstract string ActionId { get; }
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }
    public abstract string Description { get; }

    /// <summary>Costs the network nothing; the shell does the connecting.</summary>
    public ProbeCost Cost => ProbeCost.Passive;

    public bool AppliesTo(DeviceNode device) => Where(device) is not null;

    public async Task<DeviceActionResult> PerformAsync(DeviceNode device, IDeviceActionHost host,
                                                       CancellationToken ct)
    {
        if (Where(device) is not { } url)
            return DeviceActionResult.Failed("This device did not publish that address.");

        await host.OpenAsync(url, ct).ConfigureAwait(false);
        return DeviceActionResult.Worked($"Opened {url}");
    }

    private string? Where(DeviceNode device)
    {
        foreach (var fact in device.AllOf(key))
        {
            if (!Uri.TryCreate(fact.Value.Text, UriKind.Absolute, out var uri)) continue;
            if (uri.Scheme is not ("http" or "https")) continue;

            return uri.ToString();
        }
        return null;
    }
}

/// <summary>The device's own web interface, as it declared it.</summary>
/// <remarks>
/// The honest version of the button that used to be called this. It appears only for devices whose
/// description carries a <c>presentationURL</c>, which is the only statement anybody has about where a
/// device's web page actually is.
/// </remarks>
[Subfeature("network", "open-web-interface",
    DisplayName = "Web interface",
    Description = "Opens the web page the device says it has. Present only where its description document "
                + "declared one, so it is never a guess.",
    Order = 1)]
public sealed class OpenWebInterfaceAction() : OpenPublishedUrl(new FactKey("svc", "presentation"))
{
    public override string ActionId => "network.openWebInterface";
    public override string DisplayName => "Web page";
    public override string Icon => "🌐";
    public override string Description =>
        "Open this device's own web interface, at the address it declared in its description document.";
}

/// <summary>The manufacturer's page for this model.</summary>
[Subfeature("network", "open-model-url",
    DisplayName = "Model page",
    Description = "Opens the manufacturer's page for this exact model, where the device published one.",
    Order = 2)]
public sealed class OpenModelUrlAction() : OpenPublishedUrl(new FactKey("svc", "modelUrl"))
{
    public override string ActionId => "network.openModelUrl";
    public override string DisplayName => "Model";
    public override string Icon => "📄";
    public override string Description =>
        "Open the manufacturer's page for this model — the address the device itself gave for it.";
}

/// <summary>The manufacturer.</summary>
[Subfeature("network", "open-vendor-url",
    DisplayName = "Manufacturer page",
    Description = "Opens the manufacturer's own site, where the device published it.",
    Order = 3)]
public sealed class OpenVendorUrlAction() : OpenPublishedUrl(new FactKey("svc", "vendorUrl"))
{
    public override string ActionId => "network.openVendorUrl";
    public override string DisplayName => "Maker";
    public override string Icon => "🏢";
    public override string Description =>
        "Open the manufacturer's site, at the address this device gave for it.";
}

/// <summary>
/// Whatever SSDP advertised, which is usually the description document rather than a page.
/// </summary>
/// <remarks>
/// Kept, and kept last, because it is the only address a device that answered SSDP is guaranteed to have —
/// and because when the three above are missing it is the one thing there is to look at. It says what it
/// is, so nobody clicks it expecting a web page.
/// </remarks>
[Subfeature("network", "open-service-url",
    DisplayName = "Service URL",
    Description = "Opens the address SSDP advertised. That is usually the device's UPnP description "
                + "document — an XML file — rather than a web page.",
    Order = 4)]
public sealed class OpenManagementAction() : OpenPublishedUrl(new FactKey("svc", "url"))
{
    public override string ActionId => "network.openServiceUrl";
    public override string DisplayName => "Service URL";
    public override string Icon => "🔗";
    public override string Description =>
        "Open the address SSDP advertised. It is usually the device's description document rather than a "
      + "web page, because that is what SSDP points at.";
}
