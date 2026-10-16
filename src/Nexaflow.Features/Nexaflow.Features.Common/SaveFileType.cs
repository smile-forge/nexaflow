namespace Nexaflow.Features.Common;

/// <summary>A kind of file a save can be written as: what it is called, and its extension with its dot —
/// "Markdown Package", ".mdz".</summary>
public sealed record SaveFileType(string Name, string Extension);
