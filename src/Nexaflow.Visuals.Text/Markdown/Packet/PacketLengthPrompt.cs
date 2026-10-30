using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Visuals.Text.Markdown.Packet;

/// <summary>
/// Asks how many bits a field takes, where the lengths a right-click lists are not the one wanted: a small window over the one
/// the reader is in, holding the length the field takes now, which a whole number of one or more replaces.
///
/// <para>
/// The packet's own, asked by its edit handler once "Other length…" is chosen (<see cref="PacketEdits"/>) — nothing else wants a
/// number typed in after a choice, so nothing else carries the means to ask for one.
/// </para>
/// </summary>
internal sealed class PacketLengthPrompt : Window
{
    private readonly TextBox _bits;
    private readonly Button _set;

    internal PacketLengthPrompt(long now)
    {
        Title = Str.Get("Text.Packet.Length.Title");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        _bits = new TextBox { Text = now.ToString(CultureInfo.InvariantCulture), Width = 96, Margin = new Thickness(0, 6, 0, 10) };
        _bits.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        _bits.SetResourceReference(ForegroundProperty, "TextBrush");
        _bits.SetResourceReference(BorderBrushProperty, "BorderBrush");
        AutomationProperties.SetAutomationId(_bits, "Packet_Length_Bits");
        AutomationProperties.SetName(_bits, Str.Get("Text.Packet.Length.Asked"));

        // Only digits go in: a length is a whole number of bits.
        _bits.PreviewTextInput += (_, typed) => typed.Handled = !typed.Text.All(char.IsAsciiDigit);
        _bits.TextChanged += (_, _) => _set!.IsEnabled = Typed() is not null;

        _set = Named(new Button { Content = Str.Get("Text.Packet.Length.Set"), IsDefault = true, MinWidth = 72 }, "Packet_Length_Set");
        _set.Click += (_, _) =>
        {
            Length = Typed();
            Close();
        };

        var cancel = Named(new Button { Content = Str.Get("Text.Packet.Length.Cancel"), IsCancel = true, MinWidth = 72, Margin = new Thickness(6, 0, 0, 0) },
                           "Packet_Length_Cancel");
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                new TextBlock { Text = Str.Get("Text.Packet.Length.Asked") },
                _bits,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { _set, cancel } },
            },
        };

        Loaded += (_, _) =>
        {
            _bits.Focus();
            _bits.SelectAll();
        };
    }

    /// <summary>The length set, or null where the reader cancelled.</summary>
    internal long? Length { get; private set; }

    /// <summary>
    /// How many bits a field now taking <paramref name="now"/> is to take, asked of the reader over the window they are in — or
    /// null where they cancel.
    /// </summary>
    internal static long? Ask(long now)
    {
        var prompt = new PacketLengthPrompt(now) { Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive) };
        prompt.ShowDialog();

        return prompt.Length;
    }

    /// <summary>What is typed, as a length: a whole number of one or more — or null.</summary>
    private long? Typed() =>
        long.TryParse(_bits.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var bits) && bits > 0 ? bits : null;

    private static Button Named(Button button, string id)
    {
        button.SetResourceReference(ForegroundProperty, "TextBrush");
        AutomationProperties.SetAutomationId(button, id);
        return button;
    }
}
