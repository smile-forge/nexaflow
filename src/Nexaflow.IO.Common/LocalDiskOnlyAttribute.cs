using System;

namespace Nexaflow.IO.Common;

/// <summary>
/// Declares that the file IO inside a type or member only ever reaches this machine's own disk — the app's settings,
/// caches and stores under <c>%APPDATA%</c> or <c>%LOCALAPPDATA%</c>, or a path the file system has already resolved to a
/// real local file — so it may use <c>System.IO</c> and the blocking members of <see cref="IVirtualFileSystem"/>
/// (<c>NXIO001</c>, <c>NXIO002</c>) where everything else a person can open goes through the awaitable ones.
/// <para>
/// The reason is required and is what a reviewer reads, so it says where the files are: "feature settings under
/// %APPDATA%", not "local". An empty one is reported (<c>NXIO003</c>). It covers what is lexically inside the marked
/// declaration, lambdas and local functions included, and nothing it calls.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Constructor
              | AttributeTargets.Property, Inherited = false)]
public sealed class LocalDiskOnlyAttribute : Attribute
{
    /// <param name="reason">Where the files are, and why nothing a person opens from a location can arrive here.</param>
    public LocalDiskOnlyAttribute(string reason) => Reason = reason;

    /// <summary>Where the files are, and why nothing a person opens from a location can arrive here.</summary>
    public string Reason { get; }
}
