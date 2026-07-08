using System;
using System.IO;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>Repo-root resolution for source-tree-level tests — walks up from the test binary to the
/// folder holding <c>Nexaflow.slnx</c> (same convention as <c>TestSampleData</c>).</summary>
internal static class RepoRoot
{
    public static string Locate()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Nexaflow.slnx")))
                return dir.FullName;
        throw new InvalidOperationException(
            $"Could not locate the repo root (no Nexaflow.slnx above '{AppContext.BaseDirectory}').");
    }
}
