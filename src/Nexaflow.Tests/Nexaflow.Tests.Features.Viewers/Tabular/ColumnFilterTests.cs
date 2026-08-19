using System;
using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Features.Tabular.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Tabular;

/// <summary>
/// The typed per-column filters behind the filter side panel. Each is a pure predicate over the column's
/// <i>raw</i> cell text (the grid AND-s one per selected column), so the rules that matter are: an
/// unconfigured filter matches everything (an empty editor must never hide rows), and a configured one
/// parses the cell the way its column type says — currency symbols stripped, dates parsed invariantly,
/// the usual truthy spellings recognised. A cell that won't parse is excluded rather than passed through.
/// </summary>
[TestClass]
public class ColumnFilterTests
{
    // ── Type → filter mapping ─────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("tabular-filter-string")]
    [CoversNode("tabular-filter-numeric")]
    [CoversNode("tabular-filter-date")]
    [CoversNode("tabular-filter-boolean")]
    public void ForType_PicksTheEditorThatMatchesTheColumnType()
    {
        Assert.IsInstanceOfType(ColumnFilter.ForType(CsvDataType.String), typeof(StringColumnFilter));
        Assert.IsInstanceOfType(ColumnFilter.ForType(CsvDataType.Integer), typeof(NumericColumnFilter));
        Assert.IsInstanceOfType(ColumnFilter.ForType(CsvDataType.Decimal), typeof(NumericColumnFilter));
        Assert.IsInstanceOfType(ColumnFilter.ForType(CsvDataType.Currency), typeof(NumericColumnFilter));
        Assert.IsInstanceOfType(ColumnFilter.ForType(CsvDataType.Date), typeof(DateColumnFilter));
        Assert.IsInstanceOfType(ColumnFilter.ForType(CsvDataType.Time), typeof(DateColumnFilter));
        Assert.IsInstanceOfType(ColumnFilter.ForType(CsvDataType.DateTime), typeof(DateColumnFilter));
        Assert.IsInstanceOfType(ColumnFilter.ForType(CsvDataType.Boolean), typeof(BooleanColumnFilter));
    }

    // ── Text filter ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("tabular-filter-string")]
    public void StringFilter_Empty_IsInactive_AndMatchesEverything()
    {
        var f = new StringColumnFilter();

        Assert.IsFalse(f.IsActive);
        Assert.IsTrue(f.Matches("anything"));
        Assert.IsTrue(f.Matches(string.Empty));
    }

    [TestMethod]
    [CoversNode("tabular-filter-string")]
    public void StringFilter_IsCaseInsensitiveSubstring()
    {
        var f = new StringColumnFilter { Text = "ell" };

        Assert.IsTrue(f.Matches("Hello"));
        Assert.IsTrue(f.Matches("BELLS"));
        Assert.IsFalse(f.Matches("world"));
    }

    [TestMethod]
    [CoversNode("tabular-filter-string")]
    public void StringFilter_RegexMode_MatchesThePattern_AndAHalfTypedOneHidesNothing()
    {
        var f = new StringColumnFilter { Text = "^a.*z$", UseRegex = true };
        Assert.IsTrue(f.Matches("abcz"));
        Assert.IsFalse(f.Matches("abc"));

        // Mid-typing the pattern is briefly invalid — that must not blank the grid.
        f.Text = "([";
        Assert.IsTrue(f.Matches("abc"), "an invalid regex matches everything rather than hiding every row");
    }

    // ── Numeric range filter ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("tabular-filter-numeric")]
    public void NumericFilter_NoBounds_IsInactive()
    {
        var f = new NumericColumnFilter();

        Assert.IsFalse(f.IsActive);
        Assert.IsTrue(f.Matches("not a number"));
    }

    [TestMethod]
    [CoversNode("tabular-filter-numeric")]
    public void NumericFilter_BoundsAreInclusive()
    {
        var f = new NumericColumnFilter { Min = 10, Max = 20 };

        Assert.IsTrue(f.Matches("10"));
        Assert.IsTrue(f.Matches("15.5"));
        Assert.IsTrue(f.Matches("20"));
        Assert.IsFalse(f.Matches("9.99"));
        Assert.IsFalse(f.Matches("20.01"));
    }

    [TestMethod]
    [CoversNode("tabular-filter-numeric")]
    public void NumericFilter_StripsCurrencySymbols_AndExcludesUnparseableCells()
    {
        var f = new NumericColumnFilter { Min = 1 };

        Assert.IsTrue(f.Matches("$42"));
        Assert.IsTrue(f.Matches("£1,000"));
        Assert.IsFalse(f.Matches("n/a"));
        Assert.IsFalse(f.Matches("   "), "a blank cell is not in any numeric range");
    }

    [TestMethod]
    [CoversNode("tabular-filter-numeric")]
    public void NumericFilter_OneSidedBound_Works()
    {
        Assert.IsTrue(new NumericColumnFilter { Min = 5 }.Matches("1000"));
        Assert.IsFalse(new NumericColumnFilter { Max = 5 }.Matches("1000"));
    }

    // ── Date range filter ─────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("tabular-filter-date")]
    public void DateFilter_NoBounds_IsInactive()
        => Assert.IsFalse(new DateColumnFilter().IsActive);

    [TestMethod]
    [CoversNode("tabular-filter-date")]
    public void DateFilter_KeepsDatesInsideTheRange_AndDropsUnparseableCells()
    {
        var f = new DateColumnFilter
        {
            From = new DateTime(2026, 01, 01),
            To   = new DateTime(2026, 12, 31),
        };

        Assert.IsTrue(f.Matches("2026-06-15"));
        Assert.IsFalse(f.Matches("2025-12-31"));
        Assert.IsFalse(f.Matches("2027-01-01"));
        Assert.IsFalse(f.Matches("not a date"));
    }

    [TestMethod]
    [CoversNode("tabular-filter-date")]
    public void DateFilter_ToBoundCarriesItsTimeOfDay()
    {
        // "To" with a time component is compared as an instant, so 23:00 on the last day is excluded
        // when the bound is midnight — the reason the panel offers HH:mm:ss boxes alongside the pickers.
        var midnight = new DateColumnFilter { To = new DateTime(2026, 06, 15) };
        Assert.IsFalse(midnight.Matches("2026-06-15 23:00"));

        var endOfDay = new DateColumnFilter { To = new DateTime(2026, 06, 15, 23, 59, 59) };
        Assert.IsTrue(endOfDay.Matches("2026-06-15 23:00"));
    }

    // ── Boolean filter ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("tabular-filter-boolean")]
    public void BooleanFilter_BothBoxesTicked_IsInactive()
    {
        var f = new BooleanColumnFilter();

        Assert.IsTrue(f is { ShowTrue: true, ShowFalse: true });
        Assert.IsFalse(f.IsActive);
        Assert.IsTrue(f.Matches("anything at all"));
    }

    [TestMethod]
    [CoversNode("tabular-filter-boolean")]
    public void BooleanFilter_RecognisesTheUsualSpellings()
    {
        var trueOnly = new BooleanColumnFilter { ShowFalse = false };

        foreach (var truthy in new[] { "true", "TRUE", "yes", "Y", "1", " true " })
            Assert.IsTrue(trueOnly.Matches(truthy), $"'{truthy}' should read as true");

        foreach (var falsy in new[] { "false", "no", "n", "0" })
            Assert.IsFalse(trueOnly.Matches(falsy), $"'{falsy}' should read as false");
    }

    [TestMethod]
    [CoversNode("tabular-filter-boolean")]
    public void BooleanFilter_UnrecognisedCell_IsFilteredOut()
        => Assert.IsFalse(new BooleanColumnFilter { ShowFalse = false }.Matches("maybe"));
}
