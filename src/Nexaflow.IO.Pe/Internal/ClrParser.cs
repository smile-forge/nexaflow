using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Nexaflow.IO.Pe.Internal;

/// <summary>
/// The managed half of an image: the COR20 header (parsed by hand, like every other directory) and
/// then the metadata tables (read with the in-box <c>System.Reflection.Metadata</c>, which already
/// solves that format properly).
/// <para>
/// The split matters for tolerance. The COR20 header alone answers "is this managed, IL-only,
/// strong-named?" and cannot fail once the directory is in range. The metadata read is best-effort
/// on top: an obfuscated or truncated metadata root loses the assembly references but must not lose
/// the header.
/// </para>
/// </summary>
internal static class ClrParser
{
    private const string TargetFrameworkAttribute = "TargetFrameworkAttribute";

    public static PeClr Parse(PeImage image)
    {
        if (image.Directory(PeDirectory.ClrHeader) is not { IsPresent: true } dir) return PeClr.NotManaged;
        if (image.RvaToFileOffset(dir.VirtualAddress) is not { } start) return PeClr.NotManaged;

        var buf = image.Buffer;
        if (!buf.InRange(start, 72))
        {
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Clr", "The CLR header is truncated.", start));
            return PeClr.NotManaged;
        }

        buf.TryU16(start + 4,  out ushort major);
        buf.TryU16(start + 6,  out ushort minor);
        buf.TryU32(start + 8,  out uint   metadataRva);
        buf.TryU32(start + 12, out uint   metadataSize);
        buf.TryU32(start + 16, out uint   flags);
        buf.TryU32(start + 20, out uint   entryPointToken);

        var clr = new PeClr
        {
            IsManaged       = true,
            RuntimeVersion  = $"{major}.{minor}",
            Flags           = (PeClrFlags)flags,
            EntryPointToken = entryPointToken,
            MetadataRva     = metadataRva,
            MetadataSize    = metadataSize,
        };

        return ReadMetadata(image, clr);
    }

    private static PeClr ReadMetadata(PeImage image, PeClr clr)
    {
        try
        {
            using var stream = OpenForMetadata(image);
            if (stream is null) return clr;

            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return clr;

            var reader = peReader.GetMetadataReader();

            string? name = null, version = null, culture = null, publicKey = null;
            bool    winmd = false;

            if (reader.IsAssembly)
            {
                var assembly = reader.GetAssemblyDefinition();
                name    = reader.GetString(assembly.Name);
                version = assembly.Version.ToString();
                culture = assembly.Culture.IsNil ? null : reader.GetString(assembly.Culture);
                publicKey = FormatPublicKeyToken(reader, assembly.PublicKey);
                winmd   = (assembly.Flags & System.Reflection.AssemblyFlags.WindowsRuntime) != 0;
            }

            return clr with
            {
                MetadataVersion    = reader.MetadataVersion,
                AssemblyName       = name,
                AssemblyVersion    = version,
                AssemblyCulture    = string.IsNullOrEmpty(culture) ? null : culture,
                PublicKeyToken     = publicKey,
                IsWindowsRuntime   = winmd,
                AssemblyReferences = ReadReferences(reader),
                TargetFramework    = ReadTargetFramework(reader),
            };
        }
        catch (Exception e) when (e is BadImageFormatException or IOException or InvalidOperationException
                                       or UnauthorizedAccessException)
        {
            image.Add(new PeDiagnostic(PeSeverity.Info, "Clr",
                $"The image is managed but its metadata could not be read: {e.Message}"));
            return clr;
        }
    }

    /// <summary>
    /// A stream for the metadata reader. Reopening the file is cheaper than copying a mapped image
    /// into memory; only stream/in-memory input has to be materialised.
    /// </summary>
    private static Stream? OpenForMetadata(PeImage image)
    {
        if (image.Path is { Length: > 0 } path && File.Exists(path))
            return new FileStream(path, FileMode.Open, FileAccess.Read,
                                  FileShare.ReadWrite | FileShare.Delete, 1 << 16);

        if (image.Length is <= 0 or > int.MaxValue) return null;
        return new MemoryStream(image.ReadAt(0, image.Length), writable: false);
    }

    private static IReadOnlyList<PeAssemblyReference> ReadReferences(MetadataReader reader)
    {
        var result = new List<PeAssemblyReference>(reader.AssemblyReferences.Count);
        foreach (var handle in reader.AssemblyReferences)
        {
            try
            {
                var reference = reader.GetAssemblyReference(handle);
                string culture = reference.Culture.IsNil ? "" : reader.GetString(reference.Culture);
                result.Add(new PeAssemblyReference(
                    reader.GetString(reference.Name),
                    reference.Version.ToString(),
                    string.IsNullOrEmpty(culture) ? null : culture,
                    FormatPublicKeyToken(reader, reference.PublicKeyOrToken)));
            }
            catch (BadImageFormatException) { /* skip the bad row, keep the rest */ }
        }
        return result;
    }

    /// <summary>
    /// The <c>TargetFrameworkAttribute</c> value, e.g. ".NETCoreApp,Version=v10.0". It is an
    /// assembly-level custom attribute whose single fixed argument is the moniker; the blob is
    /// decoded by hand because a full <c>CustomAttributeTypeProvider</c> would be far more machinery
    /// than one string is worth.
    /// </summary>
    private static string? ReadTargetFramework(MetadataReader reader)
    {
        if (!reader.IsAssembly) return null;

        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            try
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (NameOf(reader, attribute) != TargetFrameworkAttribute) continue;

                var blob = reader.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 0x0001) continue;   // prolog
                return blob.ReadSerializedString();
            }
            catch (BadImageFormatException) { /* try the next attribute */ }
        }
        return null;
    }

    private static string? NameOf(MetadataReader reader, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                return member.Parent.Kind == HandleKind.TypeReference
                    ? reader.GetString(reader.GetTypeReference((TypeReferenceHandle)member.Parent).Name)
                    : null;

            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                return reader.GetString(reader.GetTypeDefinition(method.GetDeclaringType()).Name);

            default:
                return null;
        }
    }

    /// <summary>
    /// The 8-byte public key token. An assembly definition stores the full public key, which has to
    /// be reduced to its token (the low 8 bytes of its SHA-1, reversed); a reference usually stores
    /// the token already.
    /// </summary>
    private static string? FormatPublicKeyToken(MetadataReader reader, BlobHandle handle)
    {
        if (handle.IsNil) return null;

        var bytes = reader.GetBlobBytes(handle);
        if (bytes.Length == 0) return null;
        if (bytes.Length == 8) return Convert.ToHexStringLower(bytes);

        var hash  = System.Security.Cryptography.SHA1.HashData(bytes);
        var token = hash.AsSpan(hash.Length - 8).ToArray();
        Array.Reverse(token);
        return Convert.ToHexStringLower(token);
    }
}
