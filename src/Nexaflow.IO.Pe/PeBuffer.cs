using System.IO.MemoryMappedFiles;
using System.Text;

namespace Nexaflow.IO.Pe;

/// <summary>
/// Bounds-checked random access over an image's bytes — the foundation of this library's "never
/// throw" contract. Every accessor returns an empty span or <c>false</c> when the requested range
/// falls outside the file, so a structure pointing past EOF (the signature of a truncated download
/// or a hand-crafted anti-analysis header) degrades to a diagnostic instead of an exception.
/// <para>
/// A file is memory-mapped rather than read, so a 300 MB binary costs no managed allocation and the
/// resource/hex reads a viewer makes later stay cheap. The map is held for the lifetime of the
/// owning <see cref="PeImage"/>.
/// </para>
/// </summary>
public sealed unsafe class PeBuffer : IDisposable
{
    private readonly byte[]?                    _array;
    private readonly MemoryMappedFile?          _mmf;
    private readonly MemoryMappedViewAccessor?  _view;
    private          byte*                      _ptr;
    private          bool                       _acquired;
    private          bool                       _disposed;

    public long Length { get; }

    private PeBuffer(byte[] array)
    {
        _array = array;
        Length = array.LongLength;
    }

    private PeBuffer(MemoryMappedFile mmf, MemoryMappedViewAccessor view, long length)
    {
        _mmf   = mmf;
        _view  = view;
        Length = length;
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref _ptr);
        _acquired = true;
    }

    public static PeBuffer FromMemory(ReadOnlyMemory<byte> bytes) => new(bytes.ToArray());

    /// <summary>Reads a stream fully into memory. Used for non-seekable / in-test input.</summary>
    public static PeBuffer FromStream(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return new PeBuffer(ms.ToArray());
    }

    /// <summary>
    /// Maps <paramref name="path"/>. Falls back to a plain read when mapping is refused (a locked or
    /// sparse file), and throws only for a genuinely unreadable path — <see cref="PeReader"/> catches that.
    /// </summary>
    public static PeBuffer FromFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Image not found.", path);
        if (info.Length == 0) return new PeBuffer([]);

        try
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            try
            {
                var mmf  = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read,
                                                           HandleInheritability.None, leaveOpen: false);
                var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                return new PeBuffer(mmf, view, info.Length);
            }
            catch { fs.Dispose(); throw; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new PeBuffer(File.ReadAllBytes(path));
        }
    }

    /// <summary>True when <paramref name="count"/> bytes are readable at <paramref name="offset"/>.</summary>
    public bool InRange(long offset, long count)
        => count >= 0 && offset >= 0 && count <= Length && offset <= Length - count;

    /// <summary>The requested range, or an empty span when it falls outside the file.</summary>
    public ReadOnlySpan<byte> Slice(long offset, int count)
    {
        if (_disposed || count <= 0 || !InRange(offset, count)) return default;
        return _array is not null
            ? _array.AsSpan((int)offset, count)
            : new ReadOnlySpan<byte>(_ptr + offset, count);
    }

    public bool TryU8(long offset, out byte value)
    {
        var s = Slice(offset, 1);
        value = s.IsEmpty ? default : s[0];
        return !s.IsEmpty;
    }

    public bool TryU16(long offset, out ushort value)
    {
        var s = Slice(offset, 2);
        value = s.IsEmpty ? default : (ushort)(s[0] | (s[1] << 8));
        return !s.IsEmpty;
    }

    public bool TryU32(long offset, out uint value)
    {
        var s = Slice(offset, 4);
        value = s.IsEmpty ? default : (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24));
        return !s.IsEmpty;
    }

    public bool TryU64(long offset, out ulong value)
    {
        value = default;
        if (!TryU32(offset, out var lo) || !TryU32(offset + 4, out var hi)) return false;
        value = lo | ((ulong)hi << 32);
        return true;
    }

    /// <summary>A NUL-terminated ASCII string, or null when the offset is outside the file.
    /// An unterminated run is returned up to <paramref name="maxLength"/> — truncation is data, not failure.</summary>
    public string? AsciiZ(long offset, int maxLength = 1024)
    {
        if (!InRange(offset, 1)) return null;
        int max = (int)Math.Min(maxLength, Length - offset);
        var span = Slice(offset, max);
        if (span.IsEmpty) return null;
        int nul = span.IndexOf((byte)0);
        return Encoding.ASCII.GetString(nul >= 0 ? span[..nul] : span);
    }

    /// <summary>A UTF-16LE run of exactly <paramref name="charCount"/> characters, or null when out of range.</summary>
    public string? Utf16(long offset, int charCount)
    {
        var span = Slice(offset, charCount * 2);
        return span.IsEmpty && charCount > 0 ? null : Encoding.Unicode.GetString(span);
    }

    /// <summary>
    /// Readers currently inside <see cref="ToArray"/>. The mapping must not be torn down underneath
    /// one: releasing the pointer while another thread is reading through it is an access violation
    /// in unsafe code, which the runtime turns into a process-killing
    /// <c>ExecutionEngineException</c> rather than something catchable.
    /// </summary>
    private int _readers;

    /// <summary>Copies a range out to a fresh array, clamped to what actually exists.</summary>
    public byte[] ToArray(long offset, long count)
    {
        Interlocked.Increment(ref _readers);
        try
        {
            if (_disposed || count <= 0 || !InRange(offset, 1)) return [];

            long avail  = Math.Min(count, Length - offset);
            var  result = new byte[avail];
            long done   = 0;
            while (done < avail)
            {
                int chunk = (int)Math.Min(1 << 20, avail - done);
                var span  = Slice(offset + done, chunk);
                if (span.IsEmpty) break;          // disposed underneath us; return what we have
                span.CopyTo(result.AsSpan((int)done, chunk));
                done += chunk;
            }
            return result;
        }
        finally
        {
            Interlocked.Decrement(ref _readers);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Mark first so no new read starts, then let any in-flight one finish before unmapping.
        _disposed = true;
        for (int spins = 0; Volatile.Read(ref _readers) > 0 && spins < 1000; spins++)
            Thread.Sleep(1);

        if (_acquired)
        {
            _view!.SafeMemoryMappedViewHandle.ReleasePointer();
            _acquired = false;
            _ptr      = null;
        }
        _view?.Dispose();
        _mmf?.Dispose();
    }
}
