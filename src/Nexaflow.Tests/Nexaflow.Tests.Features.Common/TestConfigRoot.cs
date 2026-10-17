using System;
using System.IO;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features;

/// <summary>
/// Points every in-process Features test at a throwaway config root instead of the developer's real
/// <c>%APPDATA%\Smile\nexaflow</c>. A feature store that news itself up with its parameterless ctor
/// (e.g. <c>PostItStore</c>, <c>ArchiveSignatureService</c>) resolves its root from
/// <c>NEXAFLOW_CONFIG_DIR</c> exactly as the app does — so without this, a test that instantiates one
/// writes real user data (post-its showed up in the production app). These suites can't reference Core,
/// so unlike <c>Tests.Core</c>'s equivalent this isolates via the env var, not <c>ConfigManager.Initialize</c>.
/// <para>
/// The logic lives here but the <c>[AssemblyInitialize]</c> that calls it cannot: MSTest only honours one
/// declared in the assembly under test, so each suite keeps a small <c>TestConfigIsolation</c> shim. UI
/// tests are unaffected either way — each launches its child app with its own per-test
/// <c>NEXAFLOW_CONFIG_DIR</c> on the process start info, which the child reads instead of this value.
/// </para>
/// </summary>
public static class TestConfigRoot
{
    /// <summary>Points the config root at a fresh folder in this host's <see cref="TestTemp"/>, which goes with the host.</summary>
    public static void Redirect()
        => Environment.SetEnvironmentVariable("NEXAFLOW_CONFIG_DIR", TestTemp.CreateSubdirectory("config-").FullName);

    public static void Restore() => Environment.SetEnvironmentVariable("NEXAFLOW_CONFIG_DIR", null);
}
