using System.Linq;
using Nexaflow.Features.Code;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.CodeIntel;

/// <summary>
/// The editable-size ceiling. The As Code editor loads the whole file into memory, so this setting is the
/// guard that decides which files open read-only (with the "open it As Text, or split it" banner) — a
/// mis-parsed option would silently let a huge file through and hang the tab, so every offered option must
/// map to a real byte count, and anything unrecognised must fall back to the documented default.
/// </summary>
[TestClass]
[CoversNode("code-config")]
public class CodeEditorConfigTests
{
    private const long Mb = 1024 * 1024;

    [TestMethod]
    public void DefaultCeiling_Is50Mb()
        => Assert.AreEqual(50 * Mb, new CodeEditorConfig().GetMaxEditableBytes());

    [TestMethod]
    public void EveryOfferedOption_MapsToItsOwnByteCount()
    {
        var sizes = CodeEditorConfig.GetSizeOptions()
            .Select(option => new CodeEditorConfig { MaxEditableFileSize = option }.GetMaxEditableBytes())
            .ToList();

        CollectionAssert.AreEqual(new[] { 5 * Mb, 10 * Mb, 25 * Mb, 50 * Mb, 100 * Mb, 250 * Mb }, sizes);
        Assert.AreEqual(sizes.Count, sizes.Distinct().Count(), "no two options may resolve to the same ceiling");
    }

    [TestMethod]
    public void UnknownValue_FallsBackToTheDefault()
        => Assert.AreEqual(50 * Mb, new CodeEditorConfig { MaxEditableFileSize = "banana" }.GetMaxEditableBytes());

    [TestMethod]
    public void ConfigIsGlobal_UnderItsOwnName()
    {
        var config = new CodeEditorConfig();

        Assert.AreEqual("code", config.ConfigName);
        Assert.AreEqual("Code Editor", config.FriendlyName);
    }
}
