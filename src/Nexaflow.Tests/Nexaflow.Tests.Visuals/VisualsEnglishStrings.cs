using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Visuals;

/// <summary>
/// Assembly-wide hook for the visuals suite: stands English from the source tree behind <c>Str</c>, so a test that
/// compares with <c>Str.Format(key, n)</c> sees the sentence and its numbers rather than the bare key. MSTest honours
/// <c>[AssemblyInitialize]</c> only in the assembly under test, so the suite carries its own.
/// </summary>
[TestClass]
public static class VisualsEnglishStrings
{
    [AssemblyInitialize]
    public static void Init(TestContext _) => Str.Source = new Table(EnglishStringTable.Load());

    private sealed class Table(IReadOnlyDictionary<string, string> strings) : ILocalizedStringSource
    {
        public string? Find(string key) => strings.GetValueOrDefault(key);
    }
}
