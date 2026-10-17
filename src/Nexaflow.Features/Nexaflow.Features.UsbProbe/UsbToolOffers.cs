using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Plugins;

namespace Nexaflow.Features.UsbProbe;

/// <summary>
/// Every provider's tools for a device, as the device page offers them and as the debug details ask them. A provider
/// that throws is reported and skipped — one broken plugin does not take the others' tools with it.
/// </summary>
static class UsbToolOffers
{
    public static List<(IUsbToolProvider Provider, IUsbTool Tool)> From(IReadOnlyList<ISubfeatureHandle<IUsbToolProvider>> providers,
        UsbDeviceNode device, IUsbToolHost host, Action<string, Exception> failed)
    {
        var offered = new List<(IUsbToolProvider, IUsbTool)>();
        // Ejected, nothing on it answers until it is plugged in again: no tool is offered.
        if (device.IsEjected) return offered;
        foreach (var handle in providers)
        {
            try
            {
                var provider = handle.Value;
                foreach (var tool in provider.Offer(device, host)) offered.Add((provider, tool));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failed(handle.DisplayName, e);
            }
        }
        return offered;
    }
}
