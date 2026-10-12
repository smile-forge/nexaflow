using System;
using System.Collections.Generic;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Email.Theming;

/// <summary>
/// Contributes the email viewer's own colour tokens (<c>Email.*</c>) as theme fallbacks. FeatureManager discovers this by
/// reflection (like <see cref="IPageRegistration"/>) and ThemeManager merges the dictionary below the active theme, so the
/// defaults apply everywhere and any theme may retune an <c>Email.*</c> key by name.
/// </summary>
public sealed class EmailThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.Email;component/Theming/EmailTheme.xaml"),
    ];
}
