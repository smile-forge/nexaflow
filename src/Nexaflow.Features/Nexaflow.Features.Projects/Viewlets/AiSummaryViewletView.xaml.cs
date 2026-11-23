using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Visuals.Common.Viewing;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.Projects.Viewlets;

[ViewFor(typeof(AiSummaryViewletViewModel))]
public partial class AiSummaryViewletView : UserControl
{
    private readonly AiSummaryViewletViewModel _vm;
    private bool _isEditing;

    public AiSummaryViewletView(AiSummaryViewletViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        vm.Controller.ModeChanged += OnModeChanged;
        SummaryText.Text = vm.SavedText;

        ApplyMode(vm.Controller.CurrentMode);
        Loaded += (_, _) => CheckOverflow();
    }

    // ── Mode change ───────────────────────────────────────────────────────

    private void OnModeChanged(ViewletDisplayMode mode) => ApplyMode(mode);

    private void ApplyMode(ViewletDisplayMode mode)
    {
        // Title is shown only in DoubleBar; in Large the ViewletHost banner carries the name
        TitleText.Visibility = mode == ViewletDisplayMode.DoubleBar
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Overflow indicator is only meaningful in DoubleBar
        if (mode != ViewletDisplayMode.DoubleBar)
            OverflowButton.Visibility = Visibility.Collapsed;
        else
            CheckOverflow();
    }

    // ── Overflow detection ────────────────────────────────────────────────

    private void ContentArea_SizeChanged(object sender, SizeChangedEventArgs e) => CheckOverflow();

    private void CheckOverflow()
    {
        if (_isEditing || _vm.Controller.CurrentMode != ViewletDisplayMode.DoubleBar)
        {
            OverflowButton.Visibility = Visibility.Collapsed;
            return;
        }

        // Measure the TextBlock with unconstrained height to get its natural height
        SummaryText.Measure(new Size(
            Math.Max(0, ContentArea.ActualWidth),
            double.PositiveInfinity));

        bool overflows = SummaryText.DesiredSize.Height > ContentArea.ActualHeight;
        OverflowButton.Visibility = overflows ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Edit / Save ───────────────────────────────────────────────────────

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        _isEditing = true;

        EditTextBox.Text = SummaryText.Text;
        EditTextBox.Visibility  = Visibility.Visible;
        SummaryText.Visibility  = Visibility.Collapsed;
        OverflowButton.Visibility = Visibility.Collapsed;

        EditButton.Visibility = Visibility.Collapsed;
        SaveButton.Visibility = Visibility.Visible;
        SaveButton.IsEnabled  = false;

        EditTextBox.Focus();
        EditTextBox.CaretIndex = EditTextBox.Text.Length;
    }

    private void EditTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SaveButton.IsEnabled = EditTextBox.Text != _vm.SavedText;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var text = EditTextBox.Text;
        _vm.Save(text);
        ExitEditMode(text);
    }

    private void ExitEditMode(string text)
    {
        _isEditing = false;

        SummaryText.Text = text;
        SummaryText.Visibility  = Visibility.Visible;
        EditTextBox.Visibility  = Visibility.Collapsed;

        SaveButton.Visibility = Visibility.Collapsed;
        EditButton.Visibility = Visibility.Visible;

        CheckOverflow();
    }

    // ── Overflow indicator click ──────────────────────────────────────────

    private void OverflowButton_Click(object sender, RoutedEventArgs e)
        => _vm.Controller.SetDisplayMode(ViewletDisplayMode.Large);
}
