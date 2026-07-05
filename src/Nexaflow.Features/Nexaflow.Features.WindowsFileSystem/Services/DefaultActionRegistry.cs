using Nexaflow.Features.WindowsFileSystem.FileActions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Singleton holding the live <see cref="DefaultActionsConfig"/> and resolving the per-extension
/// double-click override for a file. Queried by <see cref="DefaultFileOpener"/> before its automatic
/// resolution; updated by the Default Actions Options control on Save.
/// </summary>
public sealed class DefaultActionRegistry
{
    public static DefaultActionRegistry Instance { get; } = new();
    private DefaultActionRegistry() { }

    private DefaultActionsConfig _config = new();

    public void Initialize(DefaultActionsConfig config) => _config = config;
    public void Update(DefaultActionsConfig config) => _config = config;

    /// <summary>
    /// Returns the override that applies to <paramref name="file"/>, matching the most specific
    /// (longest, e.g. <c>tar.gz</c> before <c>gz</c>) extension first, then a <c>*</c> catch-all. Null when
    /// no override applies.
    /// </summary>
    public DefaultActionOverride? Resolve(FileInfo file)
    {
        var overrides = _config.Overrides;
        if (overrides is null || overrides.Count == 0) return null;

        foreach (var cand in ExtensionCandidates(file.Name))
        {
            var bare = cand.TrimStart('.');
            var match = overrides.FirstOrDefault(o =>
                string.Equals(NormalizeExt(o.Extension), bare, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return overrides.FirstOrDefault(o => o.Extension is "*" or "*.*");
    }

    /// <summary>Compound-then-bare extension candidates, longest first: <c>foo.tar.gz</c> → <c>.tar.gz</c>, <c>.gz</c>.</summary>
    private static IEnumerable<string> ExtensionCandidates(string fileName)
    {
        var name = Path.GetFileName(fileName).ToLowerInvariant();
        for (int i = name.IndexOf('.'); i >= 0; i = name.IndexOf('.', i + 1))
            yield return name[i..];
    }

    /// <summary>Normalises a stored extension ("*.pdf" / ".pdf" / "PDF") to bare lower-case ("pdf").</summary>
    private static string NormalizeExt(string ext)
    {
        var e = ext.Trim();
        if (e.StartsWith("*.")) e = e[2..];
        return e.TrimStart('.').ToLowerInvariant();
    }
}
