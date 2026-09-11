using System.Windows.Markup;

namespace Nexaflow.Visuals.Common.Localization;

/// <summary>
/// <c>{loc:Str Help.Search.Placeholder}</c> — a UI string in the active language, resolved once as the XAML loads
/// (see <see cref="Str"/>). Declare
/// <c>xmlns:loc="clr-namespace:Nexaflow.Visuals.Common.Localization;assembly=Nexaflow.Visuals.Common"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class StrExtension : MarkupExtension
{
    public StrExtension() { }

    public StrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(System.IServiceProvider serviceProvider) => Str.Get(Key);
}
