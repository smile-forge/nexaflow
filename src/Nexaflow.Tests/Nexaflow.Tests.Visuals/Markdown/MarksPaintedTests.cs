using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>Marks laid over a surface's view of its document are asked only about what is near the screen, and drawn again when they change.</summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("content-marks")]
public class MarksPaintedTests
{
    [TestMethod]
    public void ALayer_IsAskedOnlyAboutTheStretchNearTheScreen() => UiThread.Run(() =>
    {
        var text = string.Join("\n", Enumerable.Range(0, 20_000).Select(line => $"line {line}"));

        MarkdownEditorHarness.Run(text, surface =>
        {
            var layer = new Asked();
            surface.Editor.Marks.Add(layer);
            surface.UpdateLayout();
            MarkdownEditorHarness.Pump();

            Assert.IsTrue(layer.Requests.Count > 0, "the layer was never asked");
            Assert.IsTrue(layer.Requests.All(request => request.End - request.Start < text.Length / 4),
                "a layer was asked about far more of the document than is near the screen");
        }, surface => surface.WrittenIn = ContentLanguages.AsTyped);
    });

    [TestMethod]
    public void WhatASearchFound_IsWhatTheViewShows() => UiThread.Run(() =>
    {
        MarkdownEditorHarness.Run("one fish two fish", surface =>
        {
            surface.Find("fish");

            Assert.AreEqual(2, surface.Editor.Found.Places.Count);
            Assert.AreEqual(surface.Found[surface.At], surface.Editor.Found.LookedAt);

            surface.Stop();
            Assert.AreEqual(0, surface.Editor.Found.Places.Count);
        }, surface => surface.WrittenIn = ContentLanguages.AsTyped);
    });

    /// <summary>A layer that marks nothing and remembers every stretch it was asked about.</summary>
    private sealed class Asked : ContentMarkLayer
    {
        public List<ContentMarkRequest> Requests { get; } = [];

        public override void Mark(ContentMarkRequest request, ICollection<ContentMark> into) => this.Requests.Add(request);
    }
}
