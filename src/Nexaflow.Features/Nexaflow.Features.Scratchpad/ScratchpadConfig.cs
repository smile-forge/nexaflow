using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Scratchpad;

public sealed class ScratchpadConfig : IFeatureConfig
{
    public string ConfigName   => "scratchpad";
    public string FriendlyName => Str.Get("Scratchpad.Config.Scratchpad");

    [ConfigDisplayName("Scratchpad.Config.Scratchpad.NoteLifetime")]
    [ListSource(typeof(ScratchpadConfig), nameof(GetLifetimeOptions))]
    public string NoteLifetime { get; set; } = "2 hours";

    [ConfigDisplayName("Scratchpad.Config.Scratchpad.RecycleBinRetention")]
    [ListSource(typeof(ScratchpadConfig), nameof(GetRetentionOptions))]
    public string RecycleBinRetention { get; set; } = "30 days";

    public static IEnumerable<ConfigListOption> GetLifetimeOptions() =>
    [
        new("30 minutes", Str.Format("Scratchpad.Config.Scratchpad.Minutes", 30)),
        new("1 hour",     Str.Get("Scratchpad.Config.Scratchpad.Hour")),
        new("2 hours",    Str.Format("Scratchpad.Config.Scratchpad.Hours", 2)),
        new("4 hours",    Str.Format("Scratchpad.Config.Scratchpad.Hours", 4)),
        new("8 hours",    Str.Format("Scratchpad.Config.Scratchpad.Hours", 8)),
        new("24 hours",   Str.Format("Scratchpad.Config.Scratchpad.Hours", 24)),
    ];

    public static IEnumerable<ConfigListOption> GetRetentionOptions() =>
    [
        new("None",     Str.Get("Scratchpad.Config.Scratchpad.None")),
        new("1 day",    Str.Get("Scratchpad.Config.Scratchpad.Day")),
        new("15 days",  Str.Format("Scratchpad.Config.Scratchpad.Days", 15)),
        new("30 days",  Str.Format("Scratchpad.Config.Scratchpad.Days", 30)),
        new("60 days",  Str.Format("Scratchpad.Config.Scratchpad.Days", 60)),
        new("90 days",  Str.Format("Scratchpad.Config.Scratchpad.Days", 90)),
        new("Infinite", Str.Get("Scratchpad.Config.Scratchpad.Infinite")),
    ];

    public TimeSpan GetNoteLifetime() => NoteLifetime switch
    {
        "30 minutes" => TimeSpan.FromMinutes(30),
        "1 hour"     => TimeSpan.FromHours(1),
        "2 hours"    => TimeSpan.FromHours(2),
        "4 hours"    => TimeSpan.FromHours(4),
        "8 hours"    => TimeSpan.FromHours(8),
        "24 hours"   => TimeSpan.FromHours(24),
        _            => TimeSpan.FromHours(2)
    };

    /// <summary>Returns the number of days to retain recycled notes, or null for infinite.</summary>
    public int? GetRetentionDays() => RecycleBinRetention switch
    {
        "None"     => 0,
        "1 day"    => 1,
        "15 days"  => 15,
        "30 days"  => 30,
        "60 days"  => 60,
        "90 days"  => 90,
        "Infinite" => null,
        _          => 30
    };
}
