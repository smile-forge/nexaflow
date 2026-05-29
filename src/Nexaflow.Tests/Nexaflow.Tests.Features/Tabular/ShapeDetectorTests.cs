using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexaflow.Features.Tabular.Detection;

namespace Nexaflow.Tests.Features.Tabular;

[TestClass]
public class ShapeDetectorTests
{
    private static FileSample MakeSample(IEnumerable<string> head, IEnumerable<string> tail) =>
        new(head.ToList(), tail.ToList(), LineEndingKind.Lf, Encoding.UTF8,
            HadBom: false, BomByteCount: 0, FileLength: 0, AverageLineByteSize: 64,
            LeadingCommentLines: System.Array.Empty<string>());

    [TestMethod]
    public void DetectAll_StandardCsvWithHeader_TopShapeIsCommaWithHeader()
    {
        var sample = MakeSample(
            head: new[] { "name,age,score", "alice,30,9.5", "bob,42,7.0", "carol,25,8.1" },
            tail: new[] { "dan,33,6.2", "eve,28,9.9", "frank,40,5.5", "gina,29,8.8" });

        var shapes = ShapeDetector.DetectAll(sample);
        var top    = shapes[0];

        Assert.AreEqual(',', top.Separator);
        Assert.AreEqual(3,  top.FieldCount);
        Assert.IsTrue(top.HasHeader, "Header row of all-string cells over numeric body should be detected.");
    }

    [TestMethod]
    public void DetectAll_TabSeparated_NoHeader_DetectsTab()
    {
        var sample = MakeSample(
            head: new[] { "alpha\t1\t2.5", "beta\t2\t3.6", "gamma\t3\t4.7", "delta\t4\t5.8" },
            tail: new[] { "epsilon\t5\t6.9", "zeta\t6\t7.0", "eta\t7\t8.1", "theta\t8\t9.2" });

        var shapes = ShapeDetector.DetectAll(sample);
        var top    = shapes[0];

        Assert.AreEqual('\t', top.Separator);
        Assert.AreEqual(3,    top.FieldCount);
        Assert.IsFalse(top.HasHeader, "First row is also strings+numbers — should not flag as header.");
    }

    [TestMethod]
    public void DetectAll_SemicolonSeparated_DetectsSemicolon()
    {
        var sample = MakeSample(
            head: new[] { "id;state;amount", "1;active;2,60", "2;active;3,40", "3;inactive;1,20" },
            tail: new[] { "4;active;5,00", "5;active;7,10", "6;active;8,80", "7;inactive;9,90" });

        var shapes = ShapeDetector.DetectAll(sample);
        var top    = shapes[0];

        Assert.AreEqual(';', top.Separator);
        Assert.AreEqual(3,   top.FieldCount);
    }

    [TestMethod]
    public void DetectAll_QuotedFieldsWithEmbeddedCommas_DetectsCommaAndQuotes()
    {
        var sample = MakeSample(
            head: new[]
            {
                "name,bio,age",
                "\"alice\",\"loves cats, dogs\",30",
                "\"bob\",\"likes hiking\",42",
                "\"carol\",\"reads books, magazines\",25",
            },
            tail: new[]
            {
                "\"dan\",\"foo\",1",
                "\"eve\",\"bar\",2",
                "\"frank\",\"baz, qux\",3",
                "\"gina\",\"quux\",4",
            });

        var shapes = ShapeDetector.DetectAll(sample);
        var top    = shapes[0];

        Assert.AreEqual(',',  top.Separator);
        Assert.AreEqual('"',  top.Quote);
        Assert.AreEqual(3,    top.FieldCount,
            "Without quote handling the embedded comma would split into 4 fields.");
    }

    [TestMethod]
    public void DetectAll_SingleColumnFile_StillReturnsAShape()
    {
        var sample = MakeSample(
            head: new[] { "101", "202", "303", "404" },
            tail: new[] { "505", "606", "707", "808" });

        var shapes = ShapeDetector.DetectAll(sample);
        Assert.IsTrue(shapes.Count > 0);
        // No genuine multi-column shape — the fallback Single-column shape should be top.
        Assert.AreEqual(1, shapes[0].FieldCount);
    }

    [TestMethod]
    public void DetectAll_EmptyFile_ReturnsTrivialShape()
    {
        var shapes = ShapeDetector.DetectAll(MakeSample(new string[0], new string[0]));
        Assert.IsTrue(shapes.Count > 0);
    }

    [TestMethod]
    public void DetectAll_HeaderLabelOverNumericColumns_FlagsHasHeader()
    {
        // Mirrors subscription.csv-style shape: every body column is Integer, header is labels.
        var sample = MakeSample(
            head: new[] { "id;subscriberId;customerNo;state", "1;100;200;active", "2;101;201;active", "3;102;202;inactive" },
            tail: new[] { "4;103;203;active", "5;104;204;inactive", "6;105;205;active", "7;106;206;active" });

        var top = ShapeDetector.DetectAll(sample)[0];

        Assert.AreEqual(';', top.Separator);
        Assert.IsTrue(top.HasHeader, "Header labels over Integer columns should flag HasHeader.");
    }

    [TestMethod]
    public void DetectAll_ColumnTypes_InferredFromBody()
    {
        var sample = MakeSample(
            head: new[] { "id,joined,active", "1,2024-01-01,true", "2,2024-02-02,false", "3,2024-03-03,true" },
            tail: new[] { "4,2024-04-04,false", "5,2024-05-05,true", "6,2024-06-06,false", "7,2024-07-07,true" });

        var top = ShapeDetector.DetectAll(sample)[0];

        Assert.AreEqual(',', top.Separator);
        Assert.IsTrue(top.HasHeader);
        Assert.AreEqual(CsvDataType.Integer, top.ColumnTypes[0]);
        Assert.AreEqual(CsvDataType.Date,    top.ColumnTypes[1]);
        Assert.AreEqual(CsvDataType.Boolean, top.ColumnTypes[2]);
    }
}
