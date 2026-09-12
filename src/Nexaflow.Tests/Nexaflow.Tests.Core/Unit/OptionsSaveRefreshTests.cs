using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Which tabs an Options save reopens.
/// </summary>
/// <remarks>
/// A refresh closes a tab and opens it again, which throws away whatever the page was doing. Reopening every
/// feature's tabs on any save closed pages whose settings nobody touched — a Network sweep in flight among
/// them.
/// </remarks>
[TestClass]
[CoversNode("opt-save-validate")]
public class OptionsSaveRefreshTests
{
    public sealed class Touched : IFeatureConfig
    {
        public string ConfigName => "test-touched";
        public string FriendlyName => "Touched";
        public string Value { get; set; } = "";
    }

    public sealed class Untouched : IFeatureConfig
    {
        public string ConfigName => "test-untouched";
        public string FriendlyName => "Untouched";
        public string Value { get; set; } = "";
    }

    [TestMethod]
    public void A_save_reopens_only_the_tabs_whose_settings_changed()
    {
        var touched = new ConfigEditViewModel(new Touched(), "test-touched", "Touched");
        var untouched = new ConfigEditViewModel(new Untouched(), "test-untouched", "Untouched");

        touched.Properties.Single(p => p.PropertyName == nameof(Touched.Value)).Value = "changed";

        CollectionAssert.AreEqual(new[] { typeof(Touched) },
                                  OptionsViewModel.ConfigTypesToRefresh([touched, untouched]).ToArray());
    }

    [TestMethod]
    public void A_save_that_changed_nothing_reopens_nothing()
    {
        var untouched = new ConfigEditViewModel(new Untouched(), "test-untouched", "Untouched");

        Assert.AreEqual(0, OptionsViewModel.ConfigTypesToRefresh([untouched]).Count);
    }
}
