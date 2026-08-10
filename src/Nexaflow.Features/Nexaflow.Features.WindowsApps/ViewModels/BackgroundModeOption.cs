using Nexaflow.Features.WindowsApps.Models;

namespace Nexaflow.Features.WindowsApps.ViewModels;

/// <summary>
/// One entry in the "Let this app run in background" dropdown: the stored
/// <see cref="BackgroundAppMode"/> plus the wording Windows uses for it.
/// </summary>
public sealed record BackgroundModeOption(BackgroundAppMode Mode, string Label, string Description)
{
    /// <summary>The three choices, in the order Windows lists them.</summary>
    public static IReadOnlyList<BackgroundModeOption> All { get; } =
    [
        new(BackgroundAppMode.PowerOptimized, "Power optimized (recommended)",
            "Windows decides when the app may run in the background, and pauses it under battery saver."),
        new(BackgroundAppMode.Always, "Always",
            "The app keeps working in the background even under battery saver — it will use more power."),
        new(BackgroundAppMode.Never, "Never",
            "The app never runs in the background, so it can't sync, notify or update until you open it."),
    ];

    public static BackgroundModeOption For(BackgroundAppMode mode) =>
        All.First(o => o.Mode == mode);
}
