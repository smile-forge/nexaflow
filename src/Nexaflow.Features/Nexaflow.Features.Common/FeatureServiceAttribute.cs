namespace Nexaflow.Features.Common;

/// <summary>
/// Marks a class of a feature's own as a service its registrations, handlers and actions take by constructor: one instance per
/// workspace runtime, built by the feature DI with the same injection they get — the runtime's <see cref="IShellServices"/>,
/// <see cref="IAIService"/>, file system, configs, tools, or another feature service — and shared by everything of that runtime
/// asking for it. It lives as long as the instances that captured the runtime's services: dropped, and disposed when it is
/// <see cref="System.IDisposable"/>, when the runtime is reconfigured or closes, after its tabs have gone.
/// </summary>
/// <remarks>What two page kinds of one feature share — the Images viewer's and Artist's editing engine, whose recipe store holds
/// pending writes that must not have two writers — is one of these, rather than a static keyed by something of the runtime's.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FeatureServiceAttribute : Attribute { }
