using Avalonia.Markup.Xaml;
using Nexaflow.Localization;

namespace Nexaflow.AvaloniaShell.Shell;

/// <summary><c>{loc:Str Key}</c>: a string in the user's language, from the same packs the WPF app reads its own from.</summary>
public sealed class StrExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Str.Get(key);
}
