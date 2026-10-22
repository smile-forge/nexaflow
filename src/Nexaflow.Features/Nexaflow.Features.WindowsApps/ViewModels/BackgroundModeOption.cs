using Nexaflow.Features.WindowsApps.Models;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.WindowsApps.ViewModels;

/// <summary>
/// One entry in the "Let this app run in background" dropdown: the stored
/// <see cref="BackgroundAppMode"/> plus the wording Windows uses for it.
/// </summary>
public sealed record BackgroundModeOption(BackgroundAppMode Mode, string Label, string Description)
{
    /// <summary>
    /// The three choices, in the order Windows lists them. Built on each read, so the wording is looked up
    /// in the current language.
    /// </summary>
    public static IReadOnlyList<BackgroundModeOption> All =>
    [
        new(BackgroundAppMode.PowerOptimized, Str.Get("WindowsApps.Advanced.Background.PowerOptimized"),
            Str.Get("WindowsApps.Advanced.Background.PowerOptimizedDescription")),
        new(BackgroundAppMode.Always, Str.Get("WindowsApps.Advanced.Background.Always"),
            Str.Get("WindowsApps.Advanced.Background.AlwaysDescription")),
        new(BackgroundAppMode.Never, Str.Get("WindowsApps.Advanced.Background.Never"),
            Str.Get("WindowsApps.Advanced.Background.NeverDescription")),
    ];

    public static BackgroundModeOption For(BackgroundAppMode mode) =>
        All.First(o => o.Mode == mode);
}
