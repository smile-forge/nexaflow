using Nexaflow.Features.Common;
using Nexaflow.Features.UsbProbe.Views;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe;

/// <summary>
/// Options → USB: how device names are kept current. Global, because the usb.ids copy it governs is one per user.
/// </summary>
[CustomControl(typeof(UsbProbeOptionsView))]
public sealed class UsbProbeConfig : IFeatureConfig
{
    public string ConfigName => "usbprobe";
    public string FriendlyName => Str.Get("UsbProbe.Config.Section");

    /// <summary>
    /// Whether opening the USB page looks for a newer usb.ids at linux-usb.org once the last check is more than
    /// a month old. Off, names change only when Options → USB is asked to check.
    /// </summary>
    public bool CheckDeviceNamesMonthly { get; set; } = true;
}
