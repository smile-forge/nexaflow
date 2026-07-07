namespace Nexaflow.Features.WindowsFileSystem.FileActions;

public enum CriteriaType
{
    /// <summary>Glob pattern on the filename, e.g. "*.pdf".</summary>
    Extension,
    /// <summary>
    /// Like <see cref="Extension"/>, but a <em>secondary</em> capability rather than the file's primary
    /// identity: the experience is offered on the action strip (and can be chosen as an explicit default)
    /// but is hidden from the right-click menu and never participates in the automatic default-open
    /// decision. Lets e.g. <c>/archive</c> claim <c>*.docx</c> (a zip container) without hijacking the
    /// Windows "Open" verb or forcing the user to add an override.
    /// </summary>
    OptionalExtension,
    /// <summary>HKCR PerceivedType value, e.g. "Image", "Video". Case-insensitive.</summary>
    PerceivedType,
    /// <summary>MIME content type, e.g. "application/pdf" or "text/*".</summary>
    ContentType,
    /// <summary>File signature type name as returned by FileTypeChecker, e.g. "PDF", "ZIP".</summary>
    MagicNumber,
    /// <summary>Glob pattern on the full file path.</summary>
    PathPattern,
}

public sealed class FileSelectionCriteria
{
    public CriteriaType Type  { get; set; }
    public string       Value { get; set; } = string.Empty;
}
