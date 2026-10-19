namespace Nexaflow.Media.Jbig2.Internal;

/// <summary>
/// A segment that cannot be decoded as it stands — its data ends early, or holds a value the standard does not allow.
/// The page is composed without it, and the reason kept as a warning on the image.
/// </summary>
internal sealed class Jbig2DataException(string message) : Exception(message)
{
    public static Jbig2DataException DataEnd() => new("the data ends before the segment does");
}

/// <summary>
/// What one decode has spent and met: the memory its bitmaps and tables take and the pixels it works through, each held
/// to a limit the caller set — a damaged header can claim a page, a grid or a dictionary of any size — and the warnings
/// it raised. A globals stream's context is charged by every page decoded with it, from whichever thread decodes a
/// dictionary first.
/// </summary>
internal sealed class Jbig2DecodeContext(long memoryLimit, int workLimit)
{
    private const int KeptWarnings = 100;

    /// <summary>The work a page of 1 megapixel may do, which a smaller page may do too.</summary>
    private const long SmallPage = 1L << 20;

    private readonly List<string> _warnings = [];
    private int _warningCount;
    private long _charged;
    private long _work;
    private long _workAllowed = workLimit * SmallPage;

    /// <summary>Takes <paramref name="bytes"/> from what the decode may use, refusing the decode once it is used up.</summary>
    public void Charge(long bytes)
    {
        if (bytes < 0) throw new Jbig2DataException("a size the data gives is out of range");
        if (Interlocked.Add(ref _charged, bytes) > memoryLimit)
            throw new Jbig2Exception($"Decoding needs more than the {memoryLimit / (1024 * 1024)} MB it may use");
    }

    public long Charged => Interlocked.Read(ref _charged);

    /// <summary>
    /// Counts <paramref name="pixels"/> decoded, combined or copied against the work the decode may do, refusing the
    /// decode once it is done — before the work, where it is counted ahead.
    /// </summary>
    public void Work(long pixels)
    {
        if (Interlocked.Add(ref _work, pixels) > Interlocked.Read(ref _workAllowed))
            throw new Jbig2Exception($"Decoding needs more than {workLimit} times the work of its page");
    }

    /// <summary>Allows the work a page of <paramref name="pagePixels"/> may do, where it is more than allowed already.</summary>
    public void AllowWorkFor(long pagePixels)
    {
        var allowed = workLimit * Math.Max(pagePixels, SmallPage);
        for (var current = Interlocked.Read(ref _workAllowed); current < allowed; current = Interlocked.Read(ref _workAllowed))
            if (Interlocked.CompareExchange(ref _workAllowed, allowed, current) == current) return;
    }

    /// <summary>A bitmap's bytes, charged: rows of whole bytes, the width and height checked first.</summary>
    public byte[] Allocate(int width, int height)
    {
        if (width < 0 || height < 0) throw new Jbig2DataException($"a bitmap of {width} x {height} pixels");
        var bytes = (long)((width + 7) >> 3) * height;
        Charge(bytes);
        if (bytes > Array.MaxLength) throw new Jbig2Exception($"A bitmap of {width} x {height} pixels is larger than one buffer holds");
        return new byte[bytes];
    }

    /// <summary>The contexts of an arithmetic coding procedure, a byte each, charged.</summary>
    public CX NewCx(int size)
    {
        Charge(size);
        return new CX(size, 1);
    }

    public void Warn(string message)
    {
        lock (_warnings)
        {
            _warningCount++;
            if (_warnings.Count < KeptWarnings) _warnings.Add(message);
        }
    }

    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_warnings)
                return _warningCount <= _warnings.Count ? [.. _warnings] : [.. _warnings, $"and {_warningCount - _warnings.Count} more"];
        }
    }
}
