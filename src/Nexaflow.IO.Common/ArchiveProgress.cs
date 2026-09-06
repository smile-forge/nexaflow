using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Nexaflow.IO.Common;

/// <summary>
/// Watches how far a read has got and stops it once cancelled.
/// <para>
/// This is how an archive write is instrumented without any handler knowing. The VFS owns every
/// <see cref="ArchiveWriteEntry.OpenContent"/> delegate, so wrapping what one returns counts bytes as
/// the handler pulls them, and cancels <i>inside</i> a single large entry rather than only between
/// entries — which is the whole difference for an 8 GB file in a zip.
/// </para>
/// <para>
/// Deliberately seek- and length-transparent. A tar writer asks its source for <see cref="Length"/>
/// to build the entry header before it reads a byte, so a forward-only wrapper breaks every
/// <c>.tar*</c> write while leaving a zip-only test suite green.
/// </para>
/// </summary>
internal sealed class CountingStream(
    Stream inner, Action<long> onPosition, Action<long> onFinished, CancellationToken ct) : Stream
{
    private long _read;
    private bool _finished;

    public override int Read(byte[] buffer, int offset, int count)
    {
        ct.ThrowIfCancellationRequested();
        return Advance(inner.Read(buffer, offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ct.ThrowIfCancellationRequested();
        return Advance(inner.Read(buffer));
    }

    public override int ReadByte()
    {
        ct.ThrowIfCancellationRequested();
        var b = inner.ReadByte();
        if (b >= 0) Advance(1);
        return b;
    }

    private int Advance(int read)
    {
        if (read > 0)
        {
            _read += read;
            onPosition(_read);
        }
        return read;
    }

    // ── Transparent pass-through: see the class remarks on Length ─────────────

    public override bool CanRead  => inner.CanRead;
    public override bool CanSeek  => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length   => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set { inner.Position = value; _read = value; }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var moved = inner.Seek(offset, origin);
        _read = moved;
        return moved;
    }

    public override void Flush() => inner.Flush();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_finished)
        {
            _finished = true;
            inner.Dispose();
            onFinished(_read);
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Turns per-entry stream activity into <see cref="TransferProgress"/>, so extracting and building an
/// archive report exactly as a copy does and land in the same progress row.
/// <para>
/// Reports are gated at 200 ms to match <see cref="FileTransferEngine"/>'s own interval — a 40 GB
/// extract must not post one report per 80 KB chunk. A phase change and the final report always go
/// through.
/// </para>
/// </summary>
internal sealed class ArchiveProgressReporter(IProgress<TransferProgress>? sink, CancellationToken ct)
{
    private static readonly long ReportTicks = TimeSpan.FromMilliseconds(200).Ticks;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<string> _credited = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private TransferPhase _phase = TransferPhase.Scanning;
    private long _bytesTotal;
    private int  _itemsTotal;
    private long _bytesCredited;
    private long _bytesInFlight;
    private int  _itemsDone;
    private string? _currentItem;
    private long _lastReportTicks = long.MinValue;

    /// <summary>The token the work must honour. Held here so a caller passes one object, not two.</summary>
    internal CancellationToken Token { get; } = ct;

    /// <summary>Measuring, so the bar sweeps: reading a large archive's central directory takes
    /// seconds and is not free.</summary>
    internal void Scanning() => Transition(TransferPhase.Scanning);

    /// <summary>Totals are known; the bar can fill.</summary>
    internal void Measured(long bytesTotal, int itemsTotal)
    {
        lock (_gate)
        {
            _bytesTotal = bytesTotal;
            _itemsTotal = itemsTotal;
        }
        Transition(TransferPhase.Running);
    }

    internal void ItemStarted(string path)
    {
        lock (_gate)
        {
            _currentItem   = path;
            _bytesInFlight = 0;
        }
        Publish(force: false);
    }

    /// <summary>How far into the current entry the reader has got. Moves the bar between entries
    /// without crediting anything — see <see cref="ItemFinished"/>.</summary>
    internal void Advanced(long positionInItem)
    {
        lock (_gate) _bytesInFlight = positionInItem;
        Publish(force: false);
    }

    /// <summary>
    /// Credits one entry, once. Keyed by path rather than accumulated from stream positions because a
    /// handler is free to open an entry twice — a second pass for a CRC, say — and summing the reads
    /// would take the bar past its own total.
    /// </summary>
    internal void ItemFinished(string path, long declaredLength, long observedLength)
    {
        lock (_gate)
        {
            if (_credited.Add(path))
            {
                _bytesCredited += declaredLength > 0 ? declaredLength : observedLength;
                _itemsDone++;
            }
            if (string.Equals(_currentItem, path, StringComparison.Ordinal)) _bytesInFlight = 0;
        }
        Publish(force: false);
    }

    internal void Finished() => Transition(TransferPhase.Finished);

    /// <summary>
    /// Returns <paramref name="entries"/> with their content delegates wrapped so the write reports as
    /// it runs. Each wrapper carries its own entry, so nothing here assumes the handler walks the list
    /// in order, or only once.
    /// </summary>
    internal IReadOnlyList<ArchiveWriteEntry> Decorate(IReadOnlyList<ArchiveWriteEntry> entries)
    {
        var wrapped = new List<ArchiveWriteEntry>(entries.Count);
        foreach (var e in entries)
        {
            var open = e.OpenContent;
            wrapped.Add(open is null ? e : new ArchiveWriteEntry
            {
                Path        = e.Path,
                IsDirectory = e.IsDirectory,
                Modified    = e.Modified,
                Length      = e.Length,
                OpenContent = () =>
                {
                    ItemStarted(e.Path);
                    return new CountingStream(open(), Advanced,
                                              observed => ItemFinished(e.Path, e.Length, observed), Token);
                },
            });
        }
        return wrapped;
    }

    private void Transition(TransferPhase phase)
    {
        lock (_gate) _phase = phase;
        Publish(force: true);
    }

    private void Publish(bool force)
    {
        if (sink is null) return;

        TransferProgress snapshot;
        lock (_gate)
        {
            var now = _clock.Elapsed.Ticks;
            if (!force && now - _lastReportTicks < ReportTicks) return;
            _lastReportTicks = now;
            snapshot = Snapshot();
        }
        sink.Report(snapshot);
    }

    /// <summary>Caller holds <see cref="_gate"/>.</summary>
    private TransferProgress Snapshot()
    {
        var done    = _bytesCredited + _bytesInFlight;
        var seconds = _clock.Elapsed.TotalSeconds;
        var rate    = seconds > 0.5 ? (long)(done / seconds) : 0;

        TimeSpan? remaining = rate > 0 && _bytesTotal > done
            ? TimeSpan.FromSeconds((_bytesTotal - done) / (double)rate)
            : null;

        return new TransferProgress(_phase, done, _bytesTotal, _itemsDone, _itemsTotal,
                                    _currentItem, rate, remaining, null);
    }
}
