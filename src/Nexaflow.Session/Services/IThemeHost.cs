namespace Nexaflow.Session.Services;

/// <summary>
/// The theme the windows are drawn with, as the session sees it. Features contribute theme resources as their assemblies load;
/// the session collects them, and the toolkit folds each new set into the theme it has applied, on the UI thread.
/// </summary>
public interface IThemeHost
{
    /// <summary>Folds <paramref name="contributions"/> — every feature's theme resources so far — into the theme applied.</summary>
    void ApplyContributions(IReadOnlyList<Uri> contributions);
}
