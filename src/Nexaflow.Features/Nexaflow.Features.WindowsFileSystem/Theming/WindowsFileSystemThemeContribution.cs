using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;

namespace Nexaflow.Features.WindowsFileSystem.Theming;

/// <summary>
/// Contributes the file-system feature colour tokens (<c>ExternalApp.*</c>) as theme fallbacks.
/// Discovered by reflection like <see cref="IPageRegistration"/> and merged below the active
/// theme, so the defaults apply everywhere and any theme can retune a key by name.
/// </summary>
public sealed class WindowsFileSystemThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.WindowsFileSystem;component/Theming/WindowsFileSystemTheme.xaml"),
    ];
}
