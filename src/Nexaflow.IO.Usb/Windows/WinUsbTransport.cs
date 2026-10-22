using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Raw;
using Nexaflow.IO.Usb.Windows.Native;

namespace Nexaflow.IO.Usb.Windows;

/// <summary>
/// Raw access through Windows' own generic driver, WinUSB, called directly (ADR-001): a function on WinUSB — taken over
/// by Nexaflow, or a WinUSB-ready device that got it by itself — is opened through the device interface WinUSB
/// registered for it. libusbK and libusb0 are generic drivers too, but they are reached through libusb, which is a
/// backend of its own.
/// </summary>
public sealed class WinUsbRawAccess : IRawUsbAccess
{
    public string? UnreachableReason(UsbFunctionNode function)
    {
        if (!string.Equals(function.Driver.Service, "WinUSB", StringComparison.OrdinalIgnoreCase))
            return function.Mode == UsbFunctionMode.GenericRaw
                ? $"it is on {function.Driver.Service}, a generic driver reached through libusb, and Nexaflow's raw access goes through WinUSB"
                : "it is not on WinUSB — take it over first";
        if (function.DeviceInterfaceGuids.Count == 0)
            return "WinUSB is bound but registered no device interface for it (it has no DeviceInterfaceGUIDs), so no program can open it";
        return null;
    }

    public IRawUsbTransport Open(UsbFunctionNode function)
    {
        if (UnreachableReason(function) is { } reason) throw new IOException($"Cannot open it: {reason}.");
        var errors = new List<string>();
        foreach (var guid in function.DeviceInterfaceGuids)
            foreach (var path in DevNodes.Interfaces(guid, function.InstanceId))
            {
                var file = Kernel32.CreateFile(path, Kernel32.GenericRead | Kernel32.GenericWrite, Kernel32.FileShareRead | Kernel32.FileShareWrite, 0,
                    Kernel32.OpenExisting, Kernel32.FileFlagOverlapped, 0);
                if (file.IsInvalid)
                {
                    int error = Marshal.GetLastPInvokeError();
                    file.Dispose();
                    errors.Add(error is WinUsb.ErrorAccessDenied or WinUsb.ErrorSharingViolation
                        ? "another program has it open, and WinUSB lets one in at a time"
                        : $"{new Win32Exception(error).Message} (Win32 {error})");
                    continue;
                }
                try { return WinUsbTransport.Initialize(file); }
                catch (IOException e) { errors.Add(e.Message); }
            }
        throw new IOException(errors.Count == 0
            ? "WinUSB has registered no device interface for it yet — it may still be starting."
            : string.Join("; ", errors.Distinct()));
    }
}

/// <summary>
/// One opened WinUSB function: its first interface and any its association groups with it, each endpoint mapped to the
/// interface that owns it. Every transfer is overlapped and completes on the thread pool through the file handle's
/// completion port, so a reader waiting on a quiet endpoint holds no thread. A read or write is cancelled by aborting
/// its pipe; a control transfer runs to WinUSB's own five-second timeout.
/// </summary>
internal sealed class WinUsbTransport : IRawUsbTransport
{
    static readonly unsafe IOCompletionCallback Done = OnDone;

    readonly SafeFileHandle _file;
    readonly ThreadPoolBoundHandle _bound;
    readonly IReadOnlyList<nint> _interfaces;
    readonly Dictionary<byte, nint> _owners;
    readonly ConcurrentDictionary<PendingTransfer, byte> _pending = new();
    readonly object _gate = new();
    bool _disposed;

    WinUsbTransport(SafeFileHandle file, ThreadPoolBoundHandle bound, IReadOnlyList<nint> interfaces, Dictionary<byte, nint> owners, IReadOnlyList<UsbPipe> pipes)
    {
        _file = file;
        _bound = bound;
        _interfaces = interfaces;
        _owners = owners;
        Pipes = pipes;
    }

    public IReadOnlyList<UsbPipe> Pipes { get; }

    /// <summary>Takes <paramref name="file"/> over — closed here if WinUSB will not have it.</summary>
    public static WinUsbTransport Initialize(SafeFileHandle file)
    {
        if (!WinUsb.WinUsb_Initialize(file, out nint first))
        {
            var message = Kernel32.LastError("WinUsb_Initialize");
            file.Dispose();
            throw new IOException(message);
        }

        var interfaces = new List<nint> { first };
        for (byte index = 0; index < byte.MaxValue && WinUsb.WinUsb_GetAssociatedInterface(first, index, out nint associated); index++)
            interfaces.Add(associated);

        var pipes = new List<UsbPipe>();
        var owners = new Dictionary<byte, nint>();
        foreach (var handle in interfaces)
        {
            if (!WinUsb.WinUsb_QueryInterfaceSettings(handle, 0, out var settings)) continue;
            for (byte index = 0; index < settings.NumEndpoints; index++)
                if (WinUsb.WinUsb_QueryPipe(handle, 0, index, out var pipe))
                {
                    pipes.Add(new UsbPipe(settings.InterfaceNumber, pipe.PipeId, (UsbEndpointType)pipe.PipeType, pipe.MaximumPacketSize, pipe.Interval));
                    owners[pipe.PipeId] = handle;
                }
        }

        try
        {
            return new WinUsbTransport(file, ThreadPoolBoundHandle.BindHandle(file), interfaces, owners, pipes);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            Free(interfaces);
            file.Dispose();
            throw new IOException($"The function opened but its handle could not take overlapped transfers: {e.Message}", e);
        }
    }

    public async Task<byte[]> ControlInAsync(UsbControlSetup setup, CancellationToken ct)
    {
        if (setup.Direction != UsbDirection.In) throw new ArgumentException("An IN control transfer needs bmRequestType's direction bit set.", nameof(setup));
        var buffer = new byte[setup.Length];
        int read = await TransferAsync(Operation.Control, _interfaces[0], 0, Packet(setup), buffer, setup.RequestName, ct).ConfigureAwait(false);
        return buffer[..read];
    }

    public Task ControlOutAsync(UsbControlSetup setup, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (setup.Direction != UsbDirection.Out) throw new ArgumentException("An OUT control transfer needs bmRequestType's direction bit clear.", nameof(setup));
        if (data.Length != setup.Length) throw new ArgumentException($"wLength is {setup.Length} but {data.Length} byte(s) were given.", nameof(data));
        return TransferAsync(Operation.Control, _interfaces[0], 0, Packet(setup), data.ToArray(), setup.RequestName, ct);
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadEndpointAsync(byte endpoint, int packetSize, [EnumeratorCancellation] CancellationToken ct)
    {
        var (owner, pipe) = PipeFor(endpoint, UsbDirection.In);
        int size = packetSize > 0 ? packetSize : pipe.MaxPacketSize;
        while (true)
        {
            var buffer = new byte[size];
            int read;
            try { read = await TransferAsync(Operation.Read, owner, endpoint, default, buffer, $"endpoint 0x{endpoint:X2}", ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { yield break; }
            yield return buffer.AsMemory(0, read);
        }
    }

    public Task WriteEndpointAsync(byte endpoint, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var (owner, _) = PipeFor(endpoint, UsbDirection.Out);
        return TransferAsync(Operation.Write, owner, endpoint, default, data.ToArray(), $"endpoint 0x{endpoint:X2}", ct);
    }

    (nint Owner, UsbPipe Pipe) PipeFor(byte endpoint, UsbDirection direction)
    {
        var pipe = Pipes.FirstOrDefault(p => p.Address == endpoint)
            ?? throw new ArgumentException($"Endpoint 0x{endpoint:X2} is not one of this function's.", nameof(endpoint));
        if (pipe.Direction != direction) throw new ArgumentException($"Endpoint 0x{endpoint:X2} is an {pipe.Direction.ToString().ToUpperInvariant()} endpoint.", nameof(endpoint));
        if (pipe.Type is not (UsbEndpointType.Bulk or UsbEndpointType.Interrupt))
            throw new ArgumentException($"Endpoint 0x{endpoint:X2} is {pipe.Type.ToString().ToLowerInvariant()}; only bulk and interrupt endpoints are read and written here.", nameof(endpoint));
        return (_owners[endpoint], pipe);
    }

    static WinUsbSetupPacket Packet(UsbControlSetup setup) => new()
    {
        RequestType = setup.RequestType, Request = setup.Request, Value = setup.Value, Index = setup.Index, Length = setup.Length,
    };

    enum Operation { Control, Read, Write }

    unsafe Task<int> TransferAsync(Operation operation, nint owner, byte pipe, WinUsbSetupPacket setup, byte[] buffer, string what, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var pending = new PendingTransfer(this, what, ct);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pending[pending] = 0;
        }

        // pinData pins the buffer for as long as the transfer runs, so the pointer taken here stays good after it.
        NativeOverlapped* overlapped = _bound.AllocateNativeOverlapped(Done, pending, buffer);
        bool finished;
        fixed (byte* data = buffer)
        {
            finished = operation switch
            {
                Operation.Control => WinUsb.WinUsb_ControlTransfer(owner, setup, data, (uint)buffer.Length, null, overlapped),
                Operation.Read => WinUsb.WinUsb_ReadPipe(owner, pipe, data, (uint)buffer.Length, null, overlapped),
                _ => WinUsb.WinUsb_WritePipe(owner, pipe, data, (uint)buffer.Length, null, overlapped),
            };
        }
        if (!finished && Marshal.GetLastPInvokeError() is var error && error != WinUsb.ErrorIoPending)
        {
            // Nothing was queued, so no completion will come for it.
            _bound.FreeNativeOverlapped(overlapped);
            _pending.TryRemove(pending, out _);
            return Task.FromException<int>(Failure(what, error));
        }

        // Either way it completes through the port; until then a pipe transfer can be cut short by aborting its pipe —
        // as disposing does, which may have started after the check above.
        if (operation != Operation.Control)
        {
            if (ct.CanBeCanceled) pending.Arm(ct.Register(() => WinUsb.WinUsb_AbortPipe(owner, pipe)));
            if (Volatile.Read(ref _disposed)) WinUsb.WinUsb_AbortPipe(owner, pipe);
        }
        return pending.Task;
    }

    static unsafe void OnDone(uint errorCode, uint numBytes, NativeOverlapped* overlapped)
    {
        var pending = (PendingTransfer)ThreadPoolBoundHandle.GetNativeOverlappedState(overlapped)!;
        pending.Owner._bound.FreeNativeOverlapped(overlapped);
        pending.Owner._pending.TryRemove(pending, out _);
        pending.Complete(errorCode, numBytes);
    }

    static IOException Failure(string what, int error) => new(error switch
    {
        WinUsb.ErrorGenFailure => $"{what}: the device stalled it — it does not support the request, or refused it",
        WinUsb.ErrorSemTimeout => $"{what}: the device did not answer in time",
        WinUsb.ErrorOperationAborted => $"{what}: aborted",
        WinUsb.ErrorDeviceNotConnected => $"{what}: the device is gone",
        _ => $"{what}: {new Win32Exception(error).Message} (Win32 {error})",
    });

    /// <summary>Aborts every pipe, waits for what was in flight to come back, then lets the function go.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        foreach (var (endpoint, owner) in _owners) WinUsb.WinUsb_AbortPipe(owner, endpoint);
        try { await Task.WhenAll(_pending.Keys.Select(p => p.Task)).ConfigureAwait(false); }
        catch (Exception) { /* each failure already went to the caller that started it */ }
        Free(_interfaces);
        _bound.Dispose();
        _file.Dispose();
    }

    /// <summary>The associated interfaces first, then the one they came from.</summary>
    static void Free(IReadOnlyList<nint> interfaces)
    {
        for (int i = interfaces.Count - 1; i >= 0; i--) WinUsb.WinUsb_Free(interfaces[i]);
    }

    /// <summary>One transfer in flight: the task its caller awaits, and the abort its cancellation registered.</summary>
    sealed class PendingTransfer(WinUsbTransport owner, string what, CancellationToken ct)
    {
        readonly TaskCompletionSource<int> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly object _gate = new();
        CancellationTokenRegistration _abort;
        bool _completed;

        public WinUsbTransport Owner => owner;

        public Task<int> Task => _done.Task;

        /// <summary>Keeps the abort until the transfer completes — or drops it now, when it already has.</summary>
        public void Arm(CancellationTokenRegistration abort)
        {
            lock (_gate)
            {
                if (!_completed)
                {
                    _abort = abort;
                    return;
                }
            }
            abort.Dispose();
        }

        public void Complete(uint error, uint transferred)
        {
            CancellationTokenRegistration abort;
            lock (_gate)
            {
                _completed = true;
                abort = _abort;
            }
            abort.Dispose();
            if (error == 0) _done.TrySetResult((int)transferred);
            else if (error == WinUsb.ErrorOperationAborted && ct.IsCancellationRequested) _done.TrySetCanceled(ct);
            else _done.TrySetException(Failure(what, (int)error));
        }
    }
}
