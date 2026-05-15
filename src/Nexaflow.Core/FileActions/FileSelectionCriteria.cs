namespace Nexaflow.Core.FileActions;

public enum CriteriaType
{
    /// <summary>Glob pattern on the filename, e.g. "*.pdf".</summary>
    Extension,
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
