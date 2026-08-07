using System;
using System.Collections.Generic;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.WindowsFileSystem.Theming;

/// <summary>
/// Contributes the This PC row tokens (<c>Drive.*</c>) as theme fallbacks. FeatureManager discovers this
/// by reflection (like <see cref="IPageRegistration"/>) and ThemeManager merges the dictionary below the
/// active theme, so the defaults apply everywhere and any theme may retune a <c>Drive.*</c> key by name
/// — no Core⇄feature reference either way. Mirrors the scratchpad's <c>PostIt.*</c> contribution.
/// </summary>
public sealed class WindowsFileSystemThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.WindowsFileSystem;component/Theming/WindowsFileSystemTheme.xaml"),
    ];
}
