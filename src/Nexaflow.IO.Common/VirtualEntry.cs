namespace Nexaflow.IO.Common;

/// <summary>
/// One item inside a virtual location — a real directory's child, or an entry inside an archive.
/// Sizes are bytes; <see cref="CompressedSize"/> equals <see cref="Size"/> for real files (and for
/// stored/uncompressed entries). <see cref="Crc"/>/<see cref="Method"/> are archive-only (0/null on
/// the real file system).
/// </summary>
public sealed record VirtualEntry(
    string Name,
    bool IsDirectory,
    long Size,
    long CompressedSize,
    System.DateTime Modified,
    uint Crc = 0,
    string? Method = null)
{
    /// <summary>Compression ratio in [0,1] (1 = fully compressed away), or null for directories /
    /// empty / unknown-size entries. <c>0.6</c> means the entry compressed to 40% of its size.</summary>
    public double? Ratio =>
        IsDirectory || Size <= 0 || CompressedSize < 0 ? null : 1.0 - (double)CompressedSize / Size;
}
