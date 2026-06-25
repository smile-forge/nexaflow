using Nexaflow.Features.Common;

namespace Nexaflow.Features.ProductManager.Theming;

/// <summary>
/// Contributes the four <c>Product.Status.*</c> brushes as theme fallbacks. FeatureManager discovers this
/// by reflection (like <see cref="IPageRegistration"/>) and ThemeManager merges the dictionary below the
/// active theme, so the defaults apply everywhere and any theme may retune a key by name.
/// </summary>
public sealed class ProductThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.ProductManager;component/Theming/ProductTheme.xaml"),
    ];
}
