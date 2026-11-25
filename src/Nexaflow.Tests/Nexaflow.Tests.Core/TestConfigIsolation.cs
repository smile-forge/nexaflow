using System;
using System.Collections.Generic;
using Nexaflow.Core;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Core;

/// <summary>
/// Assembly-wide guard: point every in-process test at a throwaway config root instead of the developer's
/// real <c>%APPDATA%\Smile\nexaflow</c>, by setting <c>NEXAFLOW_CONFIG_DIR</c> for the test process as the UI tests set it
/// for the launched app — so this process's folders (<see cref="AppDataFolders"/>) are the root's. Without this, the suite's
/// configuration (<see cref="TestRuntimes.Config"/>), its <c>FeatureCatalog</c> and the crash and safety logs read and
/// <b>write</b> the real config — and a headless catalog build there (no WPF Application) poisons the running app,
/// silently dropping every feature's theme contribution (missing <c>PostIt.*</c>). A test that reads what it registers or
/// saves makes a configuration of its own on top of this (<see cref="TestRuntimes.ConfigIn"/>).
/// It also stands English from the source tree behind <c>Str</c>, as the feature suites do, so a message
/// built with <c>Str.Format(key, …)</c> reads as its sentence with its arguments in it rather than the bare key.
/// </summary>
[TestClass]
public static class TestConfigIsolation
{
    [AssemblyInitialize]
    public static void Init(TestContext _)
    {
        Environment.SetEnvironmentVariable(AppDataFolders.IsolatedRootVariable, TestTemp.CreateSubdirectory("config-").FullName);
        Str.Source = new EnglishTable(EnglishStringTable.Load());
    }

    private sealed class EnglishTable(IReadOnlyDictionary<string, string> strings) : ILocalizedStringSource
    {
        public string? Find(string key) => strings.GetValueOrDefault(key);
    }
}
