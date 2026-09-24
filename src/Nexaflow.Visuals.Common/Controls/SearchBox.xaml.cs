using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// The filter box a list wears: a magnifier, a watermark that shows while it is empty, and a clear button that
/// shows while it is not. Esc clears it too. <see cref="Text"/> binds two-way and updates as the user types.
/// <para>
/// Set <see cref="AutomationPrefix"/> to the owner's prefix; the input is <c>{prefix}_Search</c> and the clear
/// button <c>{prefix}_SearchClear</c>.
/// </para>
/// </summary>
public partial class SearchBox : UserControl
{
    public SearchBox()
    {
        InitializeComponent();
        Placeholder = Str.Get("Common.Search.Placeholder");
        UpdateChrome();
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SearchBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((SearchBox)d).UpdateChrome()));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(SearchBox), new PropertyMetadata(string.Empty));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly DependencyProperty AutomationPrefixProperty = DependencyProperty.Register(
        nameof(AutomationPrefix), typeof(string), typeof(SearchBox),
        new PropertyMetadata(string.Empty, OnAutomationPrefixChanged));

    public string AutomationPrefix
    {
        get => (string)GetValue(AutomationPrefixProperty);
        set => SetValue(AutomationPrefixProperty, value);
    }

    private static readonly DependencyPropertyKey InputAutomationIdKey = DependencyProperty.RegisterReadOnly(
        nameof(InputAutomationId), typeof(string), typeof(SearchBox), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty InputAutomationIdProperty = InputAutomationIdKey.DependencyProperty;

    public string InputAutomationId => (string)GetValue(InputAutomationIdProperty);

    private static readonly DependencyPropertyKey ClearAutomationIdKey = DependencyProperty.RegisterReadOnly(
        nameof(ClearAutomationId), typeof(string), typeof(SearchBox), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ClearAutomationIdProperty = ClearAutomationIdKey.DependencyProperty;

    public string ClearAutomationId => (string)GetValue(ClearAutomationIdProperty);

    private static void OnAutomationPrefixChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box    = (SearchBox)d;
        var prefix = (string?)e.NewValue ?? string.Empty;
        box.SetValue(InputAutomationIdKey, $"{prefix}_Search");
        box.SetValue(ClearAutomationIdKey, $"{prefix}_SearchClear");
    }

    /// <summary>Puts the caret in the box, for a host that opens straight into searching.</summary>
    public void FocusInput() => Input.Focus();

    private void UpdateChrome()
    {
        var empty = string.IsNullOrEmpty(Text);
        Watermark.Visibility   = empty ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Text = string.Empty;
        Input.Focus();
    }

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || string.IsNullOrEmpty(Text)) return;
        Text      = string.Empty;
        e.Handled = true;
    }

    private void Input_FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
        => Frame.SetResourceReference(Border.BorderBrushProperty, Input.IsKeyboardFocused ? "AccentBrush" : "BorderBrush");
}
