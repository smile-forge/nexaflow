using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.LocalAi;

/// <summary>
/// The AI host's runtime as the host itself decided it: the machine it read through NVIDIA's driver, the CUDA pack it runs on — null
/// where it runs on the CPU — and how much of the pack its store holds. What runs where is the host's to decide, so what Options →
/// About says of it is the host's word, never a reading of the app's own beside it.
/// </summary>
internal sealed record HostRuntimeReading(MachineCards Machine, string? RuntimePack, int PackFilesHere)
{
    /// <summary>The reading <paramref name="description"/> carries; null for a host that described no machine.</summary>
    public static HostRuntimeReading? Of(HostDescription description)
    {
        if (description.Machine is not { } machine) return null;
        var cards = new MachineCards([.. machine.Cards.Select(c => new MachineCard(c.Name, c.TotalBytes, c.BusId))],
                                     Version.TryParse(machine.CudaDriver, out var driver) ? driver : null);
        return new(cards, description.RuntimePack, machine.PackFilesHere);
    }
}

/// <summary>What asking the host came to: its <see cref="Reading"/>, or why there is none (<see cref="Why"/>).</summary>
internal sealed record HostRuntimeAnswer(HostRuntimeReading? Reading, string Why)
{
    public static HostRuntimeAnswer Answered(HostRuntimeReading reading) => new(reading, "");

    public static HostRuntimeAnswer Unanswered(string why) => new(null, why);
}

/// <summary>
/// Asks the AI host how it set its runtime up — starting it where none runs, joining the machine's service where that is installed —
/// for the rows Options → About probes together: one question answers them all, asked again once it is <see cref="Fresh"/> no longer.
/// </summary>
internal static class AiHostReading
{
    /// <summary>How long one answer serves: the rows of one look at the page, not a later one.</summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(2);

    /// <summary>How long a row waits for the answer — starting a host takes seconds.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(1);

    private static readonly Lock Gate = new();
    private static Task<HostRuntimeAnswer>? _asked;
    private static DateTime _askedAt;

    /// <summary>The host's answer, asked now or a moment ago for another row. Called off the UI thread, as a row's probe is.</summary>
    public static HostRuntimeAnswer Now()
    {
        Task<HostRuntimeAnswer> asked;
        lock (Gate)
        {
            if (_asked is null || DateTime.UtcNow - _askedAt > Fresh)
            {
                _asked = AskAsync();
                _askedAt = DateTime.UtcNow;
            }
            asked = _asked;
        }
        return asked.Wait(Patience) ? asked.Result : HostRuntimeAnswer.Unanswered(Str.Get("Shell.Components.LocalAi.HostSilent"));
    }

    private static async Task<HostRuntimeAnswer> AskAsync()
    {
        if (ProviderManager.Instance.Service<HostHolds>() is not { } holds)
            return HostRuntimeAnswer.Unanswered(Str.Get("Shell.Components.LocalAi.NotSetUp"));
        try
        {
            var description = await holds.DescribeAsync().ConfigureAwait(false);
            return HostRuntimeReading.Of(description) is { } reading
                ? HostRuntimeAnswer.Answered(reading)
                : HostRuntimeAnswer.Unanswered(Str.Get("Shell.Components.LocalAi.HostSilent"));
        }
        catch (AiHostException ex)
        {
            return HostRuntimeAnswer.Unanswered(Str.Format("Shell.Components.LocalAi.HostUnansweredFormat", ex.Message));
        }
    }
}
