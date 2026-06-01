namespace Nexaflow.Features.Common;

/// <summary>
/// Optional, reflection-discovered hook that lets a feature contribute theme resources —
/// region-token defaults and/or default <c>Scene.&lt;Region&gt;</c> templates — WITHOUT Core
/// referencing the feature (and without the feature referencing Core). A feature advertises one
/// of these exactly like an <see cref="IPageRegistration"/>; <c>FeatureManager</c> finds it by
/// reflection across the loaded <c>Nexaflow.Features.*.dll</c> assemblies.
///
/// The named dictionaries are merged at LOW precedence — below the active theme — so they act as
/// FALLBACKS: a feature that names its own region (e.g. wraps its root in
/// <c>ThemedRegion Region="Json"</c> and consumes <c>Json.*</c> tokens) gets a sane look in every
/// theme, while any theme may override a contributed key purely by string name (no assembly
/// dependency either way). If no theme overrides and the feature ships no contribution, the region
/// simply falls back to the generic <c>Surface.*</c>/<c>Text.*</c> tokens.
///
/// The coupling surface between features and themes is therefore only the set of string resource
/// keys (the token/region/scene contract) — features and the shell stay independently updatable.
/// </summary>
public interface IThemeContribution
{
    /// <summary>
    /// Pack URIs of the <c>ResourceDictionary</c> files this feature contributes, e.g.
    /// <c>pack://application:,,,/Nexaflow.Features.Foo;component/Theming/Foo.xaml</c>.
    /// Implementations must expose a public parameterless constructor.
    /// </summary>
    IReadOnlyList<Uri> ResourceDictionaryUris { get; }
}
