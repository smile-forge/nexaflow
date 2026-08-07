using CommunityToolkit.Mvvm.ComponentModel;

namespace Nexaflow.Features.SystemInfo.Models;

/// <summary>Advisory health of a single fact — drives the value's colour in the dashboard.</summary>
public enum SystemInfoStatus
{
    /// <summary>No judgement (plain fact). Rendered in the normal text colour.</summary>
    Neutral,
    /// <summary>A protective feature is on / a value is healthy (green).</summary>
    Good,
    /// <summary>Worth attention — a protection is off or unsupported (amber).</summary>
    Warning,
    /// <summary>A genuinely bad / failing state (red).</summary>
    Bad,
}

/// <summary>
/// One label → value fact within a <see cref="SystemInfoSection"/>. Observable rather than a plain record
/// only so a "?" page search can mark it in place: the dashboard has no list to filter and no selection to
/// move, so marking the card row IS how a result is shown.
/// </summary>
public sealed partial class SystemInfoItem(
    string label, string value, SystemInfoStatus status = SystemInfoStatus.Neutral) : ObservableObject
{
    public string Label { get; } = label;
    public string Value { get; } = value;
    public SystemInfoStatus Status { get; } = status;

    [ObservableProperty] private bool _isSearchHit;
}

/// <summary>A titled group of related facts (e.g. "Operating System"), shown as one dashboard card.</summary>
public sealed class SystemInfoSection(string title, string icon, double width = 380)
{
    public string Title { get; } = title;
    public string Icon  { get; } = icon;

    /// <summary>Preferred card width in the dashboard. Wider for content-heavy sections (e.g. Storage).</summary>
    public double Width { get; } = width;

    public List<SystemInfoItem> Items { get; } = [];

    public SystemInfoSection Add(string label, string? value, SystemInfoStatus status = SystemInfoStatus.Neutral)
    {
        Items.Add(new SystemInfoItem(label, string.IsNullOrWhiteSpace(value) ? "—" : value!, status));
        return this;
    }
}

/// <summary>The full point-in-time device summary produced by <c>SystemInfoCollector</c>.</summary>
public sealed class SystemInfoSnapshot
{
    public List<SystemInfoSection> Sections { get; } = [];

    /// <summary>Flat plain-text rendering of every section — used for the LLM context and copy-to-clipboard.</summary>
    public string ToPlainText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var section in Sections)
        {
            sb.Append(section.Title).AppendLine(":");
            foreach (var item in section.Items)
                sb.Append("  ").Append(item.Label).Append(": ").AppendLine(item.Value);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
