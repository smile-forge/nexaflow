namespace Nexaflow.IO.Pe;

/// <summary>Which of the three resource-directory levels a node sits at.</summary>
public enum PeResourceLevel { Type, Name, Language }

/// <summary>The standard RT_* resource type ids.</summary>
public static class PeResourceTypes
{
    public const int Cursor = 1, Bitmap = 2, Icon = 3, Menu = 4, Dialog = 5, String = 6,
                     FontDir = 7, Font = 8, Accelerator = 9, RcData = 10, MessageTable = 11,
                     GroupCursor = 12, GroupIcon = 14, Version = 16, DlgInclude = 17,
                     PlugPlay = 19, Vxd = 20, AniCursor = 21, AniIcon = 22, Html = 23,
                     Manifest = 24;

    /// <summary>"RT_ICON", "RT_MANIFEST", … or null for a non-standard id.</summary>
    public static string? FriendlyName(int id) => id switch
    {
        Cursor => "RT_CURSOR", Bitmap => "RT_BITMAP", Icon => "RT_ICON", Menu => "RT_MENU",
        Dialog => "RT_DIALOG", String => "RT_STRING", FontDir => "RT_FONTDIR", Font => "RT_FONT",
        Accelerator => "RT_ACCELERATOR", RcData => "RT_RCDATA", MessageTable => "RT_MESSAGETABLE",
        GroupCursor => "RT_GROUP_CURSOR", GroupIcon => "RT_GROUP_ICON", Version => "RT_VERSION",
        DlgInclude => "RT_DLGINCLUDE", PlugPlay => "RT_PLUGPLAY", Vxd => "RT_VXD",
        AniCursor => "RT_ANICURSOR", AniIcon => "RT_ANIICON", Html => "RT_HTML",
        Manifest => "RT_MANIFEST", 18 => "RT_TYPELIB", _ => null,
    };
}

/// <summary>
/// A node in the three-level resource tree (type → name → language). Interior nodes carry
/// <see cref="Children"/>; leaves carry <see cref="DataRva"/> / <see cref="DataSize"/> pointing at
/// the bytes. Ids may be numeric or string — both are kept, since a resource is addressed by
/// whichever one it was declared with.
/// </summary>
public sealed record PeResourceNode(
    PeResourceLevel               Level,
    int?                          Id,
    string?                       Name,
    IReadOnlyList<PeResourceNode> Children)
{
    /// <summary>Leaf only — the RVA of the resource bytes.</summary>
    public uint DataRva { get; init; }

    /// <summary>Leaf only — the length of the resource bytes.</summary>
    public uint DataSize { get; init; }

    /// <summary>Leaf only — the file offset of the resource bytes, when resolvable.</summary>
    public long? DataOffset { get; init; }

    /// <summary>Leaf only — the declared code page, 0 when unspecified.</summary>
    public uint CodePage { get; init; }

    public bool IsLeaf => Children.Count == 0 && DataSize > 0;

    /// <summary>Type-level nodes show "RT_ICON (3)"; others show the name or the numeric id.</summary>
    public string Display => Name
        ?? (Level == PeResourceLevel.Type && Id is { } t && PeResourceTypes.FriendlyName(t) is { } f
                ? $"{f} ({t})"
                : Id?.ToString() ?? "(unnamed)");

    /// <summary>Depth-first walk over this node and everything beneath it.</summary>
    public IEnumerable<PeResourceNode> Descend()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var n in child.Descend())
                yield return n;
    }
}

/// <summary>The parsed resource directory.</summary>
public sealed record PeResources(IReadOnlyList<PeResourceNode> Types)
{
    public static readonly PeResources Empty = new([]);

    public bool IsEmpty => Types.Count == 0;

    /// <summary>Every leaf under a given RT_* type id.</summary>
    public IEnumerable<PeResourceNode> LeavesOfType(int typeId)
        => Types.Where(t => t.Id == typeId).SelectMany(t => t.Descend()).Where(n => n.IsLeaf);

    /// <summary>Top-level entries for a given RT_* type id (the "name" level).</summary>
    public IEnumerable<PeResourceNode> NamesOfType(int typeId)
        => Types.Where(t => t.Id == typeId).SelectMany(t => t.Children);

    public bool HasType(int typeId) => Types.Any(t => t.Id == typeId);

    /// <summary>An embedded type library — the other half of COM registration alongside
    /// <see cref="PeExports.IsComSelfRegistering"/>.</summary>
    public bool HasTypeLib => HasType(18);
}
