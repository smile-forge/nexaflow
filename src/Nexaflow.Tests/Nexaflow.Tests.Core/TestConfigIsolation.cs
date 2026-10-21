using System;
using System.Collections.Generic;
using System.IO;
using Nexaflow.Core;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Core;

/// <summary>
/// Assembly-wide guard: point every in-process test at a throwaway config root instead of the developer's
/// real <c>%APPDATA%\Smile\nexaflow</c>. Without this, a test that touches <see cref="ConfigManager"/> /
/// <c>FeatureCatalog</c> reads and <b>writes</b> the real config — and a headless catalog build there
/// (no WPF Application) poisons the running app, silently dropping every feature's theme contribution
/// (missing <c>PostIt.*</c>). UI tests already isolate via <c>NEXAFLOW_CONFIG_DIR</c>
/// on the launched process; this covers the in-process ones. Individual classes may still redirect to their
/// own temp dir on top of this (e.g. WorkspaceManagerTests) — they restore to this root, not %APPDATA%.
/// It also stands English from the source tree behind <c>Str</c>, as the feature suites do, so a message
/// built with <c>Str.Format(key, …)</c> reads as its sentence with its arguments in it rather than the bare key.
/// </summary>
[TestClass]
public static class TestConfigIsolation
{
    [AssemblyInitialize]
    public static void Init(TestContext _)
    {
        ConfigManager.Instance.Initialize(TestTemp.CreateSubdirectory("config-").FullName);
        Str.Source = new EnglishTable(EnglishStringTable.Load());
    }

    private sealed class EnglishTable(IReadOnlyDictionary<string, string> strings) : ILocalizedStringSource
    {
        public string? Find(string key) => strings.GetValueOrDefault(key);
    }
}
