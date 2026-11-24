using Nexaflow.Core;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using NSubstitute;

namespace Nexaflow.Tests.Core;

/// <summary>
/// The suite's workspace runtimes, built as the app builds them — through a <see cref="WorkspaceRuntimeFactory"/> — but
/// unstarted: wired with their assistant, shell services, file system and feature scope, with no provider acquired, nothing
/// mounted and nothing run in the background. Their conversations go under the suite's own config root
/// (<see cref="TestConfigIsolation"/>).
/// </summary>
internal static class TestRuntimes
{
    /// <summary>The factory every runtime here comes from. Nobody holds a secret for its connections, and none are made.</summary>
    public static WorkspaceRuntimeFactory Factory { get; } = FactoryWith(tasks: null, notify: _ => { });

    /// <summary>A factory whose runtimes run their assistant's slow work on <paramref name="tasks"/>, telling
    /// <paramref name="notify"/> as each piece ends.</summary>
    public static WorkspaceRuntimeFactory FactoryWith(BackgroundActivityManager? tasks, Action<NotificationItem> notify) => new(
        ProviderManager.Instance, tasks, notify, VirtualFileSystem.Instance, LocationProviderRegistry.Instance,
        DiscoveredLocations.Instance, Substitute.For<ICredentialSource>(), ensureLocationProviders: () => { });

    /// <summary>A wired, unstarted runtime for <paramref name="workspace"/> (a fresh default one when none is given), standing ready
    /// for a launch that has not come yet when <paramref name="standby"/>.</summary>
    public static WorkspaceRuntime Unstarted(Workspace? workspace = null, bool standby = false)
    {
        var runtime = Factory.CreateUnstarted(workspace ?? new Workspace());
        runtime.IsStandby = standby;
        return runtime;
    }
}
