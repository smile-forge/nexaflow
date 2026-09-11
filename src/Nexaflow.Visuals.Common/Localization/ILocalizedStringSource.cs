namespace Nexaflow.Visuals.Common.Localization;

/// <summary>Where <see cref="Str"/> looks a key up — in the app, the shell's language manager.</summary>
public interface ILocalizedStringSource
{
    /// <summary>The text for <paramref name="key"/> in the active language (English where that has none), or null
    /// when no pack defines it.</summary>
    string? Find(string key);
}
