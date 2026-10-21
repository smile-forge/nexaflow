using System;
using System.Collections.Generic;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Features.Localization;

/// <summary>
/// English read straight from every project's <c>Localization/en/strings.json</c> (<see cref="EnglishStringTable"/>), so a
/// test that compares with <c>Str.Format(key, n)</c> sees the sentence and its numbers rather than the bare key. The suites
/// have no Core, so no <c>LanguageManager</c>; each sets this from its <c>[AssemblyInitialize]</c>.
/// </summary>
public sealed class EnglishStrings : ILocalizedStringSource
{
    private readonly IReadOnlyDictionary<string, string> _table = EnglishStringTable.Load();

    private EnglishStrings() { }

    public static void Use() => Str.Source = new EnglishStrings();

    public string? Find(string key) => _table.GetValueOrDefault(key);
}
