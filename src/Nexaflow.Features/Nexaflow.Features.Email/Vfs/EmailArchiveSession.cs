using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Features.Email.Model;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Email.Vfs;

/// <summary>Presents a parsed email's parts as archive entries so the VFS can materialise an attachment to a
/// temp file and hand it to the right viewer. Every part (regular <i>and</i> inline) is an entry, named by
/// the same <see cref="EmailAttachment.EntryName"/> the view-model uses — so index <i>n</i> is the same part
/// on both sides and <c>HandleObject(emlPath + EntryName)</c> round-trips.</summary>
internal sealed class EmailArchiveSession : IArchiveSession
{
    private readonly EmailDocument _doc;
    private readonly Stream? _container;

    public EmailArchiveSession(EmailDocument doc, Stream? container)
    {
        _doc = doc;
        _container = container;
        Entries = doc.Attachments
            .Select(a => new VirtualEntry(a.EntryName, IsDirectory: false, a.Size, a.Size,
                                          doc.Date?.LocalDateTime ?? DateTime.Now))
            .ToList();
    }

    public IReadOnlyList<VirtualEntry> Entries { get; }

    public Stream OpenEntry(string entryPath)
    {
        var norm = entryPath.Replace('\\', '/').TrimStart('/');
        var att = _doc.Attachments.FirstOrDefault(
                      a => string.Equals(a.EntryName, norm, StringComparison.OrdinalIgnoreCase))
                  ?? throw new FileNotFoundException($"Email part not found: {entryPath}");
        return new MemoryStream(att.Content, writable: false);
    }

    public void Dispose() => _container?.Dispose();
}
