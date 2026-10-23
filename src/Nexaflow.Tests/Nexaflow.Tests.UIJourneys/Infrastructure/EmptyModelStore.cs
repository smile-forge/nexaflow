using System.Collections.Generic;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.UIJourneys.Infrastructure;

/// <summary>
/// An app with none of Nexaflow's own models, the same on every machine whatever its own store holds: a model folder of
/// its own, empty, and a temp folder of its own, where a conversion keeps what page reading read — so one kept by an
/// earlier run is not found in place of the download being offered. A journey's app fetches nothing, so a download asked
/// for stops at once, saying why.
/// </summary>
public sealed class EmptyModelStore
{
    private readonly string _models = TestTemp.CreateSubdirectory("nexaflow-models-").FullName;
    private readonly string _temp = TestTemp.CreateSubdirectory("nexaflow-temp-").FullName;

    /// <summary>What the app is started with to use these folders, for <see cref="UITestBase.LaunchEnvironment"/>.</summary>
    public IReadOnlyDictionary<string, string> LaunchEnvironment => new Dictionary<string, string>
    {
        ["NEXAFLOW_AI_DIR"] = _models,
        ["TEMP"] = _temp,
        ["TMP"] = _temp,
    };
}
