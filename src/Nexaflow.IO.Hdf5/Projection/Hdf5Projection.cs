using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;

namespace Nexaflow.IO.Hdf5.Projection;

/// <summary>How a dataset reads as a file: NumPy binary, or one formatted element per line.</summary>
public enum Hdf5ProjectionFormat { Npy, Text }

/// <summary>
/// Datasets as files, which is how a file browser sees inside an HDF5 container. A dataset whose elements have
/// a fixed size is a NumPy <c>.npy</c> file — dtype and shape in its header, loadable as it is. Anything else
/// (variable-length strings and sequences, references) is a UTF-8 <c>.txt</c> file with one element per line,
/// in C order, formatted as the viewer shows it. Both stream through bounded memory.
/// </summary>
public static class Hdf5Projection
{
    public const string NpyExtension  = ".npy";
    public const string TextExtension = ".txt";

    private const ulong NpyBlockBytes    = 1 << 20;
    private const ulong TextBlockElements = 16 * 1024;

    private static readonly ConditionalWeakTable<IHdf5Source, ConcurrentDictionary<string, long>> TextLengths = new();

    public static Hdf5ProjectionFormat FormatOf(Hdf5Type type) => type.IsFixedSize ? Hdf5ProjectionFormat.Npy : Hdf5ProjectionFormat.Text;

    /// <summary><c>temperature</c> as a float64 dataset is <c>temperature.npy</c>.</summary>
    public static string EntryName(string datasetName, Hdf5Type type) =>
        datasetName + (FormatOf(type) == Hdf5ProjectionFormat.Npy ? NpyExtension : TextExtension);

    /// <summary>
    /// The dataset path an entry path names, when it ends in a projection extension: <c>/a/b.npy</c> is
    /// <c>/a/b</c>. Exactly one extension comes off, so a dataset already called <c>b.npy</c> is <c>b.npy.npy</c>.
    /// </summary>
    public static string? DatasetPathOf(string entryPath, out Hdf5ProjectionFormat format)
    {
        var p = Hdf5Path.Normalize(entryPath);
        foreach (var (ext, f) in new[] { (NpyExtension, Hdf5ProjectionFormat.Npy), (TextExtension, Hdf5ProjectionFormat.Text) })
            if (p.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && p.Length > ext.Length + 1 && p[^(ext.Length + 1)] != '/')
            {
                format = f;
                return p[..^ext.Length];
            }
        format = default;
        return null;
    }

    /// <summary>The exact byte length of a dataset's projection.</summary>
    public static long Length(IHdf5Source source, Hdf5Object dataset, CancellationToken ct = default)
    {
        var info = RequireDataset(dataset);
        if (FormatOf(info.Type) == Hdf5ProjectionFormat.Npy)
            return NpyHeader.Build(info.Type, info.Space).Length + checked((long)info.Space.ElementCount * info.Type.Size);

        // Text has no length until it is formatted: format it once, counting, and keep the count with the source.
        var lengths = TextLengths.GetValue(source, _ => new ConcurrentDictionary<string, long>(StringComparer.Ordinal));
        return lengths.GetOrAdd(dataset.Path, _ =>
        {
            long total = 0;
            foreach (var chunk in TextChunks(source, dataset.Path, info, ct)) total += chunk.Length;
            return total;
        });
    }

    /// <summary>A forward-only stream of a dataset's projection, read as it is consumed.</summary>
    public static Stream Open(IHdf5Source source, Hdf5Object dataset, CancellationToken ct = default)
    {
        var info = RequireDataset(dataset);
        return new ChunkStream(FormatOf(info.Type) == Hdf5ProjectionFormat.Npy
            ? NpyChunks(source, dataset.Path, info, ct)
            : TextChunks(source, dataset.Path, info, ct));
    }

    /// <summary>One element as a projection line: backslash, newline, return and tab are escaped.</summary>
    public static string TextLine(string formatted) =>
        formatted.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    private static IEnumerable<byte[]> NpyChunks(IHdf5Source source, string path, Hdf5DatasetInfo info, CancellationToken ct)
    {
        yield return NpyHeader.Build(info.Type, info.Space);
        foreach (var selection in Selections(info, Math.Max(1, NpyBlockBytes / (ulong)Math.Max(1, info.Type.Size))))
        {
            ct.ThrowIfCancellationRequested();
            var block = source.ReadBlock(path, selection, ct);
            var bytes = new byte[block.Count * info.Type.Size];
            block.CopyTo(bytes);
            yield return bytes;
        }
    }

    private static IEnumerable<byte[]> TextChunks(IHdf5Source source, string path, Hdf5DatasetInfo info, CancellationToken ct)
    {
        var sb = new StringBuilder();
        foreach (var selection in Selections(info, TextBlockElements))
        {
            ct.ThrowIfCancellationRequested();
            var block = source.ReadBlock(path, selection, ct);
            sb.Clear();
            for (long i = 0; i < block.Count; i++) sb.Append(TextLine(block.Format(i))).Append('\n');
            yield return Encoding.UTF8.GetBytes(sb.ToString());
        }
    }

    private static IEnumerable<Hdf5Selection> Selections(Hdf5DatasetInfo info, ulong budget) => info.Space.Kind switch
    {
        Hdf5SpaceKind.Null   => [],
        Hdf5SpaceKind.Scalar => [Hdf5Selection.Scalar],
        _                    => Hdf5Selection.Walk(info.Space.Shape, budget),
    };

    private static Hdf5DatasetInfo RequireDataset(Hdf5Object o) =>
        o.Dataset ?? throw new ArgumentException($"{o.Path} is not a dataset.", nameof(o));

    /// <summary>Serves a sequence of byte chunks as a read-only, forward-only stream.</summary>
    private sealed class ChunkStream(IEnumerable<byte[]> chunks) : Stream
    {
        private readonly IEnumerator<byte[]> _chunks = chunks.GetEnumerator();
        private byte[] _current = [];
        private int _offset;
        private long _position;

        public override bool CanRead  => true;
        public override bool CanSeek  => false;
        public override bool CanWrite => false;
        public override long Length   => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            while (_offset == _current.Length)
            {
                if (!_chunks.MoveNext()) return 0;
                _current = _chunks.Current;
                _offset  = 0;
            }
            int n = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsSpan(_offset, n).CopyTo(buffer);
            _offset   += n;
            _position += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _chunks.Dispose();
            base.Dispose(disposing);
        }
    }
}
