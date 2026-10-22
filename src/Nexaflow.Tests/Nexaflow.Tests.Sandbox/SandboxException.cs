namespace Nexaflow.Tests.Sandbox;

/// <summary>Something the person running a sandbox verb can act on, said in words they can act on. Printed without a stack.</summary>
internal sealed class SandboxException(string message) : Exception(message);
