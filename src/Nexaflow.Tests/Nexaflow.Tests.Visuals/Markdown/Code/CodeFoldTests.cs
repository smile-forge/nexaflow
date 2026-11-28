using System;
using System.Linq;
using System.Threading;

using Nexaflow.Markdown.Layout.Lines;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.Code;

/// <summary>
/// Code folded in the engine: what can be folded is read from the reading's own syntax once it lands, a fold's hidden lines are not laid,
/// the line it starts on carries a mark, and a caret sent onto a hidden line opens it.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("code-folding")]
public class CodeFoldTests
{
    private const string Source = "public class Thing\n{\n    void Run()\n    {\n        Go();\n        Go();\n    }\n}\n";

    [TestMethod]
    public void WhatCanBeFolded_IsReadFromTheCodesOwnSyntax() => UiThread.Run(() =>
    {
        var engine = Coded(Unread(Source));

        var offered = engine.Foldable();
        Assert.IsTrue(offered.Count >= 2, $"expected the class and the method to fold, got {offered.Count}");
        Assert.IsTrue(offered.All(fold => fold.End > fold.Start));
    });

    [TestMethod]
    public void AFoldsHiddenLines_AreNotLaid_AndTheLineItStartsOnCarriesAMark() => UiThread.Run(() =>
    {
        var source = Unread(Source);
        var engine = Coded(source);
        var before = engine.Laid.Lines!.Count;

        var method = engine.Foldable().Where(fold => source[fold.Start] == '{').OrderBy(fold => fold.End - fold.Start).First();
        Assert.IsTrue(engine.Editor.Folds.Fold(method.Start, method.End));

        Assert.AreEqual(before - 3, engine.Laid.Lines!.Count, "the method's body and its closing line still laid");
        Assert.IsTrue(engine.Laid.Root.SelfAndDescendants().Any(piece => piece.Kind == LinedSetting.FoldKind), "nothing says something is folded");
    });

    [TestMethod]
    public void ACaretSentOntoAHiddenLine_OpensTheFold() => UiThread.Run(() =>
    {
        var source = Unread(Source);
        var engine = Coded(source);
        var method = engine.Foldable().Where(fold => source[fold.Start] == '{').OrderBy(fold => fold.End - fold.Start).First();
        engine.Editor.Folds.Fold(method.Start, method.End);

        engine.MoveCaretTo(source.IndexOf("Go();", StringComparison.Ordinal));

        Assert.AreEqual(0, engine.Editor.Folds.Ranges.Count);
        Assert.IsFalse(engine.Laid.Root.SelfAndDescendants().Any(piece => piece.Kind == LinedSetting.FoldKind));
    });

    /// <summary>The engine showing <paramref name="source"/> as C#, laid once its reading's syntax has landed.</summary>
    private static ContentEngine Coded(string source)
    {
        var engine = new ContentEngine(WpfMedium.OnThisThread, WpfStyles.Dark);
        engine.Show("csharp", WpfStyles.Dark);
        engine.Start(source);

        using var landed = new ManualResetEventSlim();
        void Done(object? sender, EventArgs args) => landed.Set();
        engine.Reread += Done;

        try
        {
            engine.Fit(480);
            for (var tries = 0; tries < 50 && engine.Foldable().Count == 0; tries++)
            {
                landed.Wait(TimeSpan.FromMilliseconds(200));
                landed.Reset();
                engine.Relay();
            }
        }
        finally
        {
            engine.Reread -= Done;
        }

        return engine;
    }

    /// <summary>A source no reading has seen yet, so nothing kept from another test answers for it.</summary>
    private static string Unread(string source) => $"// {Guid.NewGuid():N}\n{source}";
}
