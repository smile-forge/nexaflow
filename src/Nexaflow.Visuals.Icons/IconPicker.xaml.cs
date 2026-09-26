using System.Windows;
using System.Windows.Controls;
using Nexaflow.Icons;

namespace Nexaflow.Visuals.Icons;

/// <summary>
/// Search and pick an icon from every set <see cref="IconCatalog"/> holds. <see cref="SelectedIcon"/> binds two-way.
/// Set <see cref="AutomationPrefix"/> to the owner's prefix: the search box is <c>{prefix}_Search</c>, the set chips
/// <c>{prefix}_SetAll</c> / <c>_SetEmoji</c> / <c>_SetFluent</c> / <c>_SetFluentFilled</c>, and each icon
/// <c>{prefix}_Icon_{Set}_{Name}</c> (<c>IconPicker_Icon_FluentRegular_home</c>).
/// </summary>
public partial class IconPicker : UserControl
{
    private const double CellWidth = 40;

    private readonly IconPickerViewModel _vm = new();

    public IconPicker()
    {
        InitializeComponent();
        Root.DataContext = _vm;
        _vm.Picked += icon => SelectedIcon = icon;
    }

    public IconPickerViewModel ViewModel => _vm;

    public static readonly DependencyProperty SelectedIconProperty = DependencyProperty.Register(
        nameof(SelectedIcon), typeof(IconRef), typeof(IconPicker),
        new FrameworkPropertyMetadata(default(IconRef), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((IconPicker)d)._vm.Selected = (IconRef)e.NewValue));

    public IconRef SelectedIcon
    {
        get => (IconRef)GetValue(SelectedIconProperty);
        set => SetValue(SelectedIconProperty, value);
    }

    public static readonly DependencyProperty AutomationPrefixProperty = DependencyProperty.Register(
        nameof(AutomationPrefix), typeof(string), typeof(IconPicker),
        new PropertyMetadata("IconPicker", (d, e) => ((IconPicker)d)._vm.AutomationPrefix = (string)e.NewValue));

    public string AutomationPrefix
    {
        get => (string)GetValue(AutomationPrefixProperty);
        set => SetValue(AutomationPrefixProperty, value);
    }

    /// <summary>Puts the caret in the search box, for a host that opens straight into searching.</summary>
    public void FocusSearch() => Search.FocusInput();

    private void IconGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Leave room for the scroll bar so a full row never forces a horizontal scroll.
        var usable = e.NewSize.Width - SystemParameters.VerticalScrollBarWidth - 2;
        _vm.Columns = Math.Max(1, (int)(usable / CellWidth));
    }
}
