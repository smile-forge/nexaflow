using System;
using System.Collections.Generic;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Executable.Theming;

/// <summary>
/// Ships the entropy ramp tokens. Merged below the active theme, so a theme that wants a different
/// ramp just redefines the keys — Core never needs to know this feature exists.
/// </summary>
public sealed class ExecutableThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.Executable;component/Theming/ExecutableTheme.xaml"),
    ];
}
