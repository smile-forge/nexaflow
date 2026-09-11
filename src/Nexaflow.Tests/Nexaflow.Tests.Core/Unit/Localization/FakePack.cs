using System.IO;
using System.Text;
using Nexaflow.Core.Localization;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>An in-memory language pack: logical name → content.</summary>
internal sealed class FakePack : ILanguagePack
{
    private readonly Dictionary<string, byte[]> _files;

    /// <summary>A pack of text files, stored as UTF-8.</summary>
    public FakePack(string code, params (string Name, string Content)[] files)
        : this(code, files.ToDictionary(f => f.Name, f => Encoding.UTF8.GetBytes(f.Content), StringComparer.Ordinal)) { }

    public FakePack(string code, Dictionary<string, byte[]> files)
    {
        Code   = code;
        _files = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
    }

    public string Code { get; }

    public IReadOnlyCollection<string> ResourceNames => _files.Keys;

    public Stream? Open(string logicalName)
        => _files.TryGetValue(logicalName, out var bytes) ? new MemoryStream(bytes, writable: false) : null;

    /// <summary>Adds (or replaces) a binary file — a picture.</summary>
    public FakePack With(string logicalName, byte[] content)
    {
        _files[logicalName] = content;
        return this;
    }

    /// <summary>A pack holding one <c>&lt;Project&gt;/strings.json</c> per entry.</summary>
    public static FakePack WithStrings(string code, params (string Project, string Json)[] tables)
        => new(code, tables.Select(t => ($"{t.Project}/strings.json", t.Json)).ToArray());
}
