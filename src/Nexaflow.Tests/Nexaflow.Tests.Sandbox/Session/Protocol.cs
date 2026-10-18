namespace Nexaflow.Tests.Sandbox.Session;

/// <summary>What the host asks of the agent: a verb and its arguments as typed. The agent parses them against the same catalogue.</summary>
/// <param name="Id">Orders requests (it starts with the time) and names the answer and anything made for it.</param>
internal sealed record SandboxRequest(string Id, string Verb, string[] Arguments);

/// <summary>The agent's answer to one request.</summary>
/// <param name="Artifact">A file made for the request, relative to the session folder — a screenshot, a run's folder.</param>
/// <param name="ExitCode">What the host exits with; a journey run's is the runner's own.</param>
internal sealed record SandboxResponse(string Id, bool Ok, string Text, string? Artifact = null, int? ExitCode = null);

/// <summary>The agent's heartbeat, rewritten every couple of seconds. A stale one means the agent is gone.</summary>
internal sealed record AgentStatus(int Pid, DateTimeOffset Heartbeat, string? RunId, string? RunFilter, int? AppPid);

/// <summary>What the host recorded about the sandbox it started for a session.</summary>
/// <param name="Monitor">The display the window was put on, so <c>show</c> puts it back there.</param>
internal sealed record HostRecord(string SandboxId, DateTimeOffset StartedAt, string Monitor);
