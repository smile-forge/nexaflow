using System.ComponentModel.DataAnnotations;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// What a config's attributes say of it: whether its required settings are filled — all of them, or all or none — whether the
/// setup wizard asks for it, which properties are settings, how an edited copy is put back and a typed value held, and which
/// setting greys out another.
/// </summary>
[TestClass]
[CoversNode("opt-wizard-mandatory-configs")]
public class ConfigRequirementsTests
{
    public enum Shade { Light, Dark }

    [MandatorySetup]
    private sealed class Service : IFeatureConfig
    {
        public string ConfigName   => "service";
        public string FriendlyName => "Service";

        [Required] public string Key    { get; set; } = "";
        [Required] public string Secret { get; set; } = "";
        public int    Retries { get; set; }
        public Shade  Look    { get; set; }
        public bool   Offline { get; set; }
        [DisabledIfSet(nameof(Offline))] public string Server { get; set; } = "";
        public List<string> Recent { get; set; } = [];
        public string ReadOnly => "fixed";
    }

    private sealed class Plain
    {
        public string Name { get; set; } = "";
    }

    [TestMethod]
    public void AConfig_IsComplete_OnlyWhenEveryRequiredSettingIsFilled()
    {
        Assert.IsFalse(ConfigRequirements.IsComplete(new Service { Key = "k" }));
        Assert.IsTrue(ConfigRequirements.IsComplete(new Service { Key = "k", Secret = "s" }));
        Assert.IsTrue(ConfigRequirements.IsComplete(new Plain()), "a config that requires nothing is complete");
    }

    [TestMethod]
    public void RequiredSettings_AllEmpty_OrAllFilled_AreConsistent_ButHalfFilledIsNot()
    {
        Assert.IsTrue(ConfigRequirements.RequiredAllOrNone(new Service()), "nothing entered yet");
        Assert.IsFalse(ConfigRequirements.RequiredAllOrNone(new Service { Key = "k" }), "an entry left half made");
        Assert.IsTrue(ConfigRequirements.RequiredAllOrNone(new Service { Key = "k", Secret = "s" }));
    }

    [TestMethod]
    public void TheWizard_AsksForAConfigThatSaysSo()
    {
        Assert.IsTrue(ConfigRequirements.IsMandatory(new Service()));
        Assert.IsFalse(ConfigRequirements.IsMandatory(new Plain()));
        Assert.IsTrue(ConfigRequirements.IsMandatory(new AiPersonaConfig()), "a provider-side config says so with its own attribute");
    }

    [TestMethod]
    public void TheSettings_AreTheReadWriteValuesOtherThanTheConfigsIdentity()
    {
        var names = ConfigRequirements.EditableProperties(typeof(Service)).Select(p => p.Name).ToArray();

        CollectionAssert.AreEquivalent(new[] { "Key", "Secret", "Retries", "Look", "Offline", "Server" }, names,
                                       "a list is state, a read-only property is not put back, and the name and title are the config's own");
    }

    [TestMethod]
    public void AnEditedCopy_IsPutBackWhole()
    {
        var config = new Service();
        ConfigRequirements.CopyInto(new Service { Key = "k", Retries = 3, Look = Shade.Dark }, config);

        Assert.AreEqual("k", config.Key);
        Assert.AreEqual(3, config.Retries);
        Assert.AreEqual(Shade.Dark, config.Look);
    }

    [TestMethod]
    public void ATypedValue_IsHeldAsTheSettingsKind()
    {
        Assert.AreEqual(Shade.Dark, ConfigRequirements.Coerce("Dark", typeof(Shade)));
        Assert.AreEqual(12, ConfigRequirements.Coerce("12", typeof(int)));
        Assert.AreEqual(0, ConfigRequirements.Coerce(" ", typeof(int)), "no number typed is zero");
        Assert.AreEqual("words", ConfigRequirements.Coerce("words", typeof(string)));
        Assert.IsNull(ConfigRequirements.Coerce(null, typeof(int)));
    }

    [TestMethod]
    public void ASettingThatGreysOutAnother_IsNamed_AndCountsAsSetWhenOn()
    {
        var server = typeof(Service).GetProperty(nameof(Service.Server))!;

        Assert.AreEqual((nameof(Service.Offline), true), ConfigRequirements.DisabledBy(server));
        Assert.IsNull(ConfigRequirements.DisabledBy(typeof(Service).GetProperty(nameof(Service.Key))!));
        Assert.IsTrue(ConfigRequirements.IsSet(true));
        Assert.IsFalse(ConfigRequirements.IsSet("0"));
        Assert.IsFalse(ConfigRequirements.IsSet(" "));
        Assert.IsTrue(ConfigRequirements.IsSet(2.5));
    }
}
