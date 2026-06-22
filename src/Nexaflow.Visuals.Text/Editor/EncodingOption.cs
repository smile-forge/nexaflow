using System.Text;

namespace Nexaflow.Visuals.Text.Editor;

/// <summary>An entry in the editor's encoding selector. Drives both how the file is decoded on load and
/// how it is encoded on save (so the chosen entry carries BOM intent — e.g. "UTF-8 with BOM").</summary>
public sealed record EncodingOption(string Name, Encoding Encoding)
{
    public override string ToString() => Name;
}
