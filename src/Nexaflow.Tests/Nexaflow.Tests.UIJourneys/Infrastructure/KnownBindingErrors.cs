using System.IO;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.UIJourneys.Infrastructure;

/// <summary>
/// The binding errors the app is known to report, which a journey lets pass: <c>known-binding-errors.txt</c>, one
/// signature per line as the app writes them, each with a comment saying why it stays. Anything else the app reports fails
/// the journey that made it. An entry fixed is a line deleted, so the list only shrinks.
/// </summary>
internal static class KnownBindingErrors
{
    /// <summary>The baseline, read from the source tree rather than the build, so an edit needs no rebuild.</summary>
    internal static readonly string BaselineFile = Path.Combine(
        TestSampleData.RepoRoot, "src", "Nexaflow.Tests", "Nexaflow.Tests.UIJourneys", "Infrastructure", "known-binding-errors.txt");

    private static readonly Lazy<HashSet<string>> Known = new(Read);

    /// <summary>Of <paramref name="reported"/>, those the baseline does not name.</summary>
    internal static IReadOnlyList<string> Unknown(IEnumerable<string> reported) =>
        reported.Where(signature => !Known.Value.Contains(signature)).ToList();

    private static HashSet<string> Read() =>
        File.Exists(BaselineFile)
            ? File.ReadAllLines(BaselineFile)
                  .Select(line => line.Trim())
                  .Where(line => line.Length > 0 && !line.StartsWith('#'))
                  .ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
}
