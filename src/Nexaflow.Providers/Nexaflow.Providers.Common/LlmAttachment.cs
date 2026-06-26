using System;
using System.IO;

namespace Nexaflow.Providers.Common;

/// <summary>A file attachment to include with an LLM request.</summary>
/// <param name="FilePath">Path to the file on disk, or a descriptive name when the content is supplied
/// in <paramref name="Bytes"/> rather than read from disk (e.g. a captured screenshot).</param>
/// <param name="MimeType">Optional MIME type hint (e.g. "image/png"). Providers use this to decide how
/// to encode the file — an image MIME drives the vision content path on providers that support it.</param>
/// <param name="Bytes">Optional in-memory content. When present, providers use it directly instead of
/// reading <paramref name="FilePath"/> — for ephemeral attachments (screenshots) that never hit disk.</param>
public record LlmAttachment(string FilePath, string? MimeType = null, byte[]? Bytes = null)
{
    /// <summary>True when this attachment is an image — by MIME type, or by file extension when no MIME
    /// is given. Providers that report <see cref="ILlmProvider.SupportsImages"/> encode these as vision input.</summary>
    public bool IsImage =>
        MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
        || (MimeType is null && Path.GetExtension(FilePath).ToLowerInvariant() is
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp");

    /// <summary>The image's MIME type, inferring from the file extension when <see cref="MimeType"/> is unset.</summary>
    public string ResolvedMimeType => MimeType ?? Path.GetExtension(FilePath).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif"            => "image/gif",
        ".webp"           => "image/webp",
        ".bmp"            => "image/bmp",
        _                 => "image/png",
    };

    /// <summary>Reads the attachment content — from <see cref="Bytes"/> if present, else from disk.</summary>
    public byte[] ReadBytes() => Bytes ?? File.ReadAllBytes(FilePath);
}
