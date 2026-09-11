namespace Nexaflow.Core.Localization;

/// <summary>An installed language, as the Options picker offers it: its culture code ("en", "pt-BR"), the
/// language's own name for itself ("English", "Français"), and its pack file (empty for an English with no pack).</summary>
public sealed record LanguageInfo(string Code, string DisplayName, string FilePath)
{
    // What a list shows for it, whatever template the list's style brings.
    public override string ToString() => DisplayName;
}
