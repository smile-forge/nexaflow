using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Descriptors;

namespace Nexaflow.IO.Usb.Raw;

/// <summary>One endpoint of an opened interface, as the generic driver reports it.</summary>
public sealed record UsbPipe(byte InterfaceNumber, byte Address, UsbEndpointType Type, ushort MaxPacketSize, byte Interval)
{
    public UsbDirection Direction => (Address & 0x80) != 0 ? UsbDirection.In : UsbDirection.Out;

    /// <summary>"0x81 IN interrupt · 64 B · interface 0".</summary>
    public string Label => $"0x{Address:X2} {Direction.ToString().ToUpperInvariant()} {Type.ToString().ToLowerInvariant()} · {MaxPacketSize} B · interface {InterfaceNumber}";
}

/// <summary>
/// Raw transfers with one function on a generic driver (spec §8.4): control transfers on the default pipe, and reads
/// and writes on its bulk and interrupt endpoints. Isochronous endpoints are listed but not transferred on. A
/// transport holds the function open — a generic driver lets one program in at a time — until it is disposed.
/// </summary>
public interface IRawUsbTransport : IAsyncDisposable
{
    /// <summary>Every endpoint of every interface the function spans, in their default settings.</summary>
    IReadOnlyList<UsbPipe> Pipes { get; }

    /// <summary>A control transfer that reads: up to <see cref="UsbControlSetup.Length"/> bytes, as many as the device sent.</summary>
    Task<byte[]> ControlInAsync(UsbControlSetup setup, CancellationToken ct);

    /// <summary>A control transfer that writes <paramref name="data"/>, which is <see cref="UsbControlSetup.Length"/> long.</summary>
    Task ControlOutAsync(UsbControlSetup setup, ReadOnlyMemory<byte> data, CancellationToken ct);

    /// <summary>Reads an IN endpoint until cancelled, one transfer of up to <paramref name="packetSize"/> bytes at a time.</summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadEndpointAsync(byte endpoint, int packetSize, CancellationToken ct);

    Task WriteEndpointAsync(byte endpoint, ReadOnlyMemory<byte> data, CancellationToken ct);
}

/// <summary>Opens functions for raw transfers through one backend — WinUSB on Windows; libusb could stand beside it.</summary>
public interface IRawUsbAccess
{
    /// <summary>Why this backend cannot open <paramref name="function"/>, or null when it can try — answered from what
    /// Windows knows, without opening anything.</summary>
    string? UnreachableReason(UsbFunctionNode function);

    /// <summary>Opens the function; an <see cref="IOException"/> says why not — another program holding it, say.</summary>
    IRawUsbTransport Open(UsbFunctionNode function);
}
