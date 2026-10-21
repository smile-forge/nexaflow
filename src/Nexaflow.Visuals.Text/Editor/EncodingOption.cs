using System.Text;

namespace Nexaflow.Visuals.Text.Editor;

/// <summary>An entry in the editor's encoding selector. Drives both how the file is decoded on load and
/// how it is encoded on save (so the chosen entry carries BOM intent — e.g. "UTF-8 with BOM"). <see cref="Name"/>
/// is what it is known by, in every language; <see cref="Display"/> what the selector shows, where that is words
/// rather than the encoding's own name.</summary>
public sealed record EncodingOption(string Name, Encoding Encoding, string? Display = null)
{
    public override string ToString() => Display ?? Name;
}
