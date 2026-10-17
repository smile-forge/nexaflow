namespace Nexaflow.IO.Usb.Binding;

/// <summary>The generic driver a take-over installs.</summary>
public enum GenericUsbDriverKind { WinUsb }

/// <summary>
/// A binding's lifecycle. Every change is written as an intent before it is made and as an outcome after, so a
/// record still in an <c>…Intent</c> state means the process died in between — and restore picks it up from there.
/// </summary>
public enum UsbBindingState { BindIntent, Bound, BindFailed, RestoreIntent, Restored, RestoreFailed }

/// <summary>
/// One take-over, with everything needed to undo it: the original driver and class, and what the take-over
/// changed besides the driver (the class GUID, the DeviceInterfaceGUIDs value it wrote for winusb.sys).
/// </summary>
public sealed record UsbBindingRecord(
    Guid Id, string InterfaceInstanceId, string HardwareId,
    string? OriginalService, string? OriginalInfName, string? OriginalInfSection, string? OriginalMatchingId,
    Guid? OriginalClassGuid, string Strategy, string AddedInterfaceGuid, DateTimeOffset BoundAt)
{
    public const string InboxWinUsbStrategy = "InboxWinUsb";

    public UsbBindingState State { get; set; }
    public bool ClassChanged { get; set; }

    /// <summary>A published oemNN.inf; always null for <see cref="InboxWinUsbStrategy"/>, which publishes nothing.</summary>
    public string? PublishedInfName { get; init; }

    /// <summary>A trusted certificate the binding added; always null for <see cref="InboxWinUsbStrategy"/>.</summary>
    public string? CertificateThumbprint { get; init; }

    public DateTimeOffset? RestoredAt { get; set; }

    /// <summary>Each step the bridge took, in order, with the Win32 result — what to read when Windows changes under us.</summary>
    public List<string> Log { get; init; } = [];

    /// <summary>Still holds, or may hold, the generic driver — anything not cleanly restored.</summary>
    public bool IsActive => State is not UsbBindingState.Restored;
}
