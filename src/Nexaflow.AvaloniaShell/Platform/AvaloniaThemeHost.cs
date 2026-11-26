using Microsoft.Extensions.Logging;

namespace Nexaflow.AvaloniaShell.Platform;

/// <summary>
/// The features' theme contributions on Avalonia. Each is a WPF resource dictionary (a pack URI into the feature's assembly, shipped
/// through <c>IThemeContribution</c>), which Avalonia cannot read, so none is applied: each is put on record, naming the gap, rather
/// than dropped unseen. An Avalonia feature view takes its colours from Fluent's resources.
/// </summary>
internal sealed class AvaloniaThemeHost(ILogger<AvaloniaThemeHost> log) : IThemeHost
{
    private readonly HashSet<Uri> _told = [];

    public void ApplyContributions(IReadOnlyList<Uri> contributions)
    {
        foreach (var contribution in contributions)
            if (_told.Add(contribution))
                log.LogInformation("theme contribution {Contribution} is a WPF resource dictionary; not applied on Avalonia", contribution);
    }
}
