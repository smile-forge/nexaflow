namespace Nexaflow.Core.Services;

/// <summary><see cref="IThemeHost"/> for WPF: the contributions are merged into the application's resources with the theme applied.</summary>
internal sealed class WpfThemeHost : IThemeHost
{
    public void ApplyContributions(IReadOnlyList<Uri> contributions) => ThemeManager.Apply(ThemeManager.Current, contributions);
}
