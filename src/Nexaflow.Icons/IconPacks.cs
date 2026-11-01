using System.Collections.Concurrent;
using System.Text.Json;

namespace Nexaflow.Icons;

/// <summary>
/// The icon packs shipped with the app, and what each of their icons draws (<see cref="VectorIcon"/>). A pack is an Iconify JSON
/// set embedded as <c>Packs/&lt;prefix&gt;.json</c>, its licence beside it, and is named by the prefix Iconify gives it —
/// <c>material-icon-theme</c> — so an icon is the <c>pack:name</c> Iconify writes (<see cref="IconRef.Iconify"/>).
/// <para>
/// A pack is read the first time one of its icons is asked for, keeping each icon's SVG as written; an icon's SVG is read into
/// outlines (<see cref="IconBody"/>) the first time it is drawn, and kept. An alias is the icon it names, with what it changes,
/// and a turn or a flip the pack asks for is done as Iconify does it.
/// </para>
/// </summary>
public static class IconPacks
{
    private const string ResourcePrefix = "Nexaflow.Icons.Packs.";
    private const string ResourceSuffix = ".json";

    private static readonly IReadOnlyDictionary<string, Lazy<Pack>> Shipped = Embedded();

    /// <summary>The prefix of every pack shipped.</summary>
    public static IEnumerable<string> Names => Shipped.Keys;

    /// <summary>Whether a pack of that prefix is shipped.</summary>
    public static bool Ships(string pack) => Shipped.ContainsKey(pack);

    /// <summary>Whether the pack <paramref name="pack"/> is shipped and has an icon called <paramref name="name"/>.</summary>
    public static bool Has(string pack, string name) => Shipped.TryGetValue(pack, out var shipped) && shipped.Value.Has(name);

    /// <summary>The name of every icon of the pack <paramref name="pack"/>, aliases among them — none where no pack of that prefix is shipped.</summary>
    public static IEnumerable<string> Icons(string pack) => Shipped.TryGetValue(pack, out var shipped) ? shipped.Value.Names : [];

    /// <summary>What a pack's icon draws — null for an icon of another set, or one no pack shipped has.</summary>
    public static VectorIcon? Of(IconRef icon) =>
        icon.IsIconify && icon.Value.IndexOf(':') is var colon and > 0 ? Of(icon.Value[..colon], icon.Value[(colon + 1)..]) : null;

    /// <summary>What the icon <paramref name="name"/> of the pack <paramref name="pack"/> draws, or null where no pack shipped has it.</summary>
    public static VectorIcon? Of(string pack, string name) => Shipped.TryGetValue(pack, out var shipped) ? shipped.Value.Drawn(name) : null;

    private static Dictionary<string, Lazy<Pack>> Embedded()
    {
        var assembly = typeof(IconPacks).Assembly;
        var packs = new Dictionary<string, Lazy<Pack>>(StringComparer.Ordinal);

        foreach (var resource in assembly.GetManifestResourceNames())
            if (resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) && resource.EndsWith(ResourceSuffix, StringComparison.Ordinal))
                packs[resource[ResourcePrefix.Length..^ResourceSuffix.Length]] = new Lazy<Pack>(() => Pack.Read(resource));

        return packs;
    }

    /// <summary>One pack, read: each icon's SVG and the box it is drawn in, aliases made the icons they name.</summary>
    private sealed class Pack(Dictionary<string, Entry> icons)
    {
        private readonly ConcurrentDictionary<string, VectorIcon?> _drawn = new(StringComparer.Ordinal);

        public bool Has(string name) => icons.ContainsKey(name);

        public IEnumerable<string> Names => icons.Keys;

        public VectorIcon? Drawn(string name) => icons.TryGetValue(name, out var entry) ? _drawn.GetOrAdd(name, _ => entry.Drawn()) : null;

        public static Pack Read(string resource)
        {
            using var stream = typeof(IconPacks).Assembly.GetManifestResourceStream(resource)
                               ?? throw new InvalidOperationException($"The icon pack '{resource}' is not embedded.");
            using var json = JsonDocument.Parse(stream);
            var root = json.RootElement;

            // What every icon of the pack is drawn in unless it says otherwise — Iconify's own defaults where the pack says nothing.
            var shared = new Entry(string.Empty, Number(root, "left") ?? 0, Number(root, "top") ?? 0, Number(root, "width") ?? 16, Number(root, "height") ?? 16, 0, false, false);
            var icons = new Dictionary<string, Entry>(StringComparer.Ordinal);

            if (root.TryGetProperty("icons", out var listed))
                foreach (var icon in listed.EnumerateObject())
                    if (icon.Value.TryGetProperty("body", out var body) && body.GetString() is { } svg)
                        icons[icon.Name] = shared.Over(icon.Value) with { Body = svg };

            if (root.TryGetProperty("aliases", out var aliases))
            {
                var named = aliases.EnumerateObject().ToDictionary(alias => alias.Name, alias => alias.Value, StringComparer.Ordinal);
                foreach (var name in named.Keys)
                    if (Aliased(name, named, icons, []) is { } entry) icons.TryAdd(name, entry);
            }

            return new Pack(icons);
        }

        /// <summary>The icon an alias names — through any aliases between — with what the alias changes; null where it names nothing.</summary>
        private static Entry? Aliased(string name, Dictionary<string, JsonElement> aliases, Dictionary<string, Entry> icons, HashSet<string> seen)
        {
            if (!aliases.TryGetValue(name, out var alias) || !seen.Add(name)) return null;
            if (!alias.TryGetProperty("parent", out var parent) || parent.GetString() is not { } of) return null;

            var named = icons.GetValueOrDefault(of) ?? Aliased(of, aliases, icons, seen);
            return named?.Over(alias);
        }
    }

    /// <summary>
    /// An icon as a pack keeps it: its SVG, the box it is drawn in, and the quarter turns and flips the pack asks for. What an icon
    /// or an alias says of its box is said over what it stands on; its turns are added on, and its flips undo or make one.
    /// </summary>
    private sealed record Entry(string Body, double Left, double Top, double Width, double Height, int Rotate, bool HorizontalFlip, bool VerticalFlip)
    {
        public Entry Over(JsonElement said) => this with
        {
            Left = Number(said, "left") ?? Left,
            Top = Number(said, "top") ?? Top,
            Width = Number(said, "width") ?? Width,
            Height = Number(said, "height") ?? Height,
            Rotate = Rotate + (int)(Number(said, "rotate") ?? 0),
            HorizontalFlip = HorizontalFlip ^ Flag(said, "hFlip"),
            VerticalFlip = VerticalFlip ^ Flag(said, "vFlip"),
        };

        private static bool Flag(JsonElement element, string property) =>
            element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

        /// <summary>
        /// The icon drawn: its SVG read inside the transform its flips and turns make, in the box they leave it — Iconify's
        /// <c>iconToSVG</c>, step for step, so an icon is turned about the point Iconify turns it about. An icon whose SVG does
        /// not read draws nothing, rather than taking down what it is drawn in.
        /// </summary>
        public VectorIcon? Drawn()
        {
            var (left, top, width, height) = (Left, Top, Width, Height);
            var rotate = Rotate;
            var steps = new List<VectorMatrix>();

            if (HorizontalFlip)
            {
                if (VerticalFlip) rotate += 2;
                else
                {
                    steps.Add(VectorMatrix.Translation(width + left, -top));
                    steps.Add(VectorMatrix.Scaling(-1, 1));
                    top = left = 0;
                }
            }
            else if (VerticalFlip)
            {
                steps.Add(VectorMatrix.Translation(-left, height + top));
                steps.Add(VectorMatrix.Scaling(1, -1));
                top = left = 0;
            }

            rotate = ((rotate % 4) + 4) % 4;
            switch (rotate)
            {
                case 1:
                    steps.Insert(0, VectorMatrix.Rotation(90, (height / 2) + top, (height / 2) + top));
                    break;
                case 2:
                    steps.Insert(0, VectorMatrix.Rotation(180, (width / 2) + left, (height / 2) + top));
                    break;
                case 3:
                    steps.Insert(0, VectorMatrix.Rotation(-90, (width / 2) + left, (width / 2) + left));
                    break;
            }

            if (rotate % 2 == 1) (left, top, width, height) = (top, left, height, width);

            var placed = steps.Aggregate(VectorMatrix.Identity, (outer, inner) => outer * inner);
            try
            {
                return new VectorIcon(left, top, width, height, IconBody.Read(Body, placed, width, height));
            }
            catch (System.Xml.XmlException)
            {
                return null;
            }
        }
    }

    private static double? Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}
