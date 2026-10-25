using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using Nexaflow.IO.Common;
using UglyToad.PdfPig.Graphics.Colors.Icc;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// Windows' colour engine as the PDF reader's, PdfPig's ICC profile service: colours an ICC profile describes — an
/// ICCBased colour space, a JPEG 2000 image's own profile, the press profile device CMYK means (<see cref="PdfCmyk"/>)
/// — converted to sRGB through the profile with Image Color Management, under the rendering intent the page asks for.
/// One engine serves every document, so a profile many documents embed is read once, however many carry it.
/// <para>
/// A document's output intent is not drawn through: it says what the document was made for, and 14.11.5 leaves a
/// reader free to disregard it, as readers showing a page on a screen do.
/// </para>
/// </summary>
internal sealed class PdfColourEngine : IIccProfileService
{
    private const int ProfilesKept = 64;

    /// <summary>The engine every document is read with.</summary>
    public static PdfColourEngine Shared { get; } = new();

    private readonly Lazy<PdfIccTargets?> _targets = new(static () =>
        InstalledBytes("sRGB Color Space Profile.icm") is { } srgb ? new PdfIccTargets(srgb, PdfLabProfile.Identity) : null);
    private readonly ConcurrentDictionary<string, Lazy<PdfIccProfile?>> _profiles = new();

    private PdfColourEngine() { }

    public bool UseOutputIntent => false;

    public string? PreferredOutputIntentSubtype => null;

    public bool TryGetProfile(ReadOnlyMemory<byte> profileBytes, [NotNullWhen(true)] out IIccProfile? profile)
    {
        profile = Profile(profileBytes);
        return profile is not null;
    }

    /// <summary>
    /// The profile <paramref name="bytes"/> hold, or <see langword="null"/> where they hold none Windows can convert
    /// through, or Windows has no sRGB profile to convert to.
    /// </summary>
    public PdfIccProfile? Profile(ReadOnlyMemory<byte> bytes)
    {
        if (_targets.Value is not { } targets) return null;

        // Profiles are known by their bytes: two documents embedding the same one share it, and its transforms.
        var key = Convert.ToHexString(SHA256.HashData(bytes.Span));
        if (!_profiles.TryGetValue(key, out var read))
        {
            // Held to a few dozen; one let go here lives on in whatever colour space still converts through it.
            if (_profiles.Count >= ProfilesKept) _profiles.Clear();
            read = _profiles.GetOrAdd(key, _ => new Lazy<PdfIccProfile?>(() => PdfIccProfile.TryRead(bytes, targets)));
        }

        return read.Value;
    }

    /// <summary>A profile Windows installs in its colour directory, or <see langword="null"/> where it cannot be read.</summary>
    public PdfIccProfile? Installed(string name) => InstalledBytes(name) is { } bytes ? Profile(bytes) : null;

    [LocalDiskOnly("colour profiles installed with Windows")]
    private static byte[]? InstalledBytes(string name)
    {
        try
        {
            var path = Path.Combine(Environment.SystemDirectory, "spool", "drivers", "color", name);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
