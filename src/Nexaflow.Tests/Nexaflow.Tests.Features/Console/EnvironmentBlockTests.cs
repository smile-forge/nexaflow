using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.IO.Terminal;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Console;

/// <summary>
/// Covers the Unicode environment-block builder used to apply per-environment variable overrides to a
/// child shell. A malformed block fails CreateProcess at runtime, so the shape is worth pinning down.
/// </summary>
[TestClass]
[CoversNode("io-terminal")]
public class EnvironmentBlockTests
{
    private static List<string> Entries(string block)
        => block.TrimEnd('\0').Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();

    [TestMethod]
    public void BuildEnvironmentBlock_IsTerminatedByDoubleNul()
    {
        var block = PseudoConsoleHostService.BuildEnvironmentBlock(
            new Dictionary<string, string> { ["NEXATEST_X"] = "1" });

        Assert.IsTrue(block.EndsWith("\0\0", StringComparison.Ordinal), "block must end with a double NUL");
    }

    [TestMethod]
    public void BuildEnvironmentBlock_IncludesTheOverride()
    {
        var block = PseudoConsoleHostService.BuildEnvironmentBlock(
            new Dictionary<string, string> { ["NEXATEST_X"] = "hello" });

        CollectionAssert.Contains(Entries(block), "NEXATEST_X=hello");
    }

    [TestMethod]
    public void BuildEnvironmentBlock_OverrideReplacesProcessVar_CaseInsensitive()
    {
        Environment.SetEnvironmentVariable("NEXATEST_OVR", "old");
        try
        {
            var block = PseudoConsoleHostService.BuildEnvironmentBlock(
                new Dictionary<string, string> { ["nexatest_ovr"] = "new" });

            var entries = Entries(block);
            Assert.AreEqual(1,
                entries.Count(e => e.StartsWith("NEXATEST_OVR=", StringComparison.OrdinalIgnoreCase)),
                "override must update the existing variable, not duplicate it");
            Assert.IsTrue(entries.Any(e => e.Equals("NEXATEST_OVR=new", StringComparison.OrdinalIgnoreCase)),
                "the override value should win");
        }
        finally { Environment.SetEnvironmentVariable("NEXATEST_OVR", null); }
    }

    [TestMethod]
    public void BuildEnvironmentBlock_EntriesAreSortedCaseInsensitive()
    {
        var block = PseudoConsoleHostService.BuildEnvironmentBlock(
            new Dictionary<string, string> { ["NEXAZZZ"] = "z", ["NEXAaaa"] = "a" });

        var names  = Entries(block).Select(e => e.Split('=')[0]).ToList();
        var sorted = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        CollectionAssert.AreEqual(sorted, names, "CreateProcess expects a sorted environment block");
    }
}
