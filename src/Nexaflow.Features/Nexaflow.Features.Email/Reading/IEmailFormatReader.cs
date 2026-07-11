using System.IO;
using Nexaflow.Features.Email.Model;

namespace Nexaflow.Features.Email.Reading;

/// <summary>Parses one on-disk email format (<c>.eml</c> or <c>.msg</c>) into the shared
/// <see cref="EmailDocument"/>. Implementations own their parser dependency (MimeKit / MsgReader); callers
/// go through <see cref="EmailDocumentReader"/>, which picks the reader and caches the result.</summary>
internal interface IEmailFormatReader
{
    /// <summary>Reads the whole message from <paramref name="stream"/> (assumed seekable). Does not dispose it.</summary>
    EmailDocument Read(Stream stream, string fileName);
}
