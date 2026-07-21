using System;
using System.IO;

namespace Nexaflow.Tests.Features;

/// <summary>
/// Assembly-wide guard: point every in-process Features test at a throwaway config root instead of the
/// developer's real <c>%APPDATA%\Smile\nexaflow</c>. A feature store that news itself up with its
/// parameterless ctor (e.g. <c>PostItStore</c>, <c>ArchiveSignatureService</c>) resolves its root from
/// <c>NEXAFLOW_CONFIG_DIR</c> exactly as the app does — so without this, a test that instantiates one
/// writes real user data (post-its showed up in the production app). This project can't reference Core,
/// so unlike <c>Tests.Core</c>'s equivalent it isolates via the env var, not <c>ConfigManager.Initialize</c>.
///
/// UI tests are unaffected: each launches its child app with its own per-test <c>NEXAFLOW_CONFIG_DIR</c>
/// on the process start info, which the child reads instead of this parent-process value.
/// </summary>
[TestClass]
public static class TestConfigIsolation
{
    private static string _dir = string.Empty;

    [AssemblyInitialize]
    public static void Init(TestContext _)
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexaflow-tests-features-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("NEXAFLOW_CONFIG_DIR", _dir);
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        Environment.SetEnvironmentVariable("NEXAFLOW_CONFIG_DIR", null);
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }
}
