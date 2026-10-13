using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Providers.Support;

/// <summary>
/// Minimal <see cref="IBackgroundActivityManager"/> for provider construction in tests.
/// Records how many activities were started so a test can assert the no-network paths
/// (model lists, model info, capabilities) never touch the ticker.
/// </summary>
internal sealed class FakeActivityManager : IBackgroundActivityManager
{
    public int StartedCount { get; private set; }

    /// <summary>What the activities said they were doing as they went, in order.</summary>
    public List<string> Updates { get; } = [];

    public IActivityHandle StartActivity(string description)
    {
        StartedCount++;
        return new Handle(this);
    }

    private sealed class Handle(FakeActivityManager manager) : IActivityHandle
    {
        public void Complete() { }
        public void Fail(string? reason = null) { }
        public void Update(string description) => manager.Updates.Add(description);
    }
}
