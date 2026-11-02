using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Nexaflow.IO.Common.OpenRaster;

/// <summary>A file a package is written with besides <c>mimetype</c> and <c>stack.xml</c>: its path inside it, its content as
/// it is written, and whether it is stored as it is — a PNG, compressed already.</summary>
public sealed record OpenRasterFile(string Path, Func<Stream> Open, bool Stored);

/// <summary>
/// An OpenRaster package (openraster.org, 0.0.6) through the file system: a zip whose first entry is <c>mimetype</c>, stored,
/// holding <c>image/openraster</c>; <c>stack.xml</c>, the layer stack, read into a tree (<see cref="OpenRasterImage"/>) and
/// written back with whatever it held that the baseline does not name; and the files it names, read entry by entry — by
/// their path inside the package (<c>picture.ora\stack.xml</c>) — so opening one reads only what is needed. Pixel-free: PNGs
/// go in and out as streams, so anything can read an <c>.ora</c>'s merged image without knowing layers.
/// </summary>
public static class OpenRasterPackage
{
    public const string MimeType = "image/openraster";

    public const string Extension = ".ora";
    public const string WrittenVersion = "0.0.6";
    public const string StackPath = "stack.xml";
    public const string MergedImagePath = "mergedimage.png";
    public const string ThumbnailPath = "Thumbnails/thumbnail.png";
    public const string MimeTypePath = "mimetype";

    /// <summary>The longest side a thumbnail may have.</summary>
    public const int ThumbnailMaxSide = 256;

    private static readonly HashSet<string> LayerAttributes = ["src", "x", "y", "name", "opacity", "visibility", "composite-op", "selected", "edit-locked", "alpha-preserve"];
    private static readonly HashSet<string> StackAttributes = ["x", "y", "name", "opacity", "visibility", "composite-op", "isolation", "selected", "edit-locked"];
    private static readonly HashSet<string> ImageAttributes = ["w", "h", "xres", "yres", "version"];

    /// <summary>The path of <paramref name="entry"/> inside the package at <paramref name="package"/>, as the file system reads it.</summary>
    public static string EntryPath(string package, string entry) => package + "\\" + entry.Replace('/', '\\');

    /// <summary>Whether <paramref name="package"/> is an OpenRaster package: its <c>mimetype</c> says so.</summary>
    public static async Task<bool> IsOpenRasterAsync(IVirtualFileSystem fileSystem, string package, CancellationToken ct = default)
    {
        try
        {
            var path = EntryPath(package, MimeTypePath);
            return await fileSystem.ExistsAsync(path, ct).ConfigureAwait(false)
                   && Encoding.ASCII.GetString(await fileSystem.ReadAllBytesAsync(path, ct).ConfigureAwait(false)).Trim() == MimeType;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>The package's layer stack.</summary>
    /// <exception cref="InvalidDataException">It has no stack, or one that does not read.</exception>
    public static async Task<OpenRasterImage> ReadStackAsync(IVirtualFileSystem fileSystem, string package, CancellationToken ct = default)
    {
        var path = EntryPath(package, StackPath);
        if (!await fileSystem.ExistsAsync(path, ct).ConfigureAwait(false)) throw new InvalidDataException($"{package} has no {StackPath}.");
        return Parse(await fileSystem.ReadAllBytesAsync(path, ct).ConfigureAwait(false));
    }

    /// <summary>Every file in the package, by its path inside it, forward-slash separated.</summary>
    public static async Task<IReadOnlyList<string>> FilesAsync(IVirtualFileSystem fileSystem, string package, CancellationToken ct = default)
    {
        var files = new List<string>();
        async Task Walk(string folder, string prefix)
        {
            await foreach (var entry in fileSystem.EnumerateAsync(folder, ct).ConfigureAwait(false))
            {
                var leaf = entry.Name.Split('/', '\\')[^1];
                if (entry.IsDirectory) await Walk(folder + "\\" + leaf, prefix + leaf + "/").ConfigureAwait(false);
                else files.Add(prefix + leaf);
            }
        }
        await Walk(package, "").ConfigureAwait(false);
        return files;
    }

    /// <summary>
    /// Writes the package at <paramref name="package"/>, whole: <c>mimetype</c> first and stored, <c>stack.xml</c>, then
    /// <paramref name="files"/> in the order given, each as it asks — built beside the destination and swapped in, so a reader
    /// never meets a half-written one. The files' content is read as it is written, so it may come from the package being
    /// replaced.
    /// </summary>
    public static ValueTask WriteAsync(IVirtualFileSystem fileSystem, string package, OpenRasterImage image, IReadOnlyList<OpenRasterFile> files,
                                       CancellationToken ct = default)
    {
        var stack = Serialize(image);
        var mime = Encoding.ASCII.GetBytes(MimeType);
        var entries = new List<ArchiveWriteEntry>
        {
            new() { Path = MimeTypePath, OpenContent = () => new MemoryStream(mime), Length = mime.Length, Compression = ArchiveEntryCompression.Stored },
            new() { Path = StackPath, OpenContent = () => new MemoryStream(stack), Length = stack.Length },
        };
        foreach (var file in files)
            entries.Add(new ArchiveWriteEntry
            {
                Path = file.Path, OpenContent = file.Open,
                Compression = file.Stored ? ArchiveEntryCompression.Stored : ArchiveEntryCompression.Default,
            });
        return fileSystem.CreateArchiveAsync(package, entries, ct: ct);
    }

    // ── stack.xml ───────────────────────────────────────────────────────────

    /// <summary>A <c>stack.xml</c> read into its tree.</summary>
    /// <exception cref="InvalidDataException">It is not an OpenRaster stack.</exception>
    public static OpenRasterImage Parse(byte[] xml)
    {
        XDocument document;
        try
        {
            using var stream = new MemoryStream(xml);
            document = XDocument.Load(stream, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"{StackPath} does not read: {ex.Message}", ex);
        }
        var image = document.Root is { Name.LocalName: "image" } root ? root : throw new InvalidDataException($"{StackPath} holds no image.");
        var rootStack = image.Elements().FirstOrDefault(e => e.Name.LocalName == "stack" && e.Name.Namespace == XNamespace.None)
                        ?? throw new InvalidDataException($"{StackPath} holds no stack.");
        return new OpenRasterImage
        {
            Width = Int(image, "w") ?? throw new InvalidDataException("The image has no width."),
            Height = Int(image, "h") ?? throw new InvalidDataException("The image has no height."),
            XResolution = Number(image, "xres"),
            YResolution = Number(image, "yres"),
            Version = (string?)image.Attribute("version") ?? "",
            Root = ReadStackElement(rootStack),
            Kept = KeptOf(image, ImageAttributes, image.Elements().Where(e => e != rootStack)),
        };
    }

    private static OpenRasterEntry ReadEntry(XElement element) => element.Name switch
    {
        { LocalName: "layer", Namespace: var ns } when ns == XNamespace.None => new OpenRasterLayerEntry
        {
            Source = (string?)element.Attribute("src") ?? "",
            X = Int(element, "x") ?? 0,
            Y = Int(element, "y") ?? 0,
            AlphaPreserve = Bool(element, "alpha-preserve"),
            Name = (string?)element.Attribute("name") ?? "",
            Opacity = Math.Clamp(Number(element, "opacity") ?? 1, 0, 1),
            Visible = !string.Equals((string?)element.Attribute("visibility"), "hidden", StringComparison.OrdinalIgnoreCase),
            CompositeOp = (string?)element.Attribute("composite-op"),
            Selected = Bool(element, "selected"),
            EditLocked = Bool(element, "edit-locked"),
            Kept = KeptOf(element, LayerAttributes, element.Elements()),
        },
        { LocalName: "stack", Namespace: var ns } when ns == XNamespace.None => ReadStackElement(element),
        _ => new OpenRasterOtherEntry
        {
            ElementName = element.Name.LocalName,
            Type = (string?)element.Attribute("type"),
            Element = element.ToString(SaveOptions.DisableFormatting),
            Name = (string?)element.Attribute("name") ?? "",
            Opacity = Math.Clamp(Number(element, "opacity") ?? 1, 0, 1),
            Visible = !string.Equals((string?)element.Attribute("visibility"), "hidden", StringComparison.OrdinalIgnoreCase),
            CompositeOp = (string?)element.Attribute("composite-op"),
            Selected = Bool(element, "selected"),
            EditLocked = Bool(element, "edit-locked"),
        },
    };

    private static OpenRasterStackEntry ReadStackElement(XElement element)
    {
        bool IsEntry(XElement child) => child.Name.Namespace == XNamespace.None || child.Attribute("type") is not null;
        return new OpenRasterStackEntry
        {
            Children = [.. element.Elements().Where(IsEntry).Select(ReadEntry)],
            Isolation = (string?)element.Attribute("isolation") ?? "isolate",
            X = Int(element, "x") ?? 0,
            Y = Int(element, "y") ?? 0,
            Name = (string?)element.Attribute("name") ?? "",
            Opacity = Math.Clamp(Number(element, "opacity") ?? 1, 0, 1),
            Visible = !string.Equals((string?)element.Attribute("visibility"), "hidden", StringComparison.OrdinalIgnoreCase),
            CompositeOp = (string?)element.Attribute("composite-op"),
            Selected = Bool(element, "selected"),
            EditLocked = Bool(element, "edit-locked"),
            Kept = KeptOf(element, StackAttributes, element.Elements().Where(c => !IsEntry(c))),
        };
    }

    private static OpenRasterKept KeptOf(XElement element, HashSet<string> baseline, IEnumerable<XElement> others) => new()
    {
        Attributes = [.. element.Attributes()
            .Where(a => !a.IsNamespaceDeclaration && !(a.Name.Namespace == XNamespace.None && baseline.Contains(a.Name.LocalName)))
            .Select(a => new OpenRasterAttribute(a.Name.NamespaceName, a.Name.LocalName, a.Value))],
        Elements = [.. others.Select(e => e.ToString(SaveOptions.DisableFormatting))],
    };

    /// <summary>The tree written as a <c>stack.xml</c>: version 0.0.6 unless it says otherwise, no offset on a stack, every kept
    /// attribute and element back where it was read, each namespace declared once on the image.</summary>
    public static byte[] Serialize(OpenRasterImage image)
    {
        var root = new XElement("image",
            new XAttribute("w", image.Width), new XAttribute("h", image.Height),
            new XAttribute("version", string.IsNullOrEmpty(image.Version) ? WrittenVersion : image.Version));
        if (image.XResolution is { } xres) root.Add(new XAttribute("xres", Format(xres)));
        if (image.YResolution is { } yres) root.Add(new XAttribute("yres", Format(yres)));
        AddKept(root, image.Kept);
        root.Add(WriteEntry(image.Root));

        // Each namespace a kept attribute uses, declared once, on the image.
        var prefixes = new Dictionary<string, string>();
        foreach (var attribute in root.DescendantsAndSelf().SelectMany(e => e.Attributes()))
        {
            var ns = attribute.Name.NamespaceName;
            if (ns.Length == 0 || attribute.IsNamespaceDeclaration || prefixes.ContainsKey(ns)) continue;
            prefixes[ns] = PrefixFor(ns, prefixes.Count);
        }
        foreach (var (ns, prefix) in prefixes) root.Add(new XAttribute(XNamespace.Xmlns + prefix, ns));

        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings)) new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Save(writer);
        return stream.ToArray();
    }

    /// <summary>The prefix a namespace is declared with: its usual one where it is known, else one made up.</summary>
    private static string PrefixFor(string ns, int index) => ns switch
    {
        OpenRasterNamespaces.Nexaflow => "nexaflow",
        OpenRasterNamespaces.Krita => "krita",
        _ => $"ns{index + 1}",
    };

    private static XElement WriteEntry(OpenRasterEntry entry)
    {
        XElement element;
        switch (entry)
        {
            case OpenRasterLayerEntry layer:
                element = new XElement("layer", new XAttribute("src", layer.Source), new XAttribute("x", layer.X), new XAttribute("y", layer.Y));
                if (layer.AlphaPreserve) element.Add(new XAttribute("alpha-preserve", "true"));
                break;
            case OpenRasterStackEntry stack:
                element = new XElement("stack", new XAttribute("isolation", stack.Isolation));
                break;
            case OpenRasterOtherEntry other:
                return XElement.Parse(other.Element, LoadOptions.PreserveWhitespace);
            default:
                throw new NotSupportedException($"No element for a {entry.GetType().Name}.");
        }
        element.Add(new XAttribute("name", entry.Name), new XAttribute("opacity", Format(entry.Opacity)),
                    new XAttribute("visibility", entry.Visible ? "visible" : "hidden"),
                    new XAttribute("composite-op", entry.CompositeOp ?? "svg:src-over"));
        if (entry.Selected) element.Add(new XAttribute("selected", "true"));
        if (entry.EditLocked) element.Add(new XAttribute("edit-locked", "true"));
        AddKept(element, entry.Kept);
        if (entry is OpenRasterStackEntry { Children: var children })
            foreach (var child in children) element.Add(WriteEntry(child));
        return element;
    }

    private static void AddKept(XElement element, OpenRasterKept kept)
    {
        foreach (var attribute in kept.Attributes)
        {
            XName name = attribute.Namespace.Length == 0 ? attribute.LocalName : XNamespace.Get(attribute.Namespace) + attribute.LocalName;
            if (element.Attribute(name) is null) element.Add(new XAttribute(name, attribute.Value));
        }
        foreach (var xml in kept.Elements) element.Add(XElement.Parse(xml, LoadOptions.PreserveWhitespace));
    }

    private static int? Int(XElement element, string name)
        => Number(element, name) is { } value ? (int)Math.Round(value) : null;

    private static double? Number(XElement element, string name)
        => double.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static bool Bool(XElement element, string name)
        => string.Equals((string?)element.Attribute(name), "true", StringComparison.OrdinalIgnoreCase);

    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}

/// <summary>The XML namespaces OpenRaster files carry beside the baseline.</summary>
public static class OpenRasterNamespaces
{
    /// <summary>What only Nexaflow reads: a picture layer's source and recipe, a markup layer's annotations, a mask's roles.</summary>
    public const string Nexaflow = "https://smile-forge.github.io/nexaflow/ns/openraster";

    /// <summary>Krita's own.</summary>
    public const string Krita = "http://krita.org/namespaces/svg/krita";
}
