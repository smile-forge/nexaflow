using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Xml;
using SharpVectors.Converters;
using SharpVectors.Dom;
using SharpVectors.Renderers.Wpf;
using SharpVectors.Runtime;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Svg.Loaders;

/// <summary>
/// Parses an SVG (or gzipped <c>.svgz</c>) file into a frozen WPF <see cref="DrawingImage"/> via
/// SharpVectors, off the UI thread. Text is emitted as geometry and external/interactive content is
/// disabled so everything the reader produces is a <see cref="Freezable"/> that can be built on a
/// background thread and handed to the UI (mirrors the Model3D loader's freeze-on-background pattern).
///
/// Security: the file is opened through an XXE-hardened, stream-backed <see cref="XmlReader"/> (no DTD,
/// no external entity resolution, no base URI) and SharpVectors is told to ignore external resource
/// references — so an untrusted SVG can't trigger network fetches or entity-expansion attacks.
/// </summary>
public sealed class SvgLoader
{
    private static readonly HashSet<string> ShapeElements = new(StringComparer.Ordinal)
    {
        "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "text", "image", "use",
    };

    public LoadedSvg Load(string path)
    {
        var settings = BuildSettings();

        DrawingGroup group;
        using (var reader = OpenSvg(path))
        {
            var svgReader = new FileSvgReader(settings);
            group = svgReader.Read(reader) ?? new DrawingGroup();
        }

        if (group.CanFreeze && !group.IsFrozen) group.Freeze(); // now safe to cross to the UI thread

        var bounds = group.Bounds;
        var image = new DrawingImage(group);
        image.Freeze();

        var (width, height, viewBox, count) = ProbeMetadata(path);

        return new LoadedSvg
        {
            Image = image,
            Bounds = bounds,
            Width = width,
            Height = height,
            ViewBox = viewBox,
            ElementCount = count,
        };
    }

    /// <summary>Static, security-hardened render settings — see the class remarks.</summary>
    private static WpfDrawingSettings BuildSettings() => new()
    {
        IncludeRuntime = false,       // no SvgObject DependencyObject wrappers — a static viewer doesn't need them
        TextAsGeometry = true,        // text → PathGeometry: freezable off-thread and crisp under zoom
        OptimizePath = true,
        EnsureViewboxSize = true,     // preserve the authored intrinsic size
        InteractiveMode = SvgInteractiveModes.None,
        ExternalResourcesAccessMode = ExternalResourcesAccessModes.Ignore, // no network / external-href fetches
        CultureInfo = CultureInfo.InvariantCulture,
    };

    /// <summary>Opens the file as an XXE-hardened <see cref="XmlReader"/>, transparently gunzipping when the
    /// content is gzip (covers <c>.svgz</c> and gzip content mislabeled <c>.svg</c>).</summary>
    private static XmlReader OpenSvg(string path)
    {
        // Read through the VFS so an .svg inside a disk image / archive resolves (real files pass through).
        Stream fs = VirtualFileSystem.Instance.OpenRead(path);
        try
        {
            int b0 = fs.ReadByte(), b1 = fs.ReadByte();
            fs.Position = 0;
            Stream svg = (b0 == 0x1F && b1 == 0x8B) ? new GZipStream(fs, CompressionMode.Decompress) : fs;

            return XmlReader.Create(svg, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore, // block external DTDs
                XmlResolver = null,                    // no external entity / DTD resolution → kills XXE
                MaxCharactersFromEntities = 1024,
                CloseInput = true,                     // disposing the reader closes the whole stream chain
            });
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    /// <summary>Lightweight streaming pass over the same (gzip-aware) content for the footer metadata: the
    /// root <c>&lt;svg&gt;</c> width/height/viewBox and a drawable-element count. Cosmetic — failures are
    /// swallowed.</summary>
    private static (string? Width, string? Height, string? ViewBox, int Count) ProbeMetadata(string path)
    {
        string? width = null, height = null, viewBox = null;
        int count = 0;
        try
        {
            using var reader = OpenSvg(path);
            bool rootSeen = false;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                var name = reader.LocalName;
                if (!rootSeen && name == "svg")
                {
                    rootSeen = true;
                    width = reader.GetAttribute("width");
                    height = reader.GetAttribute("height");
                    viewBox = reader.GetAttribute("viewBox");
                }
                if (ShapeElements.Contains(name)) count++;
            }
        }
        catch { /* metadata is cosmetic */ }
        return (width, height, viewBox, count);
    }
}
