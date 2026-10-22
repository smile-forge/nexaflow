using System.Collections.ObjectModel;
using System.Windows.Controls;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The status line under the logo: the newest background task with its state, and "Idle" for the manager's idle line.
/// <para>Builds the ticker off-screen on an STA thread; opens no window. Not parallel: it swaps the process-wide string
/// table behind <see cref="Str"/>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("chrome-activity-ticker")]
public class ActivityTickerTests
{
    [TestMethod]
    public void TheIdleLineReadsIdle_EvenWhenTheManagerWasBuiltBeforeTheStringTable() => UiThread.Run(() =>
    {
        var english = Str.Source;
        try
        {
            Str.Source = null;   // startup builds the manager before the language is chosen
            var tasks = new BackgroundActivityManager().Tasks;
            Str.Source = english;

            Assert.AreEqual("✓ Idle", Shown(tasks));
        }
        finally
        {
            Str.Source = english;
        }
    });

    [TestMethod]
    public void TheIdleLineIsInTheLanguageOfTheWindowShowingIt() => UiThread.Run(() =>
    {
        var english = Str.Source;
        var tasks   = new BackgroundActivityManager().Tasks;
        try
        {
            // A language switch restarts the window, not the app's task list: the fresh window's ticker words it anew.
            Str.Source = new French();
            Assert.AreEqual("✓ Inactif", Shown(tasks));
        }
        finally
        {
            Str.Source = english;
        }
    });

    [TestMethod]
    public void ARunningTaskShowsItsOwnDescription() => UiThread.Run(() =>
    {
        var manager = new BackgroundActivityManager();
        manager.StartActivity("Extracting images");

        Assert.AreEqual("⟳ Extracting images", Shown(manager.Tasks));
    });

    /// <summary>The text a ticker over <paramref name="tasks"/> draws, as a freshly opened window's does.</summary>
    private static string Shown(ObservableCollection<BackgroundTask> tasks)
    {
        var ticker = new ActivityTicker { ItemsSource = tasks };
        return ((TextBlock)((Canvas)ticker.Content).Children[0]).Text;
    }

    private sealed class French : ILocalizedStringSource
    {
        public string? Find(string key) => key == "Shell.BackgroundTasks.Idle" ? "Inactif" : null;
    }
}
