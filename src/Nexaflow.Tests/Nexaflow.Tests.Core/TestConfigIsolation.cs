using System;
using System.IO;
using Nexaflow.Core;

namespace Nexaflow.Tests.Core;

/// <summary>
/// Assembly-wide guard: point every in-process test at a throwaway config root instead of the developer's
/// real <c>%APPDATA%\Smile\nexaflow</c>. Without this, a test that touches <see cref="ConfigManager"/> /
/// <c>FeatureCatalog</c> reads and <b>writes</b> the real config — and a headless catalog build there
/// (no WPF Application) poisons the running app, silently dropping every feature's theme contribution
/// (missing <c>PostIt.*</c>). UI tests already isolate via <c>NEXAFLOW_CONFIG_DIR</c>
/// on the launched process; this covers the in-process ones. Individual classes may still redirect to their
/// own temp dir on top of this (e.g. WorkspaceManagerTests) — they restore to this root, not %APPDATA%.
/// </summary>
[TestClass]
public static class TestConfigIsolation
{
    private static string _dir = string.Empty;

    [AssemblyInitialize]
    public static void Init(TestContext _)
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexaflow-tests-core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        ConfigManager.Instance.Initialize(_dir);
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }
}
