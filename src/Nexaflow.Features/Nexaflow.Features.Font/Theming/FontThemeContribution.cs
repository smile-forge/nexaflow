using System;
using System.Collections.Generic;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Font.Theming;

/// <summary>
/// Ships the Font feature's theme resources — currently just the font-picker overlay's
/// <c>DataTemplate</c> — so the shell's overlay host can render <c>FontPickerViewModel</c> by type.
/// FeatureManager discovers this by reflection (like <see cref="IPageRegistration"/>) and ThemeManager
/// merges the dictionary below the active theme. Mirrors <c>AudioThemeContribution</c>.
/// </summary>
public sealed class FontThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.Font;component/Theming/FontTheme.xaml"),
    ];
}
