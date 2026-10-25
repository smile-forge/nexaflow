using System.Windows;
using System.Windows.Controls;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The closed <c>DarkComboBox</c> shows what the open list shows. Its template draws the selection box itself, and
/// a presenter given only the selection-box item and template drops <c>DisplayMemberPath</c> — WPF carries that as
/// the combo's <c>ItemTemplateSelector</c> — so a record item read as <c>Choice { Value = …, Label = … }</c> once
/// chosen. An <c>ItemTemplate</c> must still win over it. An editable combo types into the template's own text box.
/// <para>Builds the combo off-screen on an STA thread; opens no window. Not parallel: BAML
/// loading shares WPF's process-wide schema cache.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("opt-editor-listsource")]
[CoversNode("theme-combobox")]
public class DarkComboBoxTests
{
    public sealed record Choice(string Value, string Label);

    private static readonly Choice[] Choices = [new("a", "Alpha"), new("b", "Beta")];

    [TestMethod]
    public void TheClosedBoxShowsTheDisplayMember() => UiThread.Run(() =>
    {
        var shown = ClosedText(combo => combo.DisplayMemberPath = nameof(Choice.Label));

        CollectionAssert.Contains(shown, "Beta");
        Assert.IsFalse(shown.Any(t => t.Contains(nameof(Choice))), $"the item's ToString() showed: {string.Join(" | ", shown)}");
    });

    [TestMethod]
    public void AnItemTemplateStillWinsOverTheDisplayMemberSelector() => UiThread.Run(() =>
    {
        var shown = ClosedText(combo => combo.ItemTemplate = ValueTemplate());

        CollectionAssert.Contains(shown, "b");
        CollectionAssert.DoesNotContain(shown, "Beta");
    });

    [TestMethod]
    public void TheClosedBoxHonoursTheItemStringFormat() => UiThread.Run(() =>
    {
        var shown = ClosedText(combo =>
        {
            combo.ItemsSource  = new[] { 1.5, 2.25 };
            combo.ItemStringFormat = "{0:0.00} MB";
        });

        CollectionAssert.Contains(shown, "2.25 MB");
    });

    [TestMethod]
    public void AnEditableComboTypesIntoItsOwnTextBox() => UiThread.Run(() =>
    {
        var combo = ShippedStyles.Laid(new ComboBox { Style = ShippedDarkComboBox(), IsEditable = true, ItemsSource = new[] { "Alpha", "Beta" }, Text = "Gamma" });

        var editor = combo.Template.FindName("PART_EditableTextBox", combo) as TextBox;
        var shown  = (ContentPresenter)combo.Template.FindName("selectionBox", combo);

        Assert.IsNotNull(editor, "an editable combo has no PART_EditableTextBox to type into");
        Assert.AreEqual(Visibility.Visible, editor.Visibility);
        Assert.AreEqual("Gamma", editor.Text);
        Assert.AreEqual(Visibility.Collapsed, shown.Visibility);
    });

    /// <summary>Every text the closed combo draws, with its second item chosen.</summary>
    private static List<string> ClosedText(Action<ComboBox> configure)
    {
        var combo = new ComboBox { Style = ShippedDarkComboBox(), ItemsSource = Choices };
        configure(combo);
        combo.SelectedIndex = 1;

        return ShippedStyles.Descendants(ShippedStyles.Laid(combo)).OfType<TextBlock>().Select(t => t.Text).ToList();
    }

    private static Style ShippedDarkComboBox() => (Style)ShippedStyles.Load("DarkComboBoxItem", "DarkComboBox")["DarkComboBox"];

    private static DataTemplate ValueTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Choice.Value)));
        var template = new DataTemplate { VisualTree = text };
        template.Seal();
        return template;
    }
}
