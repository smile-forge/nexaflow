using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// Keeps the coverage story self-truthing: docs/testing.md deliberately carries no per-feature
/// coverage table (it rotted twice), so this guard is what stops a feature from silently shipping
/// with zero tests. It maps each feature assembly to its per-feature test folder by namespace
/// convention (Nexaflow.Tests.Features.&lt;Folder&gt;) and fails naming any feature with no test class.
/// </summary>
[TestClass]
[NoCoverage("architecture guard")]
public class CoverageGuardTests
{
    /// <summary>Feature short-name → test-folder name, where they differ from the 1:1 convention.</summary>
    private static readonly Dictionary<string, string> FolderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"]                    = "CodeIntel",     // folder name ≠ feature name (historical)
        ["Compressed.Modern"]       = "Compressed",    // the three codec backends are covered by
        ["Compressed.SecureZip"]    = "Compressed",    // the Compressed handler/codec test suite
        ["Compressed.SharpCompress"] = "Compressed",
        ["Network.Arp"]             = "Network",     // discovery plugins share the Network suite, the
        ["Network.Ssdp"]            = "Network",     // same way the codec backends share Compressed's
    };

    [TestMethod]
    public void Every_feature_assembly_has_at_least_one_test_class()
    {
        var covered = typeof(CoverageGuardTests).Assembly.GetTypes()
            .Where(t => t.IsDefined(typeof(TestClassAttribute), inherit: false))
            .Select(t => t.Namespace ?? string.Empty)
            .Where(ns => ns.StartsWith("Nexaflow.Tests.Features.", StringComparison.Ordinal))
            .Select(ns => ns["Nexaflow.Tests.Features.".Length..].Split('.')[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "Nexaflow.Features.*.dll"))
        {
            var shortName = Path.GetFileNameWithoutExtension(dll)["Nexaflow.Features.".Length..];
            if (shortName is "Common") continue;   // contracts — covered by Tests.Core (Unit/ClientTools etc.)

            var folder = FolderAliases.GetValueOrDefault(shortName, shortName);
            if (!covered.Contains(folder))
                missing.Add($"{shortName} (expected a [TestClass] under Nexaflow.Tests.Features.{folder})");
        }

        Assert.AreEqual(0, missing.Count,
            "Every feature needs at least one test class in its per-feature folder (or an alias entry " +
            $"above if the folder is deliberately shared). Missing: {string.Join("; ", missing)}");
    }
}
