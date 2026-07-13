using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>Detaches a VHD/VHDX disk image elevated.</summary>
internal sealed class DiskUnmountOperation : IElevatedOperation
{
    public string Id => ElevatedOps.DiskUnmount;

    public ElevatedOperationResult Execute(IReadOnlyDictionary<string, string> args)
    {
        if (!args.TryGetValue(ElevatedArgs.ImagePath, out var path) || string.IsNullOrWhiteSpace(path))
            return ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed, "Missing image path.");

        var (ok, message) = DiskImageMountHelper.Dismount(path);
        return ok
            ? ElevatedOperationResult.Ok(Id, message)
            : ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed, message);
    }
}
