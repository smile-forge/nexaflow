using System.Linq;
using Nexaflow.Features.SystemInfo.Converters;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Features.SystemInfo.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.SystemInfo;

/// <summary>
/// The dashboard's five cards and the health colouring behind their values, asserted against a real
/// <see cref="SystemInfoCollector"/> run on this machine. The collector's contract is that every probe is
/// independently fault-tolerant — a WMI class that isn't there, a registry key that can't be read — so the
/// card must still appear with its facts degraded to "—" rather than the whole snapshot failing. That
/// resilience is exactly what a live run pins down; the specific values are machine-dependent and are not
/// asserted.
/// </summary>
[TestClass]
public class SystemInfoDashboardTests
{
    private static readonly SystemInfoSnapshot Snapshot = new SystemInfoCollector().Collect();

    private static SystemInfoSection Card(string title)
        => Snapshot.Sections.SingleOrDefault(s => s.Title == title)
           ?? throw new AssertFailedException($"The dashboard is missing its '{title}' card.");

    [TestMethod]
    [CoversNode("operating-system")]
    public void OperatingSystemCard_NamesTheMachineAndItsWindowsBuild()
    {
        var card = Card("Operating System");

        Assert.IsTrue(card.Items.Count > 0);
        CollectionAssert.IsSubsetOf(new[] { "Name", "Version", "Architecture", "Computer Name" },
                                    card.Items.Select(i => i.Label).ToList());
    }

    [TestMethod]
    [CoversNode("hardware")]
    public void HardwareCard_IsPresentAndPopulated()
        => Assert.IsTrue(Card("Hardware").Items.Count > 0);

    [TestMethod]
    [CoversNode("display")]
    public void DisplayCard_IsPresentAndPopulated()
        => Assert.IsTrue(Card("Display").Items.Count > 0);

    [TestMethod]
    [CoversNode("storage")]
    public void StorageCard_IsPresentAndPopulated()
        => Assert.IsTrue(Card("Storage").Items.Count > 0);

    [TestMethod]
    [CoversNode("windows-security")]
    public void SecurityCard_ReportsEachProtectionWithAVerdict_NotJustText()
    {
        var card = Snapshot.Sections.Single(s => s.Items.Any(
            i => i.Status is SystemInfoStatus.Good or SystemInfoStatus.Warning or SystemInfoStatus.Bad));

        Assert.IsTrue(card.Items.Count > 0);
        Assert.IsTrue(card.Items.Any(i => i.Status != SystemInfoStatus.Neutral),
                      "the security card exists to flag protections that are off — a purely neutral card is a bug");
    }

    [TestMethod]
    [CoversNode("sysinfoview")]
    public void EveryCard_FillsAMissingFactWithADash_RatherThanBlank()
    {
        Assert.AreEqual(5, Snapshot.Sections.Count, "the dashboard is five cards");
        Assert.IsFalse(Snapshot.Sections.SelectMany(s => s.Items).Any(i => string.IsNullOrWhiteSpace(i.Value)),
                       "a probe that couldn't read its value must render '—', never an empty cell");
        StringAssert.Contains(Snapshot.ToPlainText(), "Operating System",
                              "the plain-text rendering feeds the AI context and copy-to-clipboard");
    }

    // ── Health colour coding ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("sysinfo-statuscolour")]
    public void EveryStatus_ResolvesToASemanticThemeToken()
    {
        Assert.AreEqual("SuccessBrush", StatusToBrushConverter.ResourceKey(SystemInfoStatus.Good));
        Assert.AreEqual("WarningBrush", StatusToBrushConverter.ResourceKey(SystemInfoStatus.Warning));
        Assert.AreEqual("DangerBrush", StatusToBrushConverter.ResourceKey(SystemInfoStatus.Bad));
        Assert.AreEqual("TextBrush", StatusToBrushConverter.ResourceKey(SystemInfoStatus.Neutral));
    }

    [TestMethod]
    [CoversNode("sysinfo-statuscolour")]
    public void AnUnrecognisedStatus_PaintsAsPlainText_NotAVerdict()
        => Assert.AreEqual("TextBrush", StatusToBrushConverter.ResourceKey(null));

    [TestMethod]
    [CoversNode("sysinfo-statuscolour")]
    public void Convert_AlwaysReturnsABrush_EvenWithNoApplicationResources()
        => Assert.IsNotNull(StatusToBrushConverter.Instance.Convert(
               SystemInfoStatus.Good, typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture));

    // ── WMI probe layer ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("sysinfo-wmi")]
    public void Wmi_ReadsAKnownClass_AndDegradesToNullOnAnUnknownOne()
    {
        Assert.IsNotNull(Wmi.First("Win32_OperatingSystem")?.Str("Caption"),
                         "Win32_OperatingSystem is present on every Windows install");

        Assert.IsNull(Wmi.First("Win32_NoSuchClassAnywhere"),
                      "a missing class must degrade to null so one bad probe can't fail the whole card");
    }
}
