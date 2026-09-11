using System.Globalization;

namespace Nexaflow.Visuals.Common.Localization;

/// <summary>
/// A UI string in the active language: <c>Str.Get("Help.Search.Placeholder")</c> in code,
/// <c>{loc:Str Help.Search.Placeholder}</c> in XAML (<see cref="StrExtension"/>).
/// <para>
/// Keys are <c>&lt;Area&gt;.&lt;Surface&gt;.&lt;Element&gt;</c>, Area being the owning project's short name, and each
/// project ships its English table as <c>Localization/en/strings.json</c> (docs/localization.md). A key the active
/// language lacks falls back to English; one nobody defines comes back as the key itself — visible, never a crash.
/// </para>
/// <para>
/// A string is resolved once, where it is used. There is no live re-binding, so a language switch restarts the
/// window the way a theme switch does, and the everyday single-language case pays for nothing but the lookup.
/// <see cref="Source"/> is set once by the shell at startup — the same shape as <see cref="Theming.TextTypography"/> —
/// so a feature can use this without referencing Core.
/// </para>
/// </summary>
public static class Str
{
    /// <summary>The table behind every lookup. Null (design time, a test that sets none) returns keys.</summary>
    public static ILocalizedStringSource? Source { get; set; }

    public static string Get(string key) => Source?.Find(key) ?? key;

    /// <summary><see cref="Get"/>, then formatted in the user's culture. A translation whose placeholders don't fit
    /// its arguments shows unformatted rather than throwing.</summary>
    public static string Format(string key, params object?[] args)
    {
        var text = Get(key);
        try { return string.Format(CultureInfo.CurrentCulture, text, args); }
        catch (System.FormatException) { return text; }
    }
}
