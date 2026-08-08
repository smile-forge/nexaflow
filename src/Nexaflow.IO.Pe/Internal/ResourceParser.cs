namespace Nexaflow.IO.Pe.Internal;

/// <summary>
/// The resource directory — three nested levels (type → name → language) ending in data entries.
/// Every offset inside it is relative to the directory root rather than being an RVA, except the
/// leaf's <c>OffsetToData</c>, which switches back to an RVA. Getting that wrong is the classic
/// resource-parsing bug, so the two are named apart throughout.
/// <para>
/// Subdirectory offsets are attacker-controlled and can point at each other, so the walk carries a
/// visited set as well as a depth cap.
/// </para>
/// </summary>
internal static class ResourceParser
{
    private const int MaxDepth = 3;

    public static PeResources Parse(PeImage image, PeReadOptions options)
    {
        if (image.Directory(PeDirectory.Resource) is not { IsPresent: true } dir) return PeResources.Empty;
        if (image.RvaToFileOffset(dir.VirtualAddress) is not { } root)
        {
            image.Add(new PeDiagnostic(PeSeverity.Error, "Resources",
                $"The resource directory RVA 0x{dir.VirtualAddress:X} does not map into any section."));
            return PeResources.Empty;
        }

        var visited = new HashSet<uint>();
        var types   = ReadDirectory(image, options, root, 0, PeResourceLevel.Type, visited);
        return types.Count == 0 ? PeResources.Empty : new PeResources(types);
    }

    private static List<PeResourceNode> ReadDirectory(
        PeImage image, PeReadOptions options, long root, uint directoryOffset,
        PeResourceLevel level, HashSet<uint> visited)
    {
        var result = new List<PeResourceNode>();

        if (!visited.Add(directoryOffset))
        {
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Resources",
                $"The resource tree loops back to offset 0x{directoryOffset:X}; that branch was skipped."));
            return result;
        }

        var  buf   = image.Buffer;
        long here  = root + directoryOffset;
        if (!buf.InRange(here, 16)) return result;

        buf.TryU16(here + 12, out ushort namedCount);
        buf.TryU16(here + 14, out ushort idCount);

        int total = namedCount + idCount;
        if (total > options.MaxTableEntries)
        {
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Resources",
                $"A resource directory declares {total} entries; capped at {options.MaxTableEntries}.", here));
            total = options.MaxTableEntries;
        }

        for (int i = 0; i < total; i++)
        {
            long entry = here + 16 + i * 8L;
            if (!buf.InRange(entry, 8)) break;

            buf.TryU32(entry,     out uint nameField);
            buf.TryU32(entry + 4, out uint dataField);

            int?    id   = null;
            string? name = null;
            if ((nameField & 0x8000_0000) != 0)
                name = ReadDirectoryString(image, root, nameField & 0x7FFF_FFFF);
            else
                id = (int)nameField;

            bool isSubdirectory = (dataField & 0x8000_0000) != 0;
            uint childOffset    = dataField & 0x7FFF_FFFF;

            if (isSubdirectory)
            {
                if (level == PeResourceLevel.Language)
                {
                    // A fourth level is not part of the format; stop rather than recurse blindly.
                    image.Add(new PeDiagnostic(PeSeverity.Warning, "Resources",
                        "A resource subdirectory appears below the language level and was ignored.", entry));
                    continue;
                }
                var children = ReadDirectory(image, options, root, childOffset, level + 1, visited);
                result.Add(new PeResourceNode(level, id, name, children));
            }
            else
            {
                result.Add(ReadDataEntry(image, options, root, childOffset, level, id, name));
            }
        }

        // Sibling branches may legitimately share a subdirectory; only cycles within one path matter.
        visited.Remove(directoryOffset);
        return result;
    }

    private static PeResourceNode ReadDataEntry(
        PeImage image, PeReadOptions options, long root, uint entryOffset,
        PeResourceLevel level, int? id, string? name)
    {
        var  buf  = image.Buffer;
        long here = root + entryOffset;

        if (!buf.InRange(here, 16))
        {
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Resources",
                $"A resource data entry at 0x{here:X} is truncated.", here));
            return new PeResourceNode(level, id, name, []);
        }

        buf.TryU32(here,     out uint dataRva);
        buf.TryU32(here + 4, out uint size);
        buf.TryU32(here + 8, out uint codePage);

        if (size > options.MaxResourceBytes)
        {
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Resources",
                $"Resource entry declares {size} bytes, beyond the {options.MaxResourceBytes}-byte limit; " +
                "its data will not be read.", here));
            size = 0;
        }

        // This one field is an RVA again, not a root-relative offset.
        long? dataOffset = image.RvaToFileOffset(dataRva);
        if (dataOffset is null && size > 0)
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Resources",
                $"Resource data RVA 0x{dataRva:X} does not map into any section.", here));

        return new PeResourceNode(level, id, name, [])
        {
            DataRva    = dataRva,
            DataSize   = size,
            DataOffset = dataOffset,
            CodePage   = codePage,
        };
    }

    /// <summary>A length-prefixed UTF-16 string (not NUL-terminated) at a root-relative offset.</summary>
    private static string? ReadDirectoryString(PeImage image, long root, uint offset)
    {
        var  buf  = image.Buffer;
        long here = root + offset;
        if (!buf.TryU16(here, out ushort length) || length == 0) return null;
        return buf.Utf16(here + 2, Math.Min(length, (ushort)1024));
    }
}
