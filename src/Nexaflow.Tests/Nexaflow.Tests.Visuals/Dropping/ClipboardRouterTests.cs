using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Interaction;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Dropping;

namespace Nexaflow.Tests.Visuals.Dropping;

/// <summary>
/// The window's clipboard router, over a clipboard of the test's own: where a paste goes, what it is handed, what pastes for
/// itself, and how a control with no view-model reaches the clipboard.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("vcommon-drop-router")]
public class ClipboardRouterTests
{
    private sealed class PasteBox(params string[] kinds) : Border, IPasteTarget
    {
        public List<IDragPayload> Pasted { get; } = [];
        public Exception? Throws { get; init; }

        public IReadOnlyList<string> PasteKinds => kinds;

        public Task PasteAsync(IDragPayload pasted)
        {
            if (Throws is { } failure) throw failure;
            Pasted.Add(pasted);
            return Task.CompletedTask;
        }
    }

    /// <summary>A page around <paramref name="child"/>, in a routed window nobody sees.</summary>
    private static void InWindow(FrameworkElement child, MemoryClipboard clipboard, Action<ClipboardRouter, List<Exception>> body,
                                 Func<IPasteTarget?>? pageTarget = null)
    {
        var failures = new List<Exception>();
        var window = new Window
        {
            WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false,
            Left = -4000, Top = -4000, Width = 400, Height = 400,
            Content = new Border { Child = child, Padding = new Thickness(40), Background = Brushes.Gray },
        };
        var router = ClipboardRouter.Attach(window, clipboard, failures.Add, pageTarget: pageTarget);
        try
        {
            window.Show();
            window.UpdateLayout();
            body(router, failures);
        }
        finally
        {
            window.Close();
        }
    }

    private static MemoryClipboard Holding(IDragPayload payload)
    {
        var clipboard = new MemoryClipboard();
        clipboard.Hold(payload);
        return clipboard;
    }

    [TestMethod]
    public void APaste_GoesToTheTargetNearestWhereItWasAsked_WithWhatTheClipboardHeld() => UiThread.Run(() =>
    {
        var inner = new PasteBox(DragKinds.Text);
        var outer = new PasteBox(DragKinds.Text) { Child = inner };

        InWindow(outer, Holding(ComposedPayload.OfText("pasted words")), (_, failures) =>
        {
            ApplicationCommands.Paste.Execute(null, inner);

            Assert.AreEqual("pasted words", inner.Pasted.Single().Text());
            Assert.AreEqual(0, outer.Pasted.Count, "the nearest takes it");
            Assert.AreEqual(0, failures.Count);
        });
    });

    [TestMethod]
    public void ATargetThatCannotTakeWhatIsHeld_LeavesItToTheOneAroundIt() => UiThread.Run(() =>
    {
        var words = new PasteBox(DragKinds.Text);
        var pictures = new PasteBox(DragKinds.Image) { Child = words };

        InWindow(pictures, Holding(ComposedPayload.OfImage([1, 2, 3])), (router, _) =>
        {
            Assert.AreSame(pictures, router.TargetFor(words));

            ApplicationCommands.Paste.Execute(null, words);

            Assert.AreEqual(0, words.Pasted.Count);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, pictures.Pasted.Single().Image());
        });
    });

    [TestMethod]
    public void NothingIsOffered_WhereNoTargetTakesWhatIsHeld() => UiThread.Run(() =>
    {
        var words = new PasteBox(DragKinds.Text);

        InWindow(words, Holding(ComposedPayload.OfImage([1])), (_, _) =>
            Assert.IsFalse(ApplicationCommands.Paste.CanExecute(null, words)));
    });

    [TestMethod]
    public void APasteBegunInATextBox_IsTheBoxs_NotThePageAroundIt() => UiThread.Run(() =>
    {
        var box = new TextBox();
        var page = new PasteBox(DragKinds.Text) { Child = box };

        InWindow(page, Holding(ComposedPayload.OfText("words")), (router, _) =>
            Assert.IsNull(router.TargetFor(box), "a text box pastes for itself"), pageTarget: () => page);
    });

    [TestMethod]
    public void WithNothingNearerToTakeIt_TheActivePageTakesThePaste() => UiThread.Run(() =>
    {
        var plain = new Border();
        var page = new PasteBox(DragKinds.Files);

        InWindow(plain, Holding(ComposedPayload.OfFiles([@"C:\copied\a.txt"], cut: true)), (_, _) =>
        {
            ApplicationCommands.Paste.Execute(null, plain);

            var pasted = page.Pasted.Single();
            CollectionAssert.AreEqual(new[] { @"C:\copied\a.txt" }, pasted.Files().ToArray());
            Assert.IsTrue(pasted.Contains(DragKinds.FilesCut), "a cut is still a cut once taken");
        }, pageTarget: () => page);
    });

    [TestMethod]
    public void WhatATargetThrows_IsReported() => UiThread.Run(() =>
    {
        var box = new PasteBox(DragKinds.Text) { Throws = new InvalidOperationException("no") };

        InWindow(box, Holding(ComposedPayload.OfText("words")), (_, failures) =>
        {
            ApplicationCommands.Paste.Execute(null, box);
            Assert.AreEqual("no", failures.Single().Message);
        });
    });

    [TestMethod]
    public void AControlWithNoViewModel_WritesToTheClipboardItsWindowOffers() => UiThread.Run(() =>
    {
        var clipboard = new MemoryClipboard();
        var below = new Border();
        var window = new Window { Content = new Border { Child = below } };

        // Inherited by everything the window shows: set on the window, read from far below it.
        ClipboardRouter.SetClipboard(window, clipboard);

        Assert.IsTrue(ClipboardRouter.Write(below, ComposedPayload.OfText("copied")).GetAwaiter().GetResult());
        Assert.AreEqual("copied", clipboard.Held?.Text());
        window.Close();
    });

    [TestMethod]
    public void Writing_FromSomethingShownNowhere_SaysNothingWasWritten() => UiThread.Run(() =>
        Assert.IsFalse(ClipboardRouter.Write(new Border(), ComposedPayload.OfText("lost")).GetAwaiter().GetResult()));
}
