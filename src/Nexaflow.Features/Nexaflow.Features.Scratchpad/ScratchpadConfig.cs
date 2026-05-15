using Nexaflow.Features.Common;

namespace Nexaflow.Features.Scratchpad;

public sealed class ScratchpadConfig : IFeatureConfig
{
    public string ConfigName   => "scratchpad";
    public string FriendlyName => "Scratchpad";

    [ConfigDisplayName("New Note Lifetime")]
    [ListSource(typeof(ScratchpadConfig), nameof(GetLifetimeOptions))]
    public string NoteLifetime { get; set; } = "2 hours";

    [ConfigDisplayName("Recycle Bin Retention")]
    [ListSource(typeof(ScratchpadConfig), nameof(GetRetentionOptions))]
    public string RecycleBinRetention { get; set; } = "30 days";

    public static IEnumerable<string> GetLifetimeOptions() =>
        ["30 minutes", "1 hour", "2 hours", "4 hours", "8 hours", "24 hours"];

    public static IEnumerable<string> GetRetentionOptions() =>
        ["None", "1 day", "15 days", "30 days", "60 days", "90 days", "Infinite"];

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
