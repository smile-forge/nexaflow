using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Font.Decoding;

/// <summary>
/// Turns a font file path into a <see cref="Uri"/> that WPF can render/inspect. Raw sfnt files
/// (<c>.ttf/.otf/.ttc</c>) on this disk pass straight through; one in an archive or on a location is fetched
/// once into a per-instance temp file, and wrapped formats are handed to a matching
/// <see cref="IFontFormatDecoder"/>, decoded to sfnt bytes, and written to one too — its Uri is returned
/// (WPF resolves glyphs from it lazily, so it must outlive the tab). Unsupported
/// formats return null. Owned by the page view-model; <see cref="Dispose"/> best-effort-deletes the
/// temp files. Adding a format = one more decoder in <see cref="_decoders"/>.
/// </summary>
public sealed class FontSourceResolver : IDisposable
{
    private static readonly HashSet<string> RawSfntExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".ttf", ".otf", ".ttc" };

    private readonly IReadOnlyList<IFontFormatDecoder> _decoders = [new WoffDecoder()];
    private readonly List<string> _tempFiles = [];
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "nexaflow-fonts", Guid.NewGuid().ToString("N"));

    /// <summary>The extensions a resolver can render (raw sfnt + every registered decoder's).</summary>
    public bool IsSupported(string extension) =>
        RawSfntExtensions.Contains(extension) || _decoders.Any(d => d.CanDecode(extension));

    /// <summary>
    /// A renderable Uri for <paramref name="path"/>, read through <paramref name="vfs"/>, or null if the format has no
    /// decoder. Throws (bad container, unreadable file) — callers wrap this in try/catch.
    /// </summary>
    public async Task<Uri?> ResolveRenderableUriAsync(IVirtualFileSystem vfs, string path, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        bool raw = RawSfntExtensions.Contains(ext);
        var decoder = raw ? null : _decoders.FirstOrDefault(d => d.CanDecode(ext));
        if (!raw && decoder is null) return null;

        // WPF reads glyphs from a file of its own: one on this disk is handed over as it is.
        if (raw && vfs.ResolveOnDisk(path) is { } real)
            return new Uri(Path.GetFullPath(real));

        var bytes = await vfs.ReadAllBytesAsync(path, ct);
        if (raw) return Keep(bytes, Path.GetFileNameWithoutExtension(path), ext);

        var sfnt = decoder!.DecodeToSfnt(new MemoryStream(bytes, writable: false));
        // 'OTTO' magic marks a CFF/OpenType outline table set → .otf, otherwise TrueType → .ttf.
        var outExt = sfnt.Length >= 4 && sfnt[0] == (byte)'O' && sfnt[1] == (byte)'T'
                     && sfnt[2] == (byte)'T' && sfnt[3] == (byte)'O' ? ".otf" : ".ttf";
        return Keep(sfnt, Path.GetFileNameWithoutExtension(path), outExt);
    }

    /// <summary>A font's bytes written to a file of this tab's own, for WPF to read lazily.</summary>
    [LocalDiskOnly(OwnFolder)]
    private Uri Keep(byte[] sfnt, string name, string extension)
    {
        Directory.CreateDirectory(_tempDir);
        var temp = Path.Combine(_tempDir, name + "-" + Guid.NewGuid().ToString("N")[..8] + extension);
        File.WriteAllBytes(temp, sfnt);
        _tempFiles.Add(temp);
        return new Uri(temp);
    }

    /// <summary>Why the kept fonts are on the disk directly: they are this tab's own, in its folder under the temp folder.</summary>
    private const string OwnFolder = "fonts fetched or decoded for WPF, kept in this tab's own folder under the temp folder";

    [LocalDiskOnly(OwnFolder)]
    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { /* WPF may still hold the handle; purged at OS temp cleanup. */ }
        }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
