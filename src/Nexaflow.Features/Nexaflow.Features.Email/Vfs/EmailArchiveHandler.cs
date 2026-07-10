using System;
using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Email.Reading;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Email.Vfs;

/// <summary>
/// Registers <c>.eml</c>/<c>.msg</c> files as virtual containers so their attachments resolve as entries —
/// <c>HandleObject(mail.eml\report.pdf)</c> materialises the attachment to a temp file and opens it in the
/// right viewer, and a forwarded email or zip attachment nests for free. Discovered by reflection and
/// registered into <see cref="VirtualFileSystem.Instance"/> at startup (so it needs this public
/// parameterless ctor). Read-only: attachments are extracted, never written back.
/// <para>
/// The email itself does <b>not</b> expand as a folder by default — it opens the Email viewer. That policy
/// lives in the filemap (<c>.eml</c>/<c>.msg</c> are not in <c>/archive</c>), read by
/// <c>DefaultFileOpener.ExpandsInPlace</c>; this handler only makes the parts reachable.
/// </para>
/// </summary>
public sealed class EmailArchiveHandler : IArchiveHandler
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".eml", ".msg",
    };

    public string Name => "Email";

    public ArchiveCapabilities Capabilities => ArchiveCapabilities.List | ArchiveCapabilities.Extract;

    public bool CanHandle(string fileName) => Extensions.Contains(Path.GetExtension(fileName));

    public IArchiveSession Open(Stream container, string fileName, ArchiveOpenOptions? options = null)
    {
        // Read the whole message up front (the reader caches by file identity, so repeated VFS calls for the
        // same email reuse one parse). The session then serves entries from the in-memory parts; it owns the
        // container stream and disposes it, matching the IArchiveHandler contract.
        var doc = EmailDocumentReader.Instance.Read(container, fileName);
        return new EmailArchiveSession(doc, container);
    }
}
