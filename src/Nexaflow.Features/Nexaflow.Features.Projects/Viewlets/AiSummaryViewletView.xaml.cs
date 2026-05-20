using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Projects.Model;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.Projects.Viewlets;

public partial class AiSummaryViewletView : UserControl
{
    private readonly ProjectOperations  _ops;
    private readonly string             _folderPath;
    private readonly IViewletController _controller;
    private string _savedText = string.Empty;
    private bool   _isEditing;

    public AiSummaryViewletView(ProjectOperations ops, string folderPath, IViewletController controller)
    {
        InitializeComponent();
        _ops        = ops;
        _folderPath = folderPath;
        _controller = controller;

        _controller.ModeChanged += OnModeChanged;

        var text = ops.ReadDirectorySummary(folderPath) ?? string.Empty;
        _savedText       = text;
        SummaryText.Text = text;

        ApplyMode(controller.CurrentMode);
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
        if (_isEditing || _controller.CurrentMode != ViewletDisplayMode.DoubleBar)
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
        SaveButton.IsEnabled = EditTextBox.Text != _savedText;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var text = EditTextBox.Text;
        try
        {
            _ops.WriteDirectorySummary(_folderPath, text);
            _savedText = text;
        }
        catch { /* access denied — leave edit mode open so user sees the button is still clickable */ }

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
        => _controller.SetDisplayMode(ViewletDisplayMode.Large);
}
