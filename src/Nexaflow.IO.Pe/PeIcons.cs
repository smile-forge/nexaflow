using System.Buffers.Binary;

namespace Nexaflow.IO.Pe;

/// <summary>One RT_GROUP_ICON and the reassembled <c>.ico</c> it describes.</summary>
/// <param name="Id">The group's resource id, or null when it is named.</param>
/// <param name="Name">The group's resource name, when it has one instead of an id.</param>
/// <param name="ImageCount">How many sizes/depths the group contains.</param>
/// <param name="IcoBytes">A complete, decodable <c>.ico</c> file.</param>
public sealed record PeIconGroup(int? Id, string? Name, int ImageCount, byte[] IcoBytes)
{
    public string Display => Name ?? Id?.ToString() ?? "(unnamed)";
}

/// <summary>
/// Reassembles Windows icons. An icon is not stored as a file inside a PE: the RT_GROUP_ICON
/// resource is a directory of sizes, and each entry names a <em>separate</em> RT_ICON resource
/// holding just that one image's bits. Producing a usable <c>.ico</c> means rebuilding the
/// directory with file offsets in place of resource ids and concatenating the images behind it.
/// <para>
/// Doing it this way — rather than decoding a single RT_ICON — is what makes both classic DIB icons
/// and the PNG-compressed ones Vista introduced come out right, because the reassembled file is
/// handed to a real icon decoder rather than being interpreted here.
/// </para>
/// </summary>
public static class PeIcons
{
    private const int GroupEntrySize = 14;
    private const int FileEntrySize  = 16;
    private const int DirectoryHeader = 6;

    /// <summary>Every icon group in the image, each as a ready-to-write <c>.ico</c>.</summary>
    public static IReadOnlyList<PeIconGroup> Enumerate(PeImage image)
    {
        var groups = new List<PeIconGroup>();
        foreach (var named in image.Resources.NamesOfType(PeResourceTypes.GroupIcon))
        {
            // The language level sits below the name level; any one of them describes the same group.
            var leaf = named.Descend().FirstOrDefault(n => n.IsLeaf);
            if (leaf is null) continue;
            if (Build(image, leaf) is not { } ico) continue;

            int count = (ico.Length >= DirectoryHeader)
                ? BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)) : 0;
            groups.Add(new PeIconGroup(named.Id, named.Name, count, ico));
        }
        return groups;
    }

    /// <summary>The primary application icon — the lowest-numbered group, which is the one Explorer
    /// shows. Null when the image has no icons.</summary>
    public static PeIconGroup? Primary(PeImage image)
        => Enumerate(image).OrderBy(g => g.Id ?? int.MaxValue).FirstOrDefault();

    /// <summary>
    /// Builds a <c>.ico</c> from one RT_GROUP_ICON leaf, or null when the group is unreadable.
    /// Entries whose RT_ICON is missing are skipped rather than emitted with a dangling offset.
    /// </summary>
    public static byte[]? Build(PeImage image, PeResourceNode groupLeaf)
    {
        var group = image.ReadResource(groupLeaf);
        if (group.Length < DirectoryHeader) return null;

        int count = BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(4));
        if (count <= 0 || DirectoryHeader + count * GroupEntrySize > group.Length) return null;

        // Collect the image bits first: a group can reference an icon that is not actually present,
        // and the directory has to be written with the surviving entries only.
        var images  = new List<(byte[] Header, byte[] Data)>(count);
        for (int i = 0; i < count; i++)
        {
            var entry = group.AsSpan(DirectoryHeader + i * GroupEntrySize, GroupEntrySize);
            ushort iconId = BinaryPrimitives.ReadUInt16LittleEndian(entry[12..]);

            var data = ReadIcon(image, iconId);
            if (data.Length == 0) continue;

            // The first 12 bytes are identical in both entry layouts; only the tail differs
            // (a 2-byte resource id becomes a 4-byte file offset).
            images.Add((entry[..12].ToArray(), data));
        }
        if (images.Count == 0) return null;

        int headerSize = DirectoryHeader + images.Count * FileEntrySize;
        var result     = new byte[headerSize + images.Sum(i => i.Data.Length)];

        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(0), 0);                      // reserved
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), 1);                      // type: icon
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), (ushort)images.Count);

        int offset = headerSize;
        for (int i = 0; i < images.Count; i++)
        {
            var (header, data) = images[i];
            var slot = result.AsSpan(DirectoryHeader + i * FileEntrySize, FileEntrySize);

            header.CopyTo(slot);
            BinaryPrimitives.WriteUInt32LittleEndian(slot[8..],  (uint)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(slot[12..], (uint)offset);

            data.CopyTo(result.AsSpan(offset));
            offset += data.Length;
        }
        return result;
    }

    private static byte[] ReadIcon(PeImage image, ushort iconId)
    {
        foreach (var named in image.Resources.NamesOfType(PeResourceTypes.Icon))
        {
            if (named.Id != iconId) continue;
            var leaf = named.Descend().FirstOrDefault(n => n.IsLeaf);
            if (leaf is not null) return image.ReadResource(leaf);
        }
        return [];
    }
}
