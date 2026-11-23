using System.Collections.Concurrent;
using Nexaflow.IO.Location.Wsl.Protocol;
using Nexaflow.WslHelper.Files;
using Nexaflow.WslHelper.Native;
using Nexaflow.WslHelper.Programs;
using Nexaflow.WslHelper.Watching;

namespace Nexaflow.WslHelper;

/// <summary>
/// One run of the helper: reads the backend's requests until it closes stdin, answering each on a task of its own so a
/// long listing never holds up a stat. A request still answering is cancelled by its id and sends nothing more; a watch
/// already set up is ended by the same cancel.
/// </summary>
internal sealed class HelperSession(ReplyChannel replies, FileRequests files, InotifyWatcher watcher, AccountNames accounts,
                                    ProgramRequests programs)
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _running = new();

    /// <summary>Answers until stdin ends between frames (0), or carries something that is not a frame (2).</summary>
    public async Task<int> RunAsync(Stream input)
    {
        try
        {
            while (await WslWire.ReadAsync(input, CancellationToken.None).ConfigureAwait(false) is { } frame)
                Accept(frame);
            return 0;
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"nexaflow-wsl-helper: {e.Message}");
            return 2;
        }
    }

    private void Accept(WslFrame frame)
    {
        if (frame.Code == (byte)WslOp.Cancel)
        {
            Cancel(frame);
            return;
        }

        var cancel = new CancellationTokenSource();
        if (!_running.TryAdd(frame.Id, cancel))
        {
            // Answering under an id already answering would be read as that request's reply.
            Console.Error.WriteLine($"nexaflow-wsl-helper: request {frame.Id} arrived while one with its id is still answering");
            cancel.Dispose();
            return;
        }
        _ = Task.Run(() => AnswerAsync(frame, cancel));
    }

    private void Cancel(WslFrame frame)
    {
        int target;
        try
        {
            target = frame.Read().Int32();
        }
        catch (InvalidDataException e)
        {
            Console.Error.WriteLine($"nexaflow-wsl-helper: a cancel without its id: {e.Message}");
            return;
        }
        if (_running.TryRemove(target, out var cancel)) cancel.Cancel();
        watcher.Remove(target);
    }

    private async Task AnswerAsync(WslFrame frame, CancellationTokenSource cancel)
    {
        CancellationToken ct = cancel.Token;
        try
        {
            await DispatchAsync(frame, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            HelperFailure failure = HelperFailure.From(e);
            try
            {
                await replies.SendAsync(new WslWriter(WslReply.Failed, frame.Id).Failure(failure.Failure, failure.Message), ct)
                             .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (IOException io)
            {
                Console.Error.WriteLine($"nexaflow-wsl-helper: request {frame.Id} failed ({failure.Message}) and could not say so: {io.Message}");
            }
        }
        finally
        {
            // A cancel takes the source out itself, and then it is left to the collector rather than disposed under it.
            if (_running.TryRemove(KeyValuePair.Create(frame.Id, cancel))) cancel.Dispose();
        }
    }

    private async Task DispatchAsync(WslFrame frame, CancellationToken ct)
    {
        WslReader request = frame.Read();
        int id = frame.Id;
        switch ((WslOp)frame.Code)
        {
            case WslOp.Hello:
                await HelloAsync(id, request.Int32(), ct).ConfigureAwait(false);
                return;

            case WslOp.List:
                await files.ListAsync(id, DistroPath.Checked(request.Text()), ct).ConfigureAwait(false);
                return;

            case WslOp.Stat:
                await files.StatAsync(id, DistroPath.Checked(request.Text()), ct).ConfigureAwait(false);
                return;

            case WslOp.CreateDirectory:
                FileRequests.CreateDirectory(DistroPath.Checked(request.Text()));
                break;

            case WslOp.Delete:
            {
                string path = DistroPath.Checked(request.Text());
                bool recursive = request.Bool();
                FileRequests.Delete(path, recursive);
                break;
            }

            case WslOp.Rename:
            {
                string from = DistroPath.Checked(request.Text());
                string to = DistroPath.Checked(request.Text());
                bool replace = request.Bool();
                FileRequests.Rename(from, to, replace);
                break;
            }

            case WslOp.Copy:
            {
                string from = DistroPath.Checked(request.Text());
                string to = DistroPath.Checked(request.Text());
                bool replace = request.Bool();
                FileRequests.Copy(from, to, replace);
                break;
            }

            case WslOp.Watch:
            {
                string path = DistroPath.Checked(request.Text());
                bool recursive = request.Bool();
                await WatchAsync(id, path, recursive, ct).ConfigureAwait(false);
                return;
            }

            case WslOp.Run:
            {
                string program = request.Text();
                string folder = DistroPath.Checked(request.Text());
                var arguments = new string[Count(request)];
                for (int i = 0; i < arguments.Length; i++) arguments[i] = request.Text();
                var environment = new KeyValuePair<string, string>[Count(request)];
                for (int i = 0; i < environment.Length; i++) environment[i] = KeyValuePair.Create(request.Text(), request.Text());
                await programs.RunAsync(id, program, folder, arguments, environment, ct).ConfigureAwait(false);
                break;
            }

            case WslOp.Unwatch:
                watcher.Remove(request.Int32());
                break;

            default:
                throw new HelperFailure(WslFailure.Other, $"request code {frame.Code} is not one this helper answers");
        }
        await replies.SendAsync(new WslWriter(WslReply.Done, id), ct).ConfigureAwait(false);
    }

    // A count read off the wire, held to what a frame could carry before anything is made that size.
    private static int Count(WslReader request)
    {
        int count = request.Int32();
        return count is >= 0 and <= WslWire.MaxFrame / 4
            ? count
            : throw new InvalidDataException($"A request said it holds {count} items.");
    }

    private async Task HelloAsync(int id, int version, CancellationToken ct)
    {
        if (version != WslWire.ProtocolVersion)
            throw new HelperFailure(WslFailure.Other, $"speaks protocol {WslWire.ProtocolVersion}");

        uint uid = Libc.GetUid();
        var (home, shell) = accounts.Login(uid);
        var reply = new WslWriter(WslReply.HelloReply, id)
            .Int32(WslWire.ProtocolVersion)
            .UInt32(uid)
            .String(Variable("HOME") ?? home ?? "/")
            .String(Variable("SHELL") ?? shell ?? "/bin/sh");
        await replies.SendAsync(reply, ct).ConfigureAwait(false);
    }

    // The watch reports nothing until its Done is out, so the backend never reads a change before the watch is set.
    private async Task WatchAsync(int id, string path, bool recursive, CancellationToken ct)
    {
        watcher.Add(id, path, recursive, ct);
        try
        {
            await replies.SendAsync(new WslWriter(WslReply.Done, id), ct).ConfigureAwait(false);
        }
        catch
        {
            watcher.Remove(id);
            throw;
        }
        watcher.Activate(id);
    }

    private static string? Variable(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
}
