using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The shipped <c>DarkDatePicker</c> and the calendar it drops down. WPF builds most of what shows itself — the
/// picker makes its calendar and puts it in the popup, the calendar's page builds its own day and month cells —
/// so what is pinned here is that each of those finds its themed part and style, and that a state reaches for
/// the token it should.
/// <para>Builds the controls off-screen on an STA thread; opens no window. Not parallel: BAML
/// loading shares WPF's process-wide schema cache.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("theme-datepicker")]
public class DatePickerStyleTests
{
    private static readonly DateTime Chosen = new(2026, 10, 25);

    [TestMethod]
    public void AnEmptyPickerShowsItsWatermark() => UiThread.Run(() =>
    {
        var (_, box, watermark) = Picker(Styles());

        Assert.AreEqual(string.Empty, box.Text);
        Assert.AreEqual(Visibility.Visible, watermark.Visibility);
        Assert.IsFalse(string.IsNullOrEmpty(watermark.Content as string), "the watermark is up but says nothing");
    });

    [TestMethod]
    public void APickedDateIsTypedInTheBoxInPlaceOfTheWatermark() => UiThread.Run(() =>
    {
        var (picker, box, watermark) = Picker(Styles());

        picker.SelectedDate = Chosen;
        picker.UpdateLayout();

        Assert.IsFalse(string.IsNullOrEmpty(box.Text));
        Assert.AreEqual(picker.Text, box.Text);
        Assert.AreEqual(Visibility.Collapsed, watermark.Visibility);
    });

    [TestMethod]
    public void ThePickerDropsDownTheThemedCalendar() => UiThread.Run(() =>
    {
        var styles = Styles();
        var (picker, _, _) = Picker(styles);

        var popup = (Popup)picker.Template.FindName("PART_Popup", picker);

        Assert.IsInstanceOfType<Calendar>(popup.Child, "the picker found no PART_Popup to put its calendar in");
        Assert.AreSame(styles["DarkDatePickerCalendar"], ((Calendar)popup.Child).Style);
    });

    [TestMethod]
    public void EveryDayOfTheMonthIsAThemedCell() => UiThread.Run(() =>
    {
        var styles = Styles();
        var days   = ShippedStyles.Descendants(Calendar(styles)).OfType<CalendarDayButton>().ToList();

        Assert.AreEqual(42, days.Count, "the month grid is six weeks of seven days");
        Assert.IsTrue(days.All(d => d.Style == styles["DarkCalendarDayButton"]));
    });

    [TestMethod]
    public void TheChosenDayIsDrawnInTheAccent() => UiThread.Run(() =>
    {
        var styles = Styles();
        var chosen = ShippedStyles.Descendants(Calendar(styles)).OfType<CalendarDayButton>()
                                  .Single(d => d.DataContext is DateTime date && date == Chosen);
        var cell   = (Border)chosen.Template.FindName("cell", chosen);

        Assert.AreSame(styles["AccentBrush"], cell.Background);
        Assert.AreSame(styles["OnAccentBrush"], chosen.Foreground);
    });

    [TestMethod]
    public void TheMonthIsHeadedByTheWeekdays() => UiThread.Run(() =>
    {
        var styles = Styles();
        var page   = ShippedStyles.Descendants(Calendar(styles)).OfType<CalendarItem>().Single();
        var month  = (Grid)page.Template.FindName("PART_MonthView", page);

        var titles = month.Children.OfType<TextBlock>().Where(t => Grid.GetRow(t) == 0).ToList();

        Assert.AreEqual(7, titles.Count, "CalendarItem found no day-title template under its key");
        Assert.IsTrue(titles.All(t => t.Foreground == styles["TextMutedBrush"] && t.Text.Length > 0));
    });

    [TestMethod]
    public void TheYearViewTradesTheDaysForTheMonths() => UiThread.Run(() =>
    {
        var styles   = Styles();
        var calendar = Calendar(styles, c => c.DisplayMode = CalendarMode.Year);
        var page     = ShippedStyles.Descendants(calendar).OfType<CalendarItem>().Single();
        var months   = ShippedStyles.Descendants(calendar).OfType<CalendarButton>().ToList();

        Assert.AreEqual(Visibility.Hidden,  ((Grid)page.Template.FindName("PART_MonthView", page)).Visibility);
        Assert.AreEqual(Visibility.Visible, ((Grid)page.Template.FindName("PART_YearView", page)).Visibility);
        Assert.AreEqual(12, months.Count);
        Assert.IsTrue(months.All(m => m.Style == styles["DarkCalendarButton"]));
    });

    /// <summary>The picker's styles with everything they name, in the order Styles.xaml declares them.</summary>
    private static ResourceDictionary Styles() => ShippedStyles.Load(
        "DarkCalendarDayButton", "DarkCalendarButton", "DarkCalendarItem", "DarkCalendar", "DarkDatePickerCalendar", "DarkDatePicker");

    private static (DatePicker Picker, DatePickerTextBox Box, ContentControl Watermark) Picker(ResourceDictionary styles)
    {
        var picker = ShippedStyles.Laid(new DatePicker { Style = (Style)styles["DarkDatePicker"] });
        var box    = (DatePickerTextBox)picker.Template.FindName("PART_TextBox", picker);
        return (picker, box, (ContentControl)box.Template.FindName("PART_Watermark", box));
    }

    private static Calendar Calendar(ResourceDictionary styles, Action<Calendar>? configure = null)
    {
        var calendar = new Calendar { Style = (Style)styles["DarkCalendar"], DisplayDate = Chosen, SelectedDate = Chosen };
        configure?.Invoke(calendar);
        return ShippedStyles.Laid(calendar, 400, 400);
    }
}
