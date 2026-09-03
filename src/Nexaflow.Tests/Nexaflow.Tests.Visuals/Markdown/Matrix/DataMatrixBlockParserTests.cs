using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// The <c>datamatrix</c> block body: the shared types, the Data Matrix–only ones and the wire format
/// each writes, the two settings of its own, and each diagnostic.
/// </summary>
[TestClass]
[CoversNode("datamatrix-block-syntax")]
[CoversNode("datamatrix-payloads")]
public class DataMatrixBlockParserTests
{
    [TestMethod]
    public void SharedTypes_ProduceTheSamePayloadAsAQrBlock()
    {
        var block = Parse("type: wifi\nssid: Net\npassword: pw\nsecurity: WPA");
        Assert.AreEqual("WIFI:T:WPA;S:Net;P:pw;;", block.Payload);
        Assert.IsFalse(block.Options.Gs1);
    }

    [TestMethod]
    public void Ppn_DerivesTheNumberFromThePzn_AndWrapsItInMacro06()
    {
        // PZN 01234562: 0·1+1·2+2·3+3·4+4·5+5·6+6·7 = 112, 112 mod 11 = 2. PPN check: the ASCII values
        // of "1101234562" weighted 2..11 sum to 3322, and 3322 mod 97 = 24.
        var block = Parse("type: ppn\npzn: 01234562\nlot: L1\nexpiry: 271231\nserial: S9");

        Assert.AreEqual("9N1101234562241TL1D271231SS9", block.Payload);
        Assert.AreEqual(DataMatrixMacro.Macro06, block.Options.Macro);
        Assert.IsFalse(block.Options.Gs1);
    }

    [TestMethod]
    public void Ppn_RefusesAPznThatFailsItsCheck()
    {
        Assert.IsFalse(DataMatrixBlockParser.TryParse("type: ppn\npzn: 01234563", out _, out var error));
        StringAssert.Contains(error, "check digit");
    }

    [TestMethod]
    public void Ntin_BuildsTheGtinFromThePzn_UnderGs1()
    {
        // 0 4150 01234562 → mod-10 over the thirteen digits gives 3.
        var block = Parse("type: ntin\npzn: 01234562\nexpiry: 271231\nlot: L1\nserial: S9");

        Assert.AreEqual("0104150012345623" + "17271231" + "10L1" + "" + "21S9", block.Payload);
        Assert.IsTrue(block.Options.Gs1);
    }

    [TestMethod]
    public void Ntin_TakesAGtinDirectly_AndVerifiesIt()
    {
        Assert.AreEqual("0104150012345623", Parse("type: ntin\ngtin: 04150012345623").Payload);
        Assert.AreEqual("0104150012345623", Parse("type: ntin\ngtin: 4150012345623").Payload, "thirteen digits are padded");

        Assert.IsFalse(DataMatrixBlockParser.TryParse("type: ntin\ngtin: 04150012345620", out _, out var error));
        StringAssert.Contains(error, "check digit");
    }

    [TestMethod]
    public void Gs1_StripsBrackets_AndSeparatesVariableLengthElementsOnly()
    {
        var block = Parse("type: gs1\ndata: (01)04150012345623(10)LOT7(21)SN1(17)271231");

        // 01 and 17 are fixed length: no separator after them. 10 is variable and followed: separated.
        // 21 is variable and followed by 17: separated.
        Assert.AreEqual("0104150012345623" + "10LOT7" + "21SN1" + "17271231", block.Payload);
        Assert.IsTrue(block.Options.Gs1);
    }

    [TestMethod]
    public void Gs1_RefusesAFixedLengthElementOfTheWrongLength()
    {
        Assert.IsFalse(DataMatrixBlockParser.TryParse("type: gs1\ndata: (01)123", out _, out var error));
        StringAssert.Contains(error, "14");
    }

    [TestMethod]
    public void Mailmark_FixesTheSymbolSizeByFormat()
    {
        string ninety = new string('A', 90);
        var block = Parse($"type: mailmark\nformat: 9\nmessage: {ninety}");

        Assert.AreEqual((32, 32), block.Options.Size);
        Assert.AreEqual(ninety, block.Payload);

        Assert.AreEqual((24, 24), Parse($"type: mailmark\nformat: 7\nmessage: {new string('B', 51)}").Options.Size);
        Assert.AreEqual((16, 48), Parse($"type: mailmark\nformat: 29\nmessage: {new string('C', 70)}").Options.Size);
    }

    [TestMethod]
    public void Mailmark_RefusesTheWrongLengthOrCharacters()
    {
        Assert.IsFalse(DataMatrixBlockParser.TryParse("type: mailmark\nformat: 9\nmessage: SHORT", out _, out var error));
        StringAssert.Contains(error, "90");

        Assert.IsFalse(DataMatrixBlockParser.TryParse($"type: mailmark\nformat: 7\nmessage: {new string('a', 51)}", out _, out error));
        StringAssert.Contains(error, "upper-case");
    }

    [TestMethod]
    public void ShapeAndSize_AreRead()
    {
        Assert.AreEqual(DataMatrixShape.Rectangle, Parse("type: text\ntext: x\nshape: rectangle").Options.Shape);
        Assert.AreEqual((26, 26), Parse("type: text\ntext: x\nsize: 26x26").Options.Size);
        Assert.AreEqual((16, 48), Parse("type: text\ntext: x\nsize: 16×48").Options.Size);

        Assert.IsFalse(DataMatrixBlockParser.TryParse("type: text\ntext: x\nsize: 15x15", out _, out var error));
        StringAssert.Contains(error, "not a Data Matrix size");
    }

    [TestMethod]
    public void SharedSettings_AreRead_AndUnknownKeysRefused()
    {
        var block = Parse("type: text\ntext: x\ncellSize: 6\nmargin: 1\ndark: #123456");
        Assert.AreEqual(6, block.Settings.CellSize);
        Assert.AreEqual(1, block.Settings.Margin);
        Assert.AreEqual(0x12, block.Settings.Dark!.Value.R);

        Assert.IsFalse(DataMatrixBlockParser.TryParse("type: text\ntext: x\ncellsizes: 6", out _, out var error));
        StringAssert.Contains(error, "cellsizes");
    }

    [TestMethod]
    public void AnUnknownType_ListsTheOnesThatExist()
    {
        Assert.IsFalse(DataMatrixBlockParser.TryParse("type: aztec\ntext: x", out _, out var error));
        StringAssert.Contains(error, "ppn");
        StringAssert.Contains(error, "wifi");
    }

    private static DataMatrixBlock Parse(string source)
    {
        Assert.IsTrue(DataMatrixBlockParser.TryParse(source, out var block, out var error), error);
        return block!;
    }
}
