using System;
using System.Globalization;
using System.Windows.Data;

namespace Nexaflow.Core.Converters;

/// <summary>
/// How long a tab's label may be, and how to shorten one that isn't.
/// <para>
/// This is a <b>display</b> rule, deliberately applied by the tab strip rather than baked into
/// <c>Page.Title</c>. The title is read by more than the tab: quick-open lists it, pinning a tab to the ribbon
/// takes it as the button label, the session capture persists it, and the AI context summary names the page by
/// it. Truncating the model would corrupt all four, and would leave nothing to show on hover. So the Page
/// keeps the true name and only the strip shortens it.
/// </para>
/// <para>
/// Distinct from breadcrumb shortening, which weighs a whole path and can drop interior segments. A tab has
/// one short label and one question to answer: does it fit.
/// </para>
/// </summary>
public static class TabTitle
{
    /// <summary>The most characters a tab label may show, ellipsis included.</summary>
    public const int MaxLength = 15;

    /// <summary>
    /// <paramref name="title"/> shortened to <see cref="MaxLength"/>, ending in an ellipsis when anything was
    /// dropped. The result is never longer than the limit — the ellipsis replaces a character rather than
    /// being appended past it.
    /// </summary>
    public static string Shorten(string? title)
    {
        if (string.IsNullOrEmpty(title) || title.Length <= MaxLength) return title ?? string.Empty;
        return title[..(MaxLength - 1)].TrimEnd() + "…";
    }

    /// <summary>True when <see cref="Shorten"/> would drop something, so the full name is worth a tooltip.</summary>
    public static bool IsShortened(string? title) => (title?.Length ?? 0) > MaxLength;
}

/// <summary>Binds a tab label to <see cref="TabTitle.Shorten"/>.</summary>
public sealed class TabTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => TabTitle.Shorten(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException("Tab labels are display-only.");
}

/// <summary>
/// The full title when it had to be shortened, else null so WPF shows no tooltip at all — a tooltip that
/// merely repeats the visible label is noise.
/// </summary>
public sealed class TabTitleTooltipConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && TabTitle.IsShortened(s) ? s : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException("Tab tooltips are display-only.");
}
