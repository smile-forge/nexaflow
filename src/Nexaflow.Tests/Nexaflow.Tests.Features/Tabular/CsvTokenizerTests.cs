using Nexaflow.Features.Tabular.Detection;

namespace Nexaflow.Tests.Features.Tabular;

[TestClass]
public class CsvTokenizerTests
{
    [TestMethod]
    public void Split_Unquoted_SimpleCsv()
    {
        var fields = CsvTokenizer.Split("a,b,c", ',', null);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, fields);
    }

    [TestMethod]
    public void Split_Quoted_AllowsSeparatorInsideField()
    {
        var fields = CsvTokenizer.Split("\"hello, world\",x,y", ',', '"');
        CollectionAssert.AreEqual(new[] { "hello, world", "x", "y" }, fields);
    }

    [TestMethod]
    public void Split_Quoted_EscapedQuoteByDoubling()
    {
        var fields = CsvTokenizer.Split("\"he said \"\"hi\"\"\",2", ',', '"');
        CollectionAssert.AreEqual(new[] { "he said \"hi\"", "2" }, fields);
    }

    [TestMethod]
    public void Split_Tab_HonorsSeparator()
    {
        var fields = CsvTokenizer.Split("a\tb\tc", '\t', null);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, fields);
    }

    [TestMethod]
    public void Split_EmptyTrailing_PreservesEmptyField()
    {
        var fields = CsvTokenizer.Split("a,b,", ',', null);
        CollectionAssert.AreEqual(new[] { "a", "b", "" }, fields);
    }

    [TestMethod]
    public void Split_QuoteOnlyAtFieldStart_TreatedAsLiteralOtherwise()
    {
        // A quote in the middle of an unquoted field should not start a quoted span.
        var fields = CsvTokenizer.Split("a\"b,c", ',', '"');
        CollectionAssert.AreEqual(new[] { "a\"b", "c" }, fields);
    }
}
