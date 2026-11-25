using Microsoft.Extensions.Logging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A feature whose constructor asks for an <see cref="ILogger{T}"/> is given one in the session's logs, so what it writes reaches
/// the same destinations the shell's own entries do.
/// </summary>
[TestClass]
[NoCoverage("feature DI injection semantics — no single product node")]
public class FeatureLoggerInjectionTests
{
    [TestMethod]
    public void ALoggerAskedFor_IsOneOfTheSessionsLogs()
    {
        var built = (WantsALog)TestRuntimes.Features.Injector.Instantiate(typeof(WantsALog), TestRuntimes.Unstarted());

        Assert.IsTrue(built.Log.IsEnabled(LogLevel.Error), "a fault it logs reaches the crash log");
        Assert.IsTrue(built.Log.IsEnabled(LogLevel.Information), "a step it logs reaches the session's log");
        Assert.IsFalse(built.Log.IsEnabled(LogLevel.Debug), "below what any destination keeps, nothing is made of an entry");
    }

    public sealed class WantsALog(ILogger<WantsALog> log)
    {
        public ILogger<WantsALog> Log { get; } = log;
    }
}
