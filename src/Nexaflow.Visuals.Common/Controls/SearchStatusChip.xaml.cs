using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// The status chip an <c>ISearchable</c> page leaves behind after a <c>?</c> search: how many matches,
/// what was searched for, previous/next stepping and dismiss.
/// <para>
/// It binds by <b>convention</b> to whatever page ViewModel is its DataContext — <c>IsSearchActive</c>,
/// <c>SearchMatchCount</c>, <c>CurrentSearchTerm</c>, <c>HasSearchMatches</c>,
/// <c>FindPreviousMatchCommand</c>, <c>FindNextMatchCommand</c>, <c>ClearSearchCommand</c> — rather than
/// through an interface, because those names are already the shared shape of every implementor and a
/// WPF control cannot bind to an interface it does not reference. Adding a member to that shape is the
/// page's job; this control only reads it.
/// </para>
/// <para>
/// Set <see cref="PagePrefix"/> to the page's automation prefix ("Editor", "Log", …) and the chip's five
/// AutomationIds compose from it, so each page keeps ids of its own without a per-page copy of the markup.
/// </para>
/// </summary>
public partial class SearchStatusChip : UserControl
{
    public SearchStatusChip() => InitializeComponent();

    /// <summary>Automation prefix for this page — "Editor", "Markdown", "Email", "Log", "Tabular", "Json".
    /// Yields <c>{PagePrefix}_SearchStatus</c>, <c>_SearchMatchCount</c>, <c>_SearchPrevious</c>,
    /// <c>_SearchNext</c>, <c>_SearchClear</c>.</summary>
    public static readonly DependencyProperty PagePrefixProperty = DependencyProperty.Register(
        nameof(PagePrefix), typeof(string), typeof(SearchStatusChip),
        new PropertyMetadata(string.Empty, OnPagePrefixChanged));

    public string PagePrefix
    {
        get => (string)GetValue(PagePrefixProperty);
        set => SetValue(PagePrefixProperty, value);
    }

    /// <summary>Optional marker appended to the count — "+" when the page stopped scanning at a cap, so a
    /// floor is never shown as an exact total.</summary>
    public static readonly DependencyProperty SuffixTextProperty = DependencyProperty.Register(
        nameof(SuffixText), typeof(string), typeof(SearchStatusChip), new PropertyMetadata(string.Empty));

    public string SuffixText
    {
        get => (string)GetValue(SuffixTextProperty);
        set => SetValue(SuffixTextProperty, value);
    }

    // Composed ids, exposed as read-only DPs so the markup can bind them and a UI test can still find
    // each element by the exact id its page used before this control existed.
    private static readonly DependencyPropertyKey AutomationIdStatusKey =
        RegisterId(nameof(AutomationIdStatus));
    private static readonly DependencyPropertyKey AutomationIdMatchCountKey =
        RegisterId(nameof(AutomationIdMatchCount));
    private static readonly DependencyPropertyKey AutomationIdPreviousKey =
        RegisterId(nameof(AutomationIdPrevious));
    private static readonly DependencyPropertyKey AutomationIdNextKey =
        RegisterId(nameof(AutomationIdNext));
    private static readonly DependencyPropertyKey AutomationIdClearKey =
        RegisterId(nameof(AutomationIdClear));

    public static readonly DependencyProperty AutomationIdStatusProperty = AutomationIdStatusKey.DependencyProperty;
    public static readonly DependencyProperty AutomationIdMatchCountProperty = AutomationIdMatchCountKey.DependencyProperty;
    public static readonly DependencyProperty AutomationIdPreviousProperty = AutomationIdPreviousKey.DependencyProperty;
    public static readonly DependencyProperty AutomationIdNextProperty = AutomationIdNextKey.DependencyProperty;
    public static readonly DependencyProperty AutomationIdClearProperty = AutomationIdClearKey.DependencyProperty;

    public string AutomationIdStatus => (string)GetValue(AutomationIdStatusProperty);
    public string AutomationIdMatchCount => (string)GetValue(AutomationIdMatchCountProperty);
    public string AutomationIdPrevious => (string)GetValue(AutomationIdPreviousProperty);
    public string AutomationIdNext => (string)GetValue(AutomationIdNextProperty);
    public string AutomationIdClear => (string)GetValue(AutomationIdClearProperty);

    private static DependencyPropertyKey RegisterId(string name) =>
        DependencyProperty.RegisterReadOnly(name, typeof(string), typeof(SearchStatusChip),
                                            new PropertyMetadata(string.Empty));

    private static void OnPagePrefixChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chip = (SearchStatusChip)d;
        var prefix = e.NewValue as string ?? string.Empty;
        chip.SetValue(AutomationIdStatusKey, prefix + "_SearchStatus");
        chip.SetValue(AutomationIdMatchCountKey, prefix + "_SearchMatchCount");
        chip.SetValue(AutomationIdPreviousKey, prefix + "_SearchPrevious");
        chip.SetValue(AutomationIdNextKey, prefix + "_SearchNext");
        chip.SetValue(AutomationIdClearKey, prefix + "_SearchClear");
    }
}
