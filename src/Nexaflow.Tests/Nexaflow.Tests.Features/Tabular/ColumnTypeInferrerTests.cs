using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Tabular;

[TestClass]
[CoversNode("tabular-coltype-detection")]
public class ColumnTypeInferrerTests
{
    [TestMethod]
    public void ClassifyCell_Empty_ReturnsString()
        => Assert.AreEqual(CsvDataType.String, ColumnTypeInferrer.ClassifyCell(""));

    [TestMethod]
    public void ClassifyCell_IntegerLiteral_ReturnsInteger()
        => Assert.AreEqual(CsvDataType.Integer, ColumnTypeInferrer.ClassifyCell("42"));

    [TestMethod]
    public void ClassifyCell_NegativeInteger_ReturnsInteger()
        => Assert.AreEqual(CsvDataType.Integer, ColumnTypeInferrer.ClassifyCell("-7"));

    [TestMethod]
    public void ClassifyCell_Decimal_ReturnsDecimal()
        => Assert.AreEqual(CsvDataType.Decimal, ColumnTypeInferrer.ClassifyCell("3.14"));

    [TestMethod]
    public void ClassifyCell_Currency_LeadingSymbol_ReturnsCurrency()
        => Assert.AreEqual(CsvDataType.Currency, ColumnTypeInferrer.ClassifyCell("$2.60"));

    [TestMethod]
    public void ClassifyCell_Boolean_TrueFalseYesNo_ReturnsBoolean()
    {
        Assert.AreEqual(CsvDataType.Boolean, ColumnTypeInferrer.ClassifyCell("true"));
        Assert.AreEqual(CsvDataType.Boolean, ColumnTypeInferrer.ClassifyCell("FALSE"));
        Assert.AreEqual(CsvDataType.Boolean, ColumnTypeInferrer.ClassifyCell("yes"));
        Assert.AreEqual(CsvDataType.Boolean, ColumnTypeInferrer.ClassifyCell("n"));
    }

    [TestMethod]
    public void ClassifyCell_IsoDate_ReturnsDate()
        => Assert.AreEqual(CsvDataType.Date, ColumnTypeInferrer.ClassifyCell("2025-01-15"));

    [TestMethod]
    public void ClassifyCell_IsoDateTime_ReturnsDateTime()
        => Assert.AreEqual(CsvDataType.DateTime, ColumnTypeInferrer.ClassifyCell("2025-01-15T12:34:56Z"));

    [TestMethod]
    public void ClassifyCell_EuropeanDateTime_ReturnsDateTime()
        => Assert.AreEqual(CsvDataType.DateTime, ColumnTypeInferrer.ClassifyCell("21.05.2004 15:32:18"));

    [TestMethod]
    public void ClassifyCell_EuropeanDate_ReturnsDate()
        => Assert.AreEqual(CsvDataType.Date, ColumnTypeInferrer.ClassifyCell("21.05.2004"));

    [TestMethod]
    public void ClassifyCell_String_NoMatch()
        => Assert.AreEqual(CsvDataType.String, ColumnTypeInferrer.ClassifyCell("hello world"));

    [TestMethod]
    public void AggregateColumn_AllIntegers_ReturnsInteger()
    {
        var t = ColumnTypeInferrer.AggregateColumn(new[] { "1", "2", "3" });
        Assert.AreEqual(CsvDataType.Integer, t);
    }

    [TestMethod]
    public void AggregateColumn_IntegerAndDecimal_PromotesToDecimal()
    {
        var t = ColumnTypeInferrer.AggregateColumn(new[] { "1", "2.5", "3" });
        Assert.AreEqual(CsvDataType.Decimal, t);
    }

    [TestMethod]
    public void AggregateColumn_IntegerAndString_FallsBackToString()
    {
        var t = ColumnTypeInferrer.AggregateColumn(new[] { "1", "abc", "3" });
        Assert.AreEqual(CsvDataType.String, t);
    }

    [TestMethod]
    public void AggregateColumn_IgnoresBlanks()
    {
        var t = ColumnTypeInferrer.AggregateColumn(new[] { "1", "", "  ", "3" });
        Assert.AreEqual(CsvDataType.Integer, t);
    }
}
