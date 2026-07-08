using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexaflow.Features.Font.Decoding;

/// <summary>
/// Turns a font file path into a <see cref="Uri"/> that WPF can render/inspect. Raw sfnt files
/// (<c>.ttf/.otf/.ttc</c>) pass straight through; wrapped formats are handed to a matching
/// <see cref="IFontFormatDecoder"/>, decoded to sfnt bytes, and written to a per-instance temp file
/// whose Uri is returned (WPF resolves glyphs from it lazily, so it must outlive the tab). Unsupported
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
    /// A renderable Uri for <paramref name="path"/>, or null if the format has no decoder. Throws
    /// (bad container, unreadable file) only via the decoder — callers wrap this in try/catch.
    /// </summary>
    public Uri? ResolveRenderableUri(string path)
    {
        var full = Path.GetFullPath(path);
        var ext = Path.GetExtension(full).ToLowerInvariant();

        if (RawSfntExtensions.Contains(ext))
            return new Uri(full);

        var decoder = _decoders.FirstOrDefault(d => d.CanDecode(ext));
        if (decoder is null) return null;

        byte[] sfnt;
        using (var fs = File.OpenRead(full))
            sfnt = decoder.DecodeToSfnt(fs);

        Directory.CreateDirectory(_tempDir);
        // 'OTTO' magic marks a CFF/OpenType outline table set → .otf, otherwise TrueType → .ttf.
        var outExt = sfnt.Length >= 4 && sfnt[0] == (byte)'O' && sfnt[1] == (byte)'T'
                     && sfnt[2] == (byte)'T' && sfnt[3] == (byte)'O' ? ".otf" : ".ttf";
        var temp = Path.Combine(_tempDir,
            Path.GetFileNameWithoutExtension(full) + "-" + Guid.NewGuid().ToString("N")[..8] + outExt);
        File.WriteAllBytes(temp, sfnt);
        _tempFiles.Add(temp);
        return new Uri(temp);
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { /* WPF may still hold the handle; purged at OS temp cleanup. */ }
        }
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
