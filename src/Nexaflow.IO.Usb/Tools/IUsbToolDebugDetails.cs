using System.Text.Json.Nodes;
using Nexaflow.IO.Usb.Diagnostics;

namespace Nexaflow.IO.Usb.Tools;

/// <summary>
/// A live tool that adds to a device's debug details what it sees beyond the descriptors — a disk's volumes, a phone's
/// storages, a HID collection's parsed layout, an audio endpoint's format — so a device the developers do not have can
/// be supported from what a tester sends.
/// </summary>
/// <remarks>
/// Asked of a tool that may never have been opened (debug details are taken from the USB page too), so it reads what it
/// needs itself, and changes nothing on the device. Serials and personal names go through <paramref name="mask"/>.
/// </remarks>
public interface IUsbToolDebugDetails
{
    Task<JsonObject> DebugDetailsAsync(UsbDebugMask mask, CancellationToken ct);
}
