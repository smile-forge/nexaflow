using System.Globalization;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Features.Images.Views;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// What every Artist panel shares: groups a person folds away stay folded, in this tab and the next, and a value typed at
/// the end of a slider's row is read as a person writes one.
/// </summary>
[TestClass]
[CoversNode("images-edit-panels")]
public class ArtistPanelTests
{
    [TestMethod]
    public void AGroupFolded_IsKeptInTheSettings_AndOpenedAgainWhenAsked()
    {
        var config = new ImagesConfig();
        var shell = Substitute.For<IShellServices>();
        var folds = new PanelFolds(config, shell);
        Assert.IsTrue(folds["AdjustLight"], "a group is open until folded");
        Assert.IsFalse(folds["LayersMask"], "the Layers panel's sections begin folded, wanted less often than its list");

        int told = 0;
        folds.PropertyChanged += (_, e) => { if (e.PropertyName == "Item[]") told++; };
        folds["AdjustLight"] = false;
        Assert.IsFalse(new PanelFolds(config, null)["AdjustLight"], "folded, it stays folded for the next tab, which reads the same settings");
        shell.Received(1).SaveFeatureConfig(config);

        folds["AdjustLight"] = false;
        shell.Received(1).SaveFeatureConfig(config);   // folded again: nothing to keep

        folds["AdjustLight"] = true;
        Assert.IsTrue(folds["AdjustLight"]);
        Assert.AreEqual(2, told, "each change is told, so every group bound to it follows");
    }

    [TestMethod]
    public void AValueTyped_IsReadAsAPersonWritesIt_InTheirCultureAndWithItsUnit()
    {
        var was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
            foreach (var (typed, meant) in new[] { ("12", 12.0), ("−3.5", -3.5), ("+0.50", 0.5), ("45°", 45), ("200 px", 200), ("50%", 50), (" 7 ", 7) })
            {
                Assert.IsTrue(AdjustmentSlider.TryRead(typed, out var read), $"'{typed}' is a number");
                Assert.AreEqual(meant, read, 1e-9, $"'{typed}'");
            }
            Assert.IsFalse(AdjustmentSlider.TryRead("lots", out _), "a word is no number: the value stays as it was");
            Assert.IsFalse(AdjustmentSlider.TryRead("", out _));

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.IsTrue(AdjustmentSlider.TryRead("1,5", out var french));
            Assert.AreEqual(1.5, french, 1e-9, "a comma where the person's culture writes one");
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }
}
