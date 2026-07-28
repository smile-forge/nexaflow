using System;
using System.Threading.Tasks;

namespace Nexaflow.Tests.Features.Infrastructure;

/// <summary>
/// Runs an async delegate to completion on the calling thread under a single-threaded
/// <c>SynchronizationContext</c>. Kept as a thin forwarder so existing tests' usings don't change; the
/// implementation lives in <see cref="Nexaflow.Tests.Fixtures.AsyncPumpCore"/>, shared with the Core test
/// project, which can't reference this one.
/// </summary>
public static class AsyncPump
{
    public static void Run(Func<Task> work) => Nexaflow.Tests.Fixtures.AsyncPumpCore.Run(work);
}
