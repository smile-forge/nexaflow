using Nexaflow.IO.Usb.Descriptors;

namespace Nexaflow.IO.Usb.Raw;

/// <summary>bmRequestType bits 5–6: who defines the request.</summary>
public enum UsbRequestType { Standard, Class, Vendor, Reserved }

/// <summary>bmRequestType bits 0–4: what the request is addressed to.</summary>
public enum UsbRequestRecipient { Device, Interface, Endpoint, Other }

/// <summary>
/// A control transfer's eight-byte setup packet (USB 2.0 §9.3), with bmRequestType taken apart into its direction,
/// type and recipient.
/// </summary>
public readonly record struct UsbControlSetup(byte RequestType, byte Request, ushort Value, ushort Index, ushort Length)
{
    public static UsbControlSetup Of(UsbDirection direction, UsbRequestType type, UsbRequestRecipient recipient,
        byte request, ushort value, ushort index, ushort length) =>
        new((byte)((direction == UsbDirection.In ? 0x80 : 0) | ((int)type << 5) | (int)recipient), request, value, index, length);

    public UsbDirection Direction => (RequestType & 0x80) != 0 ? UsbDirection.In : UsbDirection.Out;

    public UsbRequestType Type => (UsbRequestType)((RequestType >> 5) & 0x3);

    /// <summary>The recipient; codes past <see cref="UsbRequestRecipient.Other"/> are reserved and read as Other.</summary>
    public UsbRequestRecipient Recipient => (RequestType & 0x1F) switch { 0 => UsbRequestRecipient.Device, 1 => UsbRequestRecipient.Interface, 2 => UsbRequestRecipient.Endpoint, _ => UsbRequestRecipient.Other };

    /// <summary>A standard request's name — GET_DESCRIPTOR — or the request code in hex for a class or vendor one.</summary>
    public string RequestName => Type == UsbRequestType.Standard && UsbStandardRequests.Name(Request) is { } name ? name : $"0x{Request:X2}";

    /// <summary>"GET_DESCRIPTOR IN standard device · wValue 0x0100 wIndex 0x0000 wLength 18".</summary>
    public string Describe() =>
        $"{RequestName} {Direction.ToString().ToUpperInvariant()} {Type.ToString().ToLowerInvariant()} {Recipient.ToString().ToLowerInvariant()} · wValue 0x{Value:X4} wIndex 0x{Index:X4} wLength {Length}";
}

/// <summary>A request ready to send, named for the console's list.</summary>
public sealed record UsbRequestTemplate(string Name, UsbControlSetup Setup);

/// <summary>The standard requests of USB 2.0 §9.4, by code, and the ones the console offers ready-made.</summary>
public static class UsbStandardRequests
{
    public const byte GetStatus = 0, ClearFeature = 1, SetFeature = 3, SetAddress = 5, GetDescriptor = 6, SetDescriptor = 7,
        GetConfiguration = 8, SetConfiguration = 9, GetInterface = 10, SetInterface = 11, SynchFrame = 12;

    public static string? Name(byte request) => request switch
    {
        GetStatus => "GET_STATUS",
        ClearFeature => "CLEAR_FEATURE",
        SetFeature => "SET_FEATURE",
        SetAddress => "SET_ADDRESS",
        GetDescriptor => "GET_DESCRIPTOR",
        SetDescriptor => "SET_DESCRIPTOR",
        GetConfiguration => "GET_CONFIGURATION",
        SetConfiguration => "SET_CONFIGURATION",
        GetInterface => "GET_INTERFACE",
        SetInterface => "SET_INTERFACE",
        SynchFrame => "SYNCH_FRAME",
        _ => null,
    };

    /// <summary>
    /// The reads a console starts from: each standard request that only asks, filled in for the device, its first
    /// configuration and string, and <paramref name="interfaceNumber"/>. Requests that change the device's state
    /// (SET_ADDRESS, SET_CONFIGURATION…) are typed in, not offered.
    /// </summary>
    public static IReadOnlyList<UsbRequestTemplate> Reads(byte interfaceNumber) =>
    [
        new("GET_DESCRIPTOR (device)", Read(UsbRequestRecipient.Device, GetDescriptor, 0x0100, 0, 18)),
        new("GET_DESCRIPTOR (configuration)", Read(UsbRequestRecipient.Device, GetDescriptor, 0x0200, 0, 255)),
        new("GET_DESCRIPTOR (string languages)", Read(UsbRequestRecipient.Device, GetDescriptor, 0x0300, 0, 255)),
        new("GET_DESCRIPTOR (BOS)", Read(UsbRequestRecipient.Device, GetDescriptor, 0x0F00, 0, 255)),
        new("GET_STATUS (device)", Read(UsbRequestRecipient.Device, GetStatus, 0, 0, 2)),
        new("GET_STATUS (interface)", Read(UsbRequestRecipient.Interface, GetStatus, 0, interfaceNumber, 2)),
        new("GET_CONFIGURATION", Read(UsbRequestRecipient.Device, GetConfiguration, 0, 0, 1)),
        new("GET_INTERFACE", Read(UsbRequestRecipient.Interface, GetInterface, 0, interfaceNumber, 1)),
    ];

    static UsbControlSetup Read(UsbRequestRecipient recipient, byte request, ushort value, ushort index, ushort length) =>
        UsbControlSetup.Of(UsbDirection.In, UsbRequestType.Standard, recipient, request, value, index, length);
}
