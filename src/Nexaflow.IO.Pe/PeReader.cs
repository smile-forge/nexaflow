using System.Security.Cryptography;
using Nexaflow.IO.Pe.Internal;

namespace Nexaflow.IO.Pe;

/// <summary>
/// Reads a Portable Executable into a <see cref="PeImage"/>.
/// <para>
/// <b>This never throws.</b> Packed, truncated and deliberately-malformed binaries are the normal
/// input to an inspector, not an exceptional one, so a failure at any stage records a
/// <see cref="PeDiagnostic"/> and the read continues with whatever else is parseable. Callers check
/// <see cref="PeImage.IsPe"/> and <see cref="PeImage.Diagnostics"/>; they do not catch.
/// </para>
/// <para>
/// The returned image keeps the file mapped so later resource and hex reads are cheap — dispose it.
/// </para>
/// </summary>
public static class PeReader
{
    public static PeImage Read(string path, PeReadOptions? options = null)
    {
        var diagnostics = new List<PeDiagnostic>();
        PeBuffer buffer;
        try
        {
            buffer = PeBuffer.FromFile(path);
        }
        catch (Exception e)
        {
            diagnostics.Add(new PeDiagnostic(PeSeverity.Error, "File", $"Could not open the image: {e.Message}"));
            return new PeImage(path, PeBuffer.FromMemory(ReadOnlyMemory<byte>.Empty), diagnostics);
        }
        return ReadCore(buffer, path, options ?? PeReadOptions.Default, diagnostics);
    }

    public static PeImage Read(Stream stream, string? displayPath = null, PeReadOptions? options = null)
    {
        var diagnostics = new List<PeDiagnostic>();
        PeBuffer buffer;
        try
        {
            buffer = PeBuffer.FromStream(stream);
        }
        catch (Exception e)
        {
            diagnostics.Add(new PeDiagnostic(PeSeverity.Error, "File", $"Could not read the stream: {e.Message}"));
            return new PeImage(displayPath, PeBuffer.FromMemory(ReadOnlyMemory<byte>.Empty), diagnostics);
        }
        return ReadCore(buffer, displayPath, options ?? PeReadOptions.Default, diagnostics);
    }

    public static PeImage Read(ReadOnlyMemory<byte> bytes, string? displayPath = null, PeReadOptions? options = null)
        => ReadCore(PeBuffer.FromMemory(bytes), displayPath,
                    options ?? PeReadOptions.Default, new List<PeDiagnostic>());

    private static PeImage ReadCore(PeBuffer buffer, string? path, PeReadOptions options,
                                    List<PeDiagnostic> diagnostics)
    {
        var image = new PeImage(path, buffer, diagnostics);

        // Each stage is independently guarded: a hostile resource tree must not cost us the imports
        // that already parsed cleanly.
        Stage(image, "Headers", () => HeaderParser.Parse(buffer, image, options));
        if (!image.IsPe) return image;

        if (options.IncludeImports)   Stage(image, "Imports",   () => ImportParser.Parse(image, options));
        if (options.IncludeExports)   Stage(image, "Exports",   () => image.Exports   = ExportParser.Parse(image, options));
        if (options.IncludeResources) Stage(image, "Resources", () => image.Resources = ResourceParser.Parse(image, options));

        // The debug directory has to precede anything that reports the build time: it is what says
        // whether the COFF timestamp is a real time or a reproducible-build content hash.
        Stage(image, "Debug",       () => image.Debug       = StructureParser.ParseDebug(image, options));
        Stage(image, "Relocations", () => image.Relocations = StructureParser.ParseRelocations(image, options));
        Stage(image, "Tls",         () => image.Tls         = StructureParser.ParseTls(image, options));
        Stage(image, "Clr",         () => image.Clr         = ClrParser.Parse(image));

        if (options.IncludeResources)
        {
            Stage(image, "VersionInfo", () => image.Version  = ReadVersionInfo(image));
            Stage(image, "Manifest",    () => image.Manifest = ReadManifest(image));
        }

        if (options.IncludeEntropy)    Stage(image, "Entropy", () => image.Entropy = EntropyCalculator.Compute(buffer, options.EntropyBuckets));
        if (options.IncludeFileHashes) Stage(image, "Hashes",  () => ComputeFileHashes(image, buffer));

        Stage(image, "Security", () => image.Security = SecurityParser.Parse(image, options));

        return image;
    }

    /// <summary>
    /// Runs one parse stage. A parser is expected to report its own problems as diagnostics; this is
    /// the backstop that keeps an unforeseen bug in one structure from losing every other structure.
    /// </summary>
    private static void Stage(PeImage image, string area, Action work)
    {
        try
        {
            work();
        }
        catch (Exception e)
        {
            image.Add(new PeDiagnostic(PeSeverity.Error, area,
                $"Parsing stopped unexpectedly: {e.GetType().Name}: {e.Message}"));
        }
    }

    private static PeVersionInfo ReadVersionInfo(PeImage image)
    {
        var leaf = image.Resources.LeavesOfType(PeResourceTypes.Version).FirstOrDefault();
        if (leaf is null) return PeVersionInfo.Empty;

        var bytes = image.ReadResource(leaf);
        return bytes.Length == 0 ? PeVersionInfo.Empty : VersionInfoParser.Parse(bytes);
    }

    /// <summary>
    /// The embedded RT_MANIFEST, or the <c>&lt;file&gt;.manifest</c> sidecar when there is no
    /// embedded one. Both are real: an external manifest still governs the process, and a binary
    /// that has neither is genuinely unmanifested.
    /// </summary>
    private static AppManifest ReadManifest(PeImage image)
    {
        var leaf = image.Resources.LeavesOfType(PeResourceTypes.Manifest).FirstOrDefault();
        if (leaf is not null)
        {
            var bytes = image.ReadResource(leaf);
            if (bytes.Length > 0) return AppManifest.Parse(DecodeXml(bytes));
        }

        if (image.Path is { Length: > 0 } path)
        {
            string sidecar = path + ".manifest";
            try
            {
                if (File.Exists(sidecar))
                    return AppManifest.Parse(File.ReadAllText(sidecar), isExternal: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                image.Add(new PeDiagnostic(PeSeverity.Info, "Manifest",
                    $"An external manifest exists but could not be read: {e.Message}"));
            }
        }
        return AppManifest.Empty;
    }

    /// <summary>Manifests are UTF-8, with or without a BOM, but a UTF-16 one is not unheard of.</summary>
    private static string DecodeXml(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static void ComputeFileHashes(PeImage image, PeBuffer buffer)
    {
        const int chunkSize = 1 << 20;

        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var md5    = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        for (long done = 0; done < buffer.Length; )
        {
            int chunk = (int)Math.Min(chunkSize, buffer.Length - done);
            var span  = buffer.Slice(done, chunk);
            if (span.IsEmpty) break;
            sha256.AppendData(span);
            md5.AppendData(span);
            done += chunk;
        }

        image.Sha256 = Convert.ToHexStringLower(sha256.GetHashAndReset());
        image.Md5    = Convert.ToHexStringLower(md5.GetHashAndReset());
    }
}
