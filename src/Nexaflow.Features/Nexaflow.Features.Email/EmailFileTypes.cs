using System;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.Email;

/// <summary>The email formats the viewer opens, in one place. Keep in sync with the <c>/email</c> block in
/// <c>default-filemap.json</c> and with <c>EmailArchiveHandler</c>.</summary>
public static class EmailFileTypes
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".eml", ".msg",
    };

    /// <summary>Extensions for the shell file-picker filter.</summary>
    public static readonly string[] PickerExtensions = [".eml", ".msg"];

    public static bool IsEmail(string path) => Extensions.Contains(Path.GetExtension(path));

    /// <summary>True for Outlook <c>.msg</c> (OLE/CFBF) — everything else routes to the MIME reader.</summary>
    public static bool IsMsg(string path) =>
        string.Equals(Path.GetExtension(path), ".msg", StringComparison.OrdinalIgnoreCase);
}
