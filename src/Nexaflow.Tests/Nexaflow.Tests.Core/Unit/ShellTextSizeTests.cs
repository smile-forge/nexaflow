using Nexaflow.Core;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The shell's text size setting starts where the visuals start: the setting is the session's and the size text is drawn at is
/// the toolkit's, so each holds its own default, and a person who never touches the setting sees text at the size the visuals
/// were designed around.
/// </summary>
[TestClass]
[CoversNode("opt-shell-text-size")]
public class ShellTextSizeTests
{
    [TestMethod]
    public void TheSettingsDefault_IsTheSizeTheVisualsStartAt()
    {
        Assert.AreEqual(TextTypography.DefaultBaseFontSize, new ShellConfig().TextFontSize);
    }
}
