using System.Text.Json;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Controls;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Tests.Visuals.Theming;

/// <summary>
/// How a chosen colour is saved and read back. The three forms must round-trip through their string, a plain hex
/// string saved before swatches were tokens must still read as that colour, and anything unreadable must fall back
/// to the theme rather than fail a load.
/// </summary>
[TestClass]
[CoversNode("vcommon-color-picker")]
public class ColorSpecTests
{
    private sealed record Holder(ColorSpec Colour);

    [TestMethod]
    public void Empty_IsTheThemeDefault()
    {
        Assert.IsTrue(ColorSpec.Parse(null).IsDefault);
        Assert.IsTrue(ColorSpec.Parse("").IsDefault);
        Assert.AreEqual(string.Empty, ColorSpec.Default.ToString());
        Assert.IsNull(ColorSpec.Default.ToBrush());
        Assert.IsNull(ColorSpec.Default.Resolve());
    }

    [TestMethod]
    public void Swatch_RoundTripsByShortName()
    {
        var spec = ColorSpec.FromSwatch("Swatch.Teal");
        Assert.AreEqual(ColorSpecKind.Swatch, spec.Kind);
        Assert.AreEqual("Swatch.Teal", spec.SwatchKey);
        Assert.AreEqual("swatch:Teal", spec.ToString());
        Assert.AreEqual(spec, ColorSpec.Parse("swatch:Teal"));
        Assert.AreEqual(spec, ColorSpec.FromSwatch("Teal"));
    }

    [TestMethod]
    public void LegacyHex_ReadsAsCustom()
    {
        var spec = ColorSpec.Parse("#FF4F8EF7");
        Assert.AreEqual(ColorSpecKind.Custom, spec.Kind);
        Assert.AreEqual(Color.FromArgb(0xFF, 0x4F, 0x8E, 0xF7), spec.Color);
        Assert.AreEqual("#FF4F8EF7", spec.ToString());
    }

    [TestMethod]
    public void ShortHex_ReadsAsCustom()
        => Assert.AreEqual(Color.FromRgb(0x11, 0x22, 0x33), ColorSpec.Parse("#112233").Color);

    [TestMethod]
    public void Unreadable_FallsBackToDefault()
    {
        Assert.IsFalse(ColorSpec.TryParse("chartreuse-ish", out _));
        Assert.IsFalse(ColorSpec.TryParse("#GGHHII", out _));
        Assert.IsFalse(ColorSpec.TryParse("swatch:", out _));
        Assert.IsTrue(ColorSpec.Parse("#GGHHII").IsDefault);
    }

    [TestMethod]
    public void Custom_ResolvesToItsColour()
    {
        var colour = Color.FromArgb(0x80, 1, 2, 3);
        Assert.AreEqual(colour, ColorSpec.Custom(colour).Resolve());
        Assert.AreEqual(colour, ((SolidColorBrush)ColorSpec.Custom(colour).ToBrush()!).Color);
    }

    [TestMethod]
    public void Json_WritesTheStringAndDefaultAsNull()
    {
        Assert.AreEqual("""{"Colour":"swatch:Red"}""", JsonSerializer.Serialize(new Holder(ColorSpec.FromSwatch("Red"))));
        Assert.AreEqual("""{"Colour":null}""", JsonSerializer.Serialize(new Holder(ColorSpec.Default)));
    }

    [TestMethod]
    public void Json_ReadsEveryForm()
    {
        Assert.AreEqual(ColorSpec.FromSwatch("Red"), JsonSerializer.Deserialize<Holder>("""{"Colour":"swatch:Red"}""")!.Colour);
        Assert.AreEqual(ColorSpec.Custom(Colors.Red), JsonSerializer.Deserialize<Holder>("""{"Colour":"#FFFF0000"}""")!.Colour);
        Assert.IsTrue(JsonSerializer.Deserialize<Holder>("""{"Colour":null}""")!.Colour.IsDefault);
        Assert.IsTrue(JsonSerializer.Deserialize<Holder>("""{}""")!.Colour.IsDefault);
    }

    [TestMethod]
    [DataRow("#123", true)]
    [DataRow("123456", true)]
    [DataRow("#80123456", true)]
    [DataRow("#12345", false)]
    [DataRow("", false)]
    [DataRow("#zzzzzz", false)]
    public void PickerHex_AcceptsWpfLengthsWithOrWithoutHash(string text, bool ok)
        => Assert.AreEqual(ok, ColorPicker.TryParseHex(text, out _));
}
