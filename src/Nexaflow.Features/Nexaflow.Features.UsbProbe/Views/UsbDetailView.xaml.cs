using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Features.UsbProbe.ViewModels;

namespace Nexaflow.Features.UsbProbe.Views;

/// <summary>
/// The detail pane: one location's facts, and its descriptor's fields beside its bytes. The host hands it the
/// <see cref="Detail"/> to show and keeps <see cref="SelectedTab"/>, so a reader of descriptors stays on descriptors as
/// the selection moves. Its one piece of logic links the field table to the hex view: the row under the pointer tells
/// the view-model which bytes to light.
/// </summary>
public partial class UsbDetailView : UserControl
{
    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(UsbDetailViewModel), typeof(UsbDetailView));

    public static readonly DependencyProperty SelectedTabProperty = DependencyProperty.Register(
        nameof(SelectedTab), typeof(int), typeof(UsbDetailView),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public UsbDetailView() => InitializeComponent();

    public UsbDetailViewModel? Detail
    {
        get => (UsbDetailViewModel?)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public int SelectedTab
    {
        get => (int)GetValue(SelectedTabProperty);
        set => SetValue(SelectedTabProperty, value);
    }

    void Field_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is ListViewItem { DataContext: UsbFieldRow row } && Detail is { } detail) detail.HoveredField = row;
    }

    void Field_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is ListViewItem { DataContext: UsbFieldRow row } && Detail is { } detail && ReferenceEquals(detail.HoveredField, row))
            detail.HoveredField = null;
    }

    void Fields_MouseLeave(object sender, MouseEventArgs e)
    {
        if (Detail is { } detail) detail.HoveredField = null;
    }
}
