using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Nexaflow.Interaction;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Dropping;

namespace Nexaflow.Tests.Visuals.Dropping;

/// <summary>
/// The window's one drop router, dragged over through the drop target Windows itself would call (<see cref="OleDrag"/>):
/// who is asked, who wins, and what the source is told.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("vcommon-drop-router")]
public class DropRouterTests
{
    private sealed class Target(params string[] kinds) : IDropTarget
    {
        public DropOffer? Answer { get; init; } = new(DropEffect.Copy);
        public Exception? Throws { get; init; }
        public List<DropRequest> Asked { get; } = [];
        public List<DropRequest> Taken { get; } = [];
        public List<string[]> FilesTaken { get; } = [];
        public int Ended { get; private set; }

        public IReadOnlyList<string> AcceptedKinds => kinds;

        public DropOffer? OfferDrop(DropRequest request)
        {
            Asked.Add(request);
            return Answer;
        }

        public Task AcceptDropAsync(DropRequest request)
        {
            if (Throws is { } failure) throw failure;
            Taken.Add(request);
            FilesTaken.Add([.. request.Payload.Files()]);
            return Task.CompletedTask;
        }

        public void EndDropHover() => Ended++;
    }

    /// <summary>A target that waits — for <paramref name="gate"/>, as one asking what to do would — before taking the drop.</summary>
    private sealed class Waiting(Task gate, Exception? throws = null) : IDropTarget
    {
        public List<string> FilesTaken { get; } = [];

        public IReadOnlyList<string> AcceptedKinds => [DragKinds.Files];

        public DropOffer? OfferDrop(DropRequest request) => new(DropEffect.Copy);

        public async Task AcceptDropAsync(DropRequest request)
        {
            await gate;
            if (throws is not null) throw throws;
            FilesTaken.AddRange(request.Payload.Files());
        }
    }

    /// <summary>Runs what the drop left queued on the window's thread.</summary>
    private static void Settle()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    private static DataObject Files() => new(DataFormats.FileDrop, new[] { @"C:\drop\a.txt" });

    private static DataObject Words() => new(DataFormats.UnicodeText, "some words");

    private static DataObject OwnGesture() => new("Nexaflow.Tests.Gesture", "row 3");

    /// <summary>A page around <paramref name="child"/>, in a routed window nobody sees.</summary>
    private static void InWindow(FrameworkElement child, Action<Window, Border, List<Exception>> body,
                                 Func<DependencyObject, IDropTarget?>? targetOf = null)
    {
        var failures = new List<Exception>();
        var page = new Border { Child = child, Padding = new Thickness(60), Background = Brushes.Gray };
        var window = new Window
        {
            WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false,
            Left = -4000, Top = -4000, Width = 400, Height = 400, Content = page,
        };
        DropRouter.Attach(window, failures.Add, targetOf);
        try
        {
            window.Show();
            window.UpdateLayout();
            body(window, page, failures);
        }
        finally
        {
            window.Close();
        }
    }

    private static Border Plain() => new() { Background = Brushes.White };

    [TestMethod]
    public void WhatNothingTakes_IsRefused_RatherThanMoved() => UiThread.Run(() =>
    {
        var child = Plain();
        InWindow(child, (window, _, _) =>
        {
            using var drag = new OleDrag(window, Files());

            Assert.AreEqual(DragDropEffects.None, drag.Over(child), "left to WPF, a hover nobody handles answers Move");
            Assert.AreEqual(DragDropEffects.None, drag.Drop(child), "and a drop, everything the source allowed");
        });
    });

    [TestMethod]
    public void ATarget_IsGivenTheDrag_AndTheSourceIsToldTheOneThingDone() => UiThread.Run(() =>
    {
        var child = Plain();
        var target = new Target(DragKinds.Files);
        DropSurface.SetTarget(child, target);

        InWindow(child, (window, _, _) =>
        {
            using var drag = new OleDrag(window, Files());

            Assert.AreEqual(DragDropEffects.Copy, drag.Over(child));
            Assert.AreEqual(DragDropEffects.Copy, drag.Drop(child));
            Assert.AreEqual(1, target.Taken.Count, "once");
            CollectionAssert.AreEqual(new[] { @"C:\drop\a.txt" }, target.FilesTaken[0]);
            Assert.AreEqual(child.ActualWidth / 2, target.Taken[0].At.X, 2, "in the surface's own coordinates");
            Assert.AreEqual(1, target.Ended, "and told the hover is over");
        });
    });

    [TestMethod]
    public void ATarget_IsAskedOnlyOfWhatItSaidItTakes() => UiThread.Run(() =>
    {
        var child = Plain();
        var target = new Target(DragKinds.Image);
        DropSurface.SetTarget(child, target);

        InWindow(child, (window, _, _) =>
        {
            using var drag = new OleDrag(window, Files());

            Assert.AreEqual(DragDropEffects.None, drag.Over(child));
            Assert.AreEqual(0, target.Asked.Count);
        });
    });

    [TestMethod]
    public void TheInnermostTargetWins() => UiThread.Run(() =>
    {
        var child = Plain();
        var inner = new Target(DragKinds.Files);
        var outer = new Target(DragKinds.Files);
        DropSurface.SetTarget(child, inner);

        InWindow(child, (window, page, _) =>
        {
            DropSurface.SetTarget(page, outer);
            using var drag = new OleDrag(window, Files());
            drag.Over(child);
            drag.Drop(child);

            Assert.AreEqual(1, inner.Taken.Count);
            Assert.AreEqual(0, outer.Asked.Count);
        });
    });

    [TestMethod]
    public void ATargetThatPasses_LeavesItToTheSurfaceAroundIt() => UiThread.Run(() =>
    {
        var child = Plain();
        var outer = new Target(DragKinds.Files);
        DropSurface.SetTarget(child, new Target(DragKinds.Files) { Answer = null });

        InWindow(child, (window, page, _) =>
        {
            DropSurface.SetTarget(page, outer);
            using var drag = new OleDrag(window, Files());
            drag.Over(child);
            drag.Drop(child);

            Assert.AreEqual(1, outer.Taken.Count);
        });
    });

    [TestMethod]
    public void ATargetThatRefuses_EndsTheAsking() => UiThread.Run(() =>
    {
        var child = Plain();
        var outer = new Target(DragKinds.Files);
        DropSurface.SetTarget(child, new Target(DragKinds.Files) { Answer = DropOffer.Refused });

        InWindow(child, (window, page, _) =>
        {
            DropSurface.SetTarget(page, outer);
            using var drag = new OleDrag(window, Files());

            Assert.AreEqual(DragDropEffects.None, drag.Over(child));
            Assert.AreEqual(0, outer.Asked.Count);
        });
    });

    [TestMethod]
    public void AFileOverATextBox_ReachesThePageAroundIt() => UiThread.Run(() =>
    {
        var box = new TextBox();
        var target = new Target(DragKinds.Files, DragKinds.Text);

        InWindow(box, (window, page, _) =>
        {
            DropSurface.SetTarget(page, target);
            using var drag = new OleDrag(window, Files());

            Assert.AreEqual(DragDropEffects.Copy, drag.Over(box), "the box's refusal is not the window's");
            Assert.AreEqual(DragDropEffects.Copy, drag.Drop(box));
            Assert.AreEqual(1, target.Taken.Count);
        });
    });

    [TestMethod]
    public void WordsOverATextBox_AreTheBoxs() => UiThread.Run(() =>
    {
        var box = new TextBox();
        var target = new Target(DragKinds.Text);

        InWindow(box, (window, page, _) =>
        {
            DropSurface.SetTarget(page, target);
            using var drag = new OleDrag(window, Words(), DragDropEffects.Copy);
            drag.Over(box);
            drag.Drop(box);

            StringAssert.Contains(box.Text, "some words");
            Assert.AreEqual(0, target.Taken.Count);
        });
    });

    [TestMethod]
    public void ATargetOnATextBoxItself_IsAskedBeforeTheBox() => UiThread.Run(() =>
    {
        var box = new TextBox();
        var target = new Target(DragKinds.Text);
        DropSurface.SetTarget(box, target);

        InWindow(box, (window, _, _) =>
        {
            using var drag = new OleDrag(window, Words(), DragDropEffects.Copy);
            drag.Over(box);
            drag.Drop(box);

            Assert.AreEqual(1, target.Taken.Count);
            Assert.AreEqual(string.Empty, box.Text, "once, not by both");
        });
    });

    [TestMethod]
    public void AGestureOfTheAppsOwn_StaysWithTheControlItIsFor() => UiThread.Run(() =>
    {
        var list = new Border { Background = Brushes.White, AllowDrop = true };
        var dropped = 0;
        list.Drop += (_, _) => dropped++;

        InWindow(list, (window, page, _) =>
        {
            DropSurface.SetTarget(page, new Target(DragKinds.Files));
            using var drag = new OleDrag(window, OwnGesture(), DragDropEffects.Move);

            Assert.AreEqual(DragDropEffects.Move, drag.Over(list), "a reorder needs no hover handler of its own");
            drag.Drop(list);
            Assert.AreEqual(1, dropped);
        });
    });

    [TestMethod]
    public void AGestureOfTheAppsOwn_IsRefusedWhereNothingIsForIt() => UiThread.Run(() =>
    {
        var child = Plain();
        InWindow(child, (window, _, _) =>
        {
            using var drag = new OleDrag(window, OwnGesture(), DragDropEffects.Move);

            Assert.AreEqual(DragDropEffects.None, drag.Over(child));
        });
    });

    [TestMethod]
    public void AFileOverAListThatReorders_ReachesThePageAroundIt() => UiThread.Run(() =>
    {
        var list = new Border { Background = Brushes.White, AllowDrop = true };
        list.Drop += (_, _) => { };
        var target = new Target(DragKinds.Files);

        InWindow(list, (window, page, _) =>
        {
            DropSurface.SetTarget(page, target);
            using var drag = new OleDrag(window, Files());

            Assert.AreEqual(DragDropEffects.Copy, drag.Over(list));
            drag.Drop(list);
            Assert.AreEqual(1, target.Taken.Count);
        });
    });

    [TestMethod]
    public void FilesMoved_AreNeverReportedMoved_WhichIsLeaveToDeleteThem() => UiThread.Run(() =>
    {
        var child = Plain();
        var target = new Target(DragKinds.Files) { Answer = new DropOffer(DropEffect.Move) };
        DropSurface.SetTarget(child, target);

        InWindow(child, (window, _, _) =>
        {
            using var drag = new OleDrag(window, Files());

            Assert.AreEqual(DragDropEffects.Move, drag.Over(child), "the hover says move");
            Assert.AreEqual(DragDropEffects.None, drag.Drop(child), "the source is not told it may delete");
            Assert.AreEqual(1, target.Taken.Count);
        });
    });

    [TestMethod]
    public void AControlThatTookADropWithoutSayingHow_IsReportedAsOneCopy() => UiThread.Run(() =>
    {
        var child = new Border { Background = Brushes.White, AllowDrop = true };
        child.Drop += (_, e) => e.Handled = true;

        InWindow(child, (window, _, _) =>
        {
            using var drag = new OleDrag(window, Words());
            drag.Over(child);

            Assert.AreEqual(DragDropEffects.Copy, drag.Drop(child), "not Copy, Move and Link at once");
        });
    });

    [TestMethod]
    public void AMoveTheSourceDoesNotAllow_IsACopy_AndTheTargetIsAskedForOne() => UiThread.Run(() =>
    {
        var child = Plain();
        var target = new Target(DragKinds.Files) { Answer = new DropOffer(DropEffect.Move) };
        DropSurface.SetTarget(child, target);

        InWindow(child, (window, _, _) =>
        {
            using var drag = new OleDrag(window, Files(), DragDropEffects.Copy);
            drag.Over(child, DragDropKeyStates.LeftMouseButton | DragDropKeyStates.ShiftKey);

            Assert.AreEqual(DragDropEffects.Copy, drag.Drop(child, DragDropKeyStates.ShiftKey));
            Assert.IsFalse(target.Taken[0].Modifiers.HasFlag(KeyModifiers.Shift));
        });
    });

    [TestMethod]
    public void TheHoverIsWhatRemembersTheRightButton_BecauseTheDropCannotBeAsked() => UiThread.Run(() =>
    {
        var child = Plain();
        var target = new Target(DragKinds.Files);
        DropSurface.SetTarget(child, target);

        InWindow(child, (window, _, _) =>
        {
            using var drag = new OleDrag(window, Files());
            drag.Over(child, DragDropKeyStates.RightMouseButton);
            drag.Drop(child);
            drag.Over(child, DragDropKeyStates.LeftMouseButton);
            drag.Drop(child);

            Assert.IsTrue(target.Taken[0].AsksChoice);
            Assert.IsFalse(target.Taken[1].AsksChoice, "an ordinary drag takes it back");
        });
    });

    [TestMethod]
    public void AnElementCanStandForATarget_AsAPageDoesForItsViewModel() => UiThread.Run(() =>
    {
        var child = Plain();
        var target = new Target(DragKinds.Text);

        InWindow(child, (window, _, _) =>
        {
            using var drag = new OleDrag(window, Words(), DragDropEffects.Copy);
            drag.Over(child);
            drag.Drop(child);

            Assert.AreEqual(1, target.Taken.Count);
        }, element => ReferenceEquals(element, child) ? target : null);
    });

    [TestMethod]
    public void WhatATargetThrows_IsReported_AndTheSourceToldNothingWasDone() => UiThread.Run(() =>
    {
        var child = Plain();
        DropSurface.SetTarget(child, new Target(DragKinds.Files) { Throws = new InvalidOperationException("no") });

        InWindow(child, (window, _, failures) =>
        {
            using var drag = new OleDrag(window, Files());
            drag.Over(child);

            Assert.AreEqual(DragDropEffects.None, drag.Drop(child));
            Assert.AreEqual("no", failures.Single().Message);
        });
    });

    /// <summary>What a drop carried is taken while Windows still holds it, so a target that waits first still has it afterwards.</summary>
    [TestMethod]
    public void ATargetThatWaits_StillHasWhatTheDragCarried_AfterTheDropHasReturned() => UiThread.Run(() =>
    {
        var child = Plain();
        var gate = new TaskCompletionSource();
        var target = new Waiting(gate.Task);
        DropSurface.SetTarget(child, target);

        InWindow(child, (window, _, failures) =>
        {
            using (var drag = new OleDrag(window, Files()))
            {
                drag.Over(child);
                Assert.AreEqual(DragDropEffects.Copy, drag.Drop(child));
            }
            Assert.AreEqual(0, target.FilesTaken.Count, "nothing is taken while the target waits");

            gate.SetResult();
            Settle();

            CollectionAssert.AreEqual(new[] { @"C:\drop\a.txt" }, target.FilesTaken);
            Assert.AreEqual(0, failures.Count);
        });
    });

    [TestMethod]
    public void WhatATargetThrowsAfterItWaits_IsReported_ThoughTheDropWasAnswered() => UiThread.Run(() =>
    {
        var child = Plain();
        var gate = new TaskCompletionSource();
        DropSurface.SetTarget(child, new Waiting(gate.Task, new InvalidOperationException("later")));

        InWindow(child, (window, _, failures) =>
        {
            using (var drag = new OleDrag(window, Files()))
            {
                drag.Over(child);
                Assert.AreEqual(DragDropEffects.Copy, drag.Drop(child));
            }

            gate.SetResult();
            Settle();

            Assert.AreEqual("later", failures.Single().Message);
        });
    });

    [TestMethod]
    public void LeavingTheWindow_EndsTheHover() => UiThread.Run(() =>
    {
        var child = Plain();
        var target = new Target(DragKinds.Files);
        DropSurface.SetTarget(child, target);

        InWindow(child, (window, _, _) =>
        {
            using var drag = new OleDrag(window, Files());
            drag.Over(child);
            drag.Leave();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);

            Assert.AreEqual(1, target.Ended);
        });
    });
}
