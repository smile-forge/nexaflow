using System.Windows;
using System.Windows.Controls;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Tests.Visuals.Controls;

/// <summary>
/// The one line that keeps its end in view — the voice bar's line, where the reply grows as it is spoken and the newest
/// words are the ones to read.
/// <para>Interactive desktop only (WPF elements need an STA thread). Run with <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("voice-session")]
public class TailClipTests
{
    private const string Long = "It is Tuesday, October thirteenth, twenty twenty-six, and the time is a quarter to one in the morning.";

    private static (TailClip Clip, TextBlock Text) Laid(string text, double width)
    {
        var line = new TextBlock { Text = text, FontSize = 13 };
        var clip = new TailClip { Child = line };
        clip.Measure(new Size(width, 30));
        clip.Arrange(new Rect(0, 0, width, 30));
        return (clip, line);
    }

    [TestMethod]
    public void ALineThatFits_SitsAtTheLeft_Unfaded() => UiThread.Run(() =>
    {
        var (clip, line) = Laid("Short.", 400);

        Assert.IsFalse(clip.IsOverflowing);
        Assert.AreEqual(0, line.TranslatePoint(new Point(), clip).X, 0.5);
        Assert.AreEqual(line.DesiredSize.Width, clip.DesiredSize.Width, 0.5, "no wider than its line");
        Assert.IsNull(clip.OpacityMask);
    });

    [TestMethod]
    public void ALineTooLong_KeepsItsEndInView_AndFadesItsStart() => UiThread.Run(() =>
    {
        var (clip, line) = Laid(Long, 120);

        Assert.IsTrue(clip.IsOverflowing);
        Assert.AreEqual(120 - line.DesiredSize.Width, line.TranslatePoint(new Point(), clip).X, 0.5, "its end at the right edge");
        Assert.AreEqual(120, clip.DesiredSize.Width, 0.5, "it asks for no more than it is given");
        Assert.IsNotNull(clip.OpacityMask, "the start fades where it is cut");
    });
}
