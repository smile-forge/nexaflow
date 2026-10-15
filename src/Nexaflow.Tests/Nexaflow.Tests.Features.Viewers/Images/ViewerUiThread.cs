using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.FileActions;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Theming;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// One thread standing in for the app's UI thread: whatever the viewer marshals runs there, in order, as it does on
/// the dispatcher — so the bitmaps it presents are made and written on one thread.
/// </summary>
internal sealed class ViewerUiThread : IDisposable
{
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _work = new();
    private readonly Thread _thread;

    public ViewerUiThread()
    {
        _thread = new Thread(() => { foreach (var action in _work.GetConsumingEnumerable()) action(); }) { IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task Run(Action action)
    {
        if (Thread.CurrentThread == _thread) { action(); return Task.CompletedTask; }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Add(() =>
        {
            try { action(); done.SetResult(); }
            catch (Exception ex) { done.SetException(ex); }
        });
        return done.Task;
    }

    public T Invoke<T>(Func<T> read)
    {
        T value = default!;
        Run(() => value = read()).GetAwaiter().GetResult();
        return value;
    }

    public void Dispose() => _work.CompleteAdding();
}
