using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Core.Models;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A runtime owns its lifetime: ending it lets go of everything it holds, in order, so nothing the session keeps — a pooled
/// provider another workspace still uses — holds the ended runtime alive.
/// </summary>
[TestClass]
[CoversNode("ws-lifetimes")]
public class WorkspaceRuntimeLifetimeTests
{
    [TestMethod]
    public async Task EndingARuntime_UnhooksItsAssistant_FromTheProvidersItListenedTo()
    {
        var provider = Substitute.For<IAiProvider>();
        var runtime  = TestRuntimes.Unstarted();
        runtime.AiService.Register("c1", provider);

        await runtime.DisposeAsync();

        provider.Received(1).AvailabilityChanged -= Arg.Any<EventHandler>();
    }

    [TestMethod]
    public void AnEndedRuntime_CanBeCollected_WhileAProviderItUsedLivesOn()
    {
        // The provider is pooled: another workspace may go on using it. Were the ended runtime's assistant still listening to
        // it, the provider would keep the assistant, and through it the whole runtime, alive.
        var provider = new Announcing();
        var ended    = EndedRuntimeUsing(provider);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.IsFalse(ended.IsAlive, "the ended runtime is held by nothing that outlives it");
        GC.KeepAlive(provider);
    }

    [TestMethod]
    public async Task EndingARuntimeAgain_DoesNothingMore()
    {
        var runtime = TestRuntimes.Unstarted();

        await runtime.DisposeAsync();
        await runtime.DisposeAsync();

        Assert.IsNull(runtime.Providers);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference EndedRuntimeUsing(IAiProvider provider)
    {
        var runtime = TestRuntimes.Unstarted();
        runtime.AiService.Register("c1", provider);
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return new WeakReference(runtime);
    }

    /// <summary>A provider whose availability can change, holding whoever listens to it as a real one does.</summary>
    private sealed class Announcing : IAiProvider
    {
        public event EventHandler? AvailabilityChanged;

        public string Name => "Announcing";

        public DataResidency Residency => DataResidency.LeavesMachine;

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public void Announce() => AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
