using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>Attaches a VHD/VHDX disk image elevated, returning the assigned drive letter in
/// <see cref="ElevatedOperationResult.Data"/> under <see cref="ElevatedArgs.DriveLetter"/>.</summary>
internal sealed class DiskMountOperation : IElevatedOperation
{
    public string Id => ElevatedOps.DiskMount;

    public ElevatedOperationResult Execute(IReadOnlyDictionary<string, string> args)
    {
        if (!args.TryGetValue(ElevatedArgs.ImagePath, out var path) || string.IsNullOrWhiteSpace(path))
            return ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed, "Missing image path.");

        var (ok, drive, message) = DiskImageMountHelper.Mount(path);
        if (!ok) return ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed, message);

        var result = ElevatedOperationResult.Ok(Id, drive is null ? "Mounted." : $"Mounted as {drive}");
        if (drive is not null) result.Data[ElevatedArgs.DriveLetter] = drive;
        return result;
    }
}
