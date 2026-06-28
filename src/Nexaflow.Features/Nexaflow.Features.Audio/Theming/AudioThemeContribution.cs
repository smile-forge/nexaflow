using System;
using System.Collections.Generic;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Audio.Theming;

/// <summary>
/// Contributes the audio player's own colour tokens (<c>Audio.*</c>) as theme fallbacks. FeatureManager
/// discovers this by reflection (like <see cref="IPageRegistration"/>) and ThemeManager merges the
/// dictionary below the active theme, so the defaults apply everywhere and any theme may retune a
/// <c>Audio.*</c> key by name — no Core⇄feature reference either way. Mirrors the scratchpad's
/// <c>PostIt.*</c> contribution.
/// </summary>
public sealed class AudioThemeContribution : IThemeContribution
{
    public IReadOnlyList<Uri> ResourceDictionaryUris =>
    [
        new("pack://application:,,,/Nexaflow.Features.Audio;component/Theming/AudioTheme.xaml"),
    ];
}
