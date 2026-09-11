using System.IO;
using System.Text;
using Nexaflow.Core.Localization;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>An in-memory language pack: logical name → UTF-8 content.</summary>
internal sealed class FakePack(string code, params (string Name, string Content)[] files) : ILanguagePack
{
    private readonly Dictionary<string, byte[]> _files =
        files.ToDictionary(f => f.Name, f => Encoding.UTF8.GetBytes(f.Content), StringComparer.Ordinal);

    public string Code { get; } = code;

    public IReadOnlyCollection<string> ResourceNames => _files.Keys;

    public Stream? Open(string logicalName)
        => _files.TryGetValue(logicalName, out var bytes) ? new MemoryStream(bytes, writable: false) : null;

    /// <summary>A pack holding one <c>&lt;Project&gt;/strings.json</c> per entry.</summary>
    public static FakePack WithStrings(string code, params (string Project, string Json)[] tables)
        => new(code, tables.Select(t => ($"{t.Project}/strings.json", t.Json)).ToArray());
}
