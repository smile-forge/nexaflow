using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Nexaflow.IO.Usb.Descriptors;

namespace Nexaflow.IO.Usb.Raw;

public enum RawUsbTransferKind { ControlIn, ControlOut, EndpointIn, EndpointOut }

/// <summary>One transfer as the session log keeps it (spec R6): when, which way, to what, how much, the bytes, how it
/// ended and how long it took.</summary>
public sealed record RawUsbTransfer(DateTimeOffset At, RawUsbTransferKind Kind, string Target, int Requested, byte[] Data, string? Error, TimeSpan Duration)
{
    public bool Succeeded => Error is null;

    public UsbDirection Direction => Kind is RawUsbTransferKind.ControlIn or RawUsbTransferKind.EndpointIn ? UsbDirection.In : UsbDirection.Out;

    /// <summary>"12:01:02.345 IN  GET_DESCRIPTOR … · 18/18 B · 0.4 ms · OK · 12 01 00 02 …".</summary>
    public string Line() => string.Create(CultureInfo.InvariantCulture,
        $"{At:HH:mm:ss.fff} {(Direction == UsbDirection.In ? "IN " : "OUT")} {Target} · {Data.Length}/{Requested} B · {Duration.TotalMilliseconds:0.0} ms · {Error ?? "OK"}{(Data.Length > 0 ? " · " + UsbHex.Format(Data) : "")}");
}

/// <summary>
/// The log of every transfer made with one function — control, read and write, done or failed — across however many
/// times it was opened and let go, so a session can be shown as it happens and saved as text. Transfers reach it
/// through <see cref="Record"/>. Cancelling a read is how a reader stops, so it is not logged as a failure. The newest
/// <see cref="Kept"/> transfers are kept; the text says how many earlier ones were let go.
/// </summary>
public sealed class RawUsbSession
{
    public const int Kept = 20_000;

    readonly object _gate = new();
    readonly Queue<RawUsbTransfer> _transfers = new();
    long _dropped;

    /// <summary>Raised on the thread the transfer finished on.</summary>
    public event Action<RawUsbTransfer>? Recorded;

    public IReadOnlyList<RawUsbTransfer> Transfers
    {
        get { lock (_gate) return [.. _transfers]; }
    }

    public long Dropped
    {
        get { lock (_gate) return _dropped; }
    }

    /// <summary><paramref name="inner"/>, with every transfer through it logged here; disposing it disposes <paramref name="inner"/>.</summary>
    public IRawUsbTransport Record(IRawUsbTransport inner) => new RecordingTransport(this, inner);

    /// <summary>Every kept transfer, one a line, oldest first.</summary>
    public string Text()
    {
        var sb = new StringBuilder();
        lock (_gate)
        {
            if (_dropped > 0) sb.AppendLine(CultureInfo.InvariantCulture, $"({_dropped} earlier transfer(s) not kept)");
            foreach (var transfer in _transfers) sb.AppendLine(transfer.Line());
        }
        return sb.ToString();
    }

    public void Clear()
    {
        lock (_gate)
        {
            _transfers.Clear();
            _dropped = 0;
        }
    }

    void Add(RawUsbTransfer transfer)
    {
        lock (_gate)
        {
            _transfers.Enqueue(transfer);
            while (_transfers.Count > Kept)
            {
                _transfers.Dequeue();
                _dropped++;
            }
        }
        Recorded?.Invoke(transfer);
    }

    sealed class RecordingTransport(RawUsbSession session, IRawUsbTransport inner) : IRawUsbTransport
    {
        public IReadOnlyList<UsbPipe> Pipes => inner.Pipes;

        public async Task<byte[]> ControlInAsync(UsbControlSetup setup, CancellationToken ct)
        {
            var (at, started) = (DateTimeOffset.Now, Stopwatch.GetTimestamp());
            try
            {
                var data = await inner.ControlInAsync(setup, ct).ConfigureAwait(false);
                session.Add(new(at, RawUsbTransferKind.ControlIn, setup.Describe(), setup.Length, data, null, Stopwatch.GetElapsedTime(started)));
                return data;
            }
            catch (Exception e) when (Failed(at, RawUsbTransferKind.ControlIn, setup.Describe(), setup.Length, [], started, e)) { throw; }
        }

        public async Task ControlOutAsync(UsbControlSetup setup, ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            var (at, started) = (DateTimeOffset.Now, Stopwatch.GetTimestamp());
            try
            {
                await inner.ControlOutAsync(setup, data, ct).ConfigureAwait(false);
                session.Add(new(at, RawUsbTransferKind.ControlOut, setup.Describe(), setup.Length, data.ToArray(), null, Stopwatch.GetElapsedTime(started)));
            }
            catch (Exception e) when (Failed(at, RawUsbTransferKind.ControlOut, setup.Describe(), setup.Length, data.ToArray(), started, e)) { throw; }
        }

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadEndpointAsync(byte endpoint, int packetSize, [EnumeratorCancellation] CancellationToken ct)
        {
            string target = $"endpoint 0x{endpoint:X2}";
            await using var reads = inner.ReadEndpointAsync(endpoint, packetSize, ct).GetAsyncEnumerator(ct);
            while (true)
            {
                var (at, started) = (DateTimeOffset.Now, Stopwatch.GetTimestamp());
                bool more;
                try { more = await reads.MoveNextAsync().ConfigureAwait(false); }
                catch (Exception e) when (Failed(at, RawUsbTransferKind.EndpointIn, target, packetSize, [], started, e)) { throw; }
                if (!more) yield break;
                session.Add(new(at, RawUsbTransferKind.EndpointIn, target, packetSize, reads.Current.ToArray(), null, Stopwatch.GetElapsedTime(started)));
                yield return reads.Current;
            }
        }

        public async Task WriteEndpointAsync(byte endpoint, ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            string target = $"endpoint 0x{endpoint:X2}";
            var (at, started) = (DateTimeOffset.Now, Stopwatch.GetTimestamp());
            try
            {
                await inner.WriteEndpointAsync(endpoint, data, ct).ConfigureAwait(false);
                session.Add(new(at, RawUsbTransferKind.EndpointOut, target, data.Length, data.ToArray(), null, Stopwatch.GetElapsedTime(started)));
            }
            catch (Exception e) when (Failed(at, RawUsbTransferKind.EndpointOut, target, data.Length, data.ToArray(), started, e)) { throw; }
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();

        /// <summary>Logs a failed transfer; false, so the exception goes on as it was. A cancelled one is not a failure.</summary>
        bool Failed(DateTimeOffset at, RawUsbTransferKind kind, string target, int requested, byte[] data, long started, Exception e)
        {
            if (e is OperationCanceledException) return false;
            session.Add(new(at, kind, target, requested, data, e.Message, Stopwatch.GetElapsedTime(started)));
            return false;
        }
    }
}
