using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.Models;

namespace Nexaflow.Features.SystemInfo.Services;

/// <summary>
/// Where the dashboard's snapshot comes from. <see cref="SystemInfoCollector"/> probes this machine; the page
/// depends only on this, so what it does with a snapshot can be shown without a full gather of the machine.
/// </summary>
public interface ISystemInfoCollector
{
    /// <param name="localAi">Where the workspace's models of Nexaflow's own run, for the Local AI card; null when it uses none.</param>
    SystemInfoSnapshot Collect(LocalAiSetup? localAi = null);
}
