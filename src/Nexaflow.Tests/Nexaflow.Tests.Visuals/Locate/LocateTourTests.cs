using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Locate;

namespace Nexaflow.Tests.Visuals.Locate;

/// <summary>
/// The tour a <c>locate:</c> link runs (<see cref="LocateTour"/>): a lasso round each control in turn, each staying for
/// its five seconds or until the reader clicks — whichever comes first — a control still being opened waited for, one
/// that never comes passed over, and nothing left on screen when it ends.
/// <para>UI category: laid out on an STA thread with its own adorner layer, and the dispatcher pumped so the tour's
/// timers actually tick. No window opens.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("help-locate")]
public class LocateTourTests
{
    private const string A = "Step_A", B = "Step_B", C = "Step_C", Later = "Step_Later";

    [TestMethod]
    public void ItLassoesEachControlInTurn_AndLeavesNothingBehind() => UiThread.Run(() =>
    {
        var scene = new Scene();
        var shown = new List<string>();

        var tour = LocateTour.Start(scene.Root, [A, B, C], scene.Brisk());
        tour.StepShown += (_, target) => shown.Add(AutomationProperties.GetAutomationId(target));
        var result = scene.Pump(tour);

        CollectionAssert.AreEqual(new[] { A, B, C }, shown, "in the order the link named them");
        Assert.AreEqual(3, result.Shown);
        Assert.AreEqual(0, result.Missing.Count);
        Assert.IsFalse(result.Cancelled);
        scene.PumpFor(TimeSpan.FromMilliseconds(400));
        Assert.AreEqual(0, scene.Lassos, "the last lasso is taken down with the tour");
    });

    [TestMethod]
    public void AClickMovesItOn_RatherThanWaitingOutTheFiveSeconds() => UiThread.Run(() =>
    {
        var scene = new Scene();
        var tour = LocateTour.Start(scene.Root, [A, B],
                                    scene.Brisk() with { StepDuration = TimeSpan.FromSeconds(30), ClickGrace = TimeSpan.Zero });
        // Each lasso is answered with a click somewhere in the window, as a reader doing what it points at would.
        tour.StepShown += (_, _) => scene.Root.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(scene.Click));

        var clock = Stopwatch.StartNew();
        var result = scene.Pump(tour);

        Assert.AreEqual(2, result.Shown);
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(10), $"clicks ended both steps, not the timer ({clock.Elapsed})");
    });

    [TestMethod]
    public void AControlStillOpening_IsWaitedFor() => UiThread.Run(() =>
    {
        var scene = new Scene();
        scene.Panel(Later).Visibility = Visibility.Collapsed;   // as if the panel holding it is not open yet
        var tour = LocateTour.Start(scene.Root, [Later], scene.Brisk() with { AppearWithin = TimeSpan.FromSeconds(3) });
        scene.After(TimeSpan.FromMilliseconds(120), () => scene.Panel(Later).Visibility = Visibility.Visible);

        var result = scene.Pump(tour);

        Assert.AreEqual(1, result.Shown, "it arrived while the tour was still looking");
        Assert.AreEqual(0, result.Missing.Count);
    });

    [TestMethod]
    public void AControlThatNeverComes_IsPassedOver_AndSaidSo() => UiThread.Run(() =>
    {
        var scene = new Scene();

        var result = scene.Pump(LocateTour.Start(scene.Root, ["Step_Nowhere", A], scene.Brisk()));

        CollectionAssert.AreEqual(new[] { "Step_Nowhere" }, result.Missing.ToArray());
        Assert.AreEqual(1, result.Shown, "the rest of the chain still runs");
    });

    [TestMethod]
    public void NothingToDrawOn_IsEveryStepMissing() => UiThread.Run(() =>
    {
        var scene = new Scene();
        var orphan = new Grid();   // no adorner layer above it

        var result = scene.Pump(LocateTour.Start(orphan, [A]));

        Assert.AreEqual(0, result.Shown);
        CollectionAssert.AreEqual(new[] { A }, result.Missing.ToArray(), "so the host can say so rather than doing nothing");
    });

    [TestMethod]
    public void AControlThatGoesAway_EndsItsStep() => UiThread.Run(() =>
    {
        var scene = new Scene();
        var tour = LocateTour.Start(scene.Root, [A, B], scene.Brisk() with { StepDuration = TimeSpan.FromSeconds(30) });
        tour.StepShown += (index, _) =>
        {
            // The first step's control is taken away under it; the second is left to prove the long step timer was not
            // what ended the first.
            if (index == 0) scene.After(TimeSpan.FromMilliseconds(60), () => scene.Panel(A).Visibility = Visibility.Collapsed);
            else scene.After(TimeSpan.FromMilliseconds(40), tour.Cancel);
        };

        var result = scene.Pump(tour);

        Assert.AreEqual(2, result.Shown, "its tab closed under it, so the tour moved on rather than pointing at nothing");
    });

    [TestMethod]
    public void StoppingIt_TakesTheLassoDown() => UiThread.Run(() =>
    {
        var scene = new Scene();
        var tour = LocateTour.Start(scene.Root, [A, B, C], scene.Brisk() with { StepDuration = TimeSpan.FromSeconds(30) });
        tour.StepShown += (_, _) => scene.After(TimeSpan.FromMilliseconds(40), tour.Cancel);

        var result = scene.Pump(tour);

        Assert.IsTrue(result.Cancelled);
        Assert.AreEqual(1, result.Shown, "the rest of the chain is dropped");
        scene.PumpFor(TimeSpan.FromMilliseconds(400));
        Assert.AreEqual(0, scene.Lassos);
    });

    [TestMethod]
    public void AsecondTour_StopsTheFirst() => UiThread.Run(() =>
    {
        var scene = new Scene();
        var first = LocateTour.Start(scene.Root, [A, B, C], scene.Brisk() with { StepDuration = TimeSpan.FromSeconds(30) });
        scene.PumpFor(TimeSpan.FromMilliseconds(200));

        var second = LocateTour.Start(scene.Root, [C], scene.Brisk());
        var result = scene.Pump(second);

        Assert.IsTrue(first.Completion.IsCompleted && first.Completion.Result.Cancelled, "one tour per window at a time");
        Assert.AreEqual(1, result.Shown);
        scene.PumpFor(TimeSpan.FromMilliseconds(400));
        Assert.AreEqual(0, scene.Lassos);
    });

    // ── A window's worth of tree, off-screen ──────────────────────────────

    private sealed class Scene
    {
        private readonly Dictionary<string, Border> _panels = new(StringComparer.Ordinal);
        private readonly List<Button> _buttons = [];
        private readonly AdornerDecorator _decorator;

        public Scene()
        {
            var stack = new StackPanel();
            foreach (var id in new[] { A, B, C, Later })
            {
                var button = new Button { Content = id, Width = 120, Height = 26 };
                AutomationProperties.SetAutomationId(button, id);
                var panel = new Border { Child = button };
                _buttons.Add(button);
                _panels[id] = panel;
                stack.Children.Add(panel);
            }

            Root = new Grid();
            Root.Children.Add(stack);
            _decorator = new AdornerDecorator { Child = Root };
            Lay();
        }

        public Grid Root { get; }

        public AdornerLayer Layer => AdornerLayer.GetAdornerLayer(Root)!;

        public int Lassos => _buttons.Sum(b => Layer.GetAdorners(b)?.Length ?? 0);

        public Border Panel(string id) => _panels[id];

        /// <summary>The same tour a reader gets, wound on: the waits are what is being tested, not how long they are.</summary>
        public LocateOptions Brisk() => new()
        {
            StepDuration = TimeSpan.FromMilliseconds(120),
            AppearWithin = TimeSpan.FromMilliseconds(300),
            ClickGrace   = TimeSpan.FromMilliseconds(20),
            PollInterval = TimeSpan.FromMilliseconds(20),
        };

        public void Click()
            => _buttons[0].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseDownEvent,
            });

        public void After(TimeSpan delay, Action action)
        {
            DispatcherTimer? timer = null;
            timer = new DispatcherTimer(delay, DispatcherPriority.Normal, (_, _) => { timer!.Stop(); Lay(); action(); Lay(); },
                                        Root.Dispatcher);
            timer.Start();
        }

        /// <summary>Runs the dispatcher until the tour finishes — its timers only tick while it does.</summary>
        public LocateResult Pump(LocateTour tour)
        {
            var frame = new DispatcherFrame();
            tour.Completion.ContinueWith(_ => frame.Continue = false, TaskContinuationOptions.ExecuteSynchronously);
            var guard = new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Send,
                                            (_, _) => frame.Continue = false, Root.Dispatcher);
            Dispatcher.PushFrame(frame);
            guard.Stop();

            Assert.IsTrue(tour.Completion.IsCompleted, "the tour never finished");
            return tour.Completion.Result;
        }

        public void PumpFor(TimeSpan time)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(time, DispatcherPriority.Send, (_, _) => frame.Continue = false, Root.Dispatcher);
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }

        private void Lay()
        {
            _decorator.Measure(new Size(600, 400));
            _decorator.Arrange(new Rect(0, 0, 600, 400));
            _decorator.UpdateLayout();
        }
    }
}
