namespace Nexaflow.Tests.Features;

/// <summary>
/// Assembly-wide guard for the viewer, editor and player suite: redirects the config root to a throwaway directory so a
/// feature store that news itself up with its parameterless ctor never writes into the developer's real
/// <c>%APPDATA%\Smile\nexaflow</c>. The work is in <see cref="TestConfigRoot"/> — only this hook has to
/// live here, because MSTest honours <c>[AssemblyInitialize]</c> solely in the assembly under test, which
/// is also why each suite needs its own differently-named copy.
/// </summary>
[TestClass]
public static class ViewersConfigIsolation
{
    [AssemblyInitialize]
    public static void Init(TestContext _) => TestConfigRoot.Redirect("viewers");

    [AssemblyCleanup]
    public static void Cleanup() => TestConfigRoot.Restore();
}
