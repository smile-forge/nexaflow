namespace Nexaflow.Providers.Common;

/// <summary>A file attachment to include with an LLM request.</summary>
/// <param name="FilePath">Absolute path to the file on disk.</param>
/// <param name="MimeType">Optional MIME type hint (e.g. "image/png"). Providers may use this
/// to decide how to encode or describe the file.</param>
public record LlmAttachment(string FilePath, string? MimeType = null);
