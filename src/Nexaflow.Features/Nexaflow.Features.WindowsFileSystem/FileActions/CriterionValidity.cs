using System;

namespace Nexaflow.Features.WindowsFileSystem.FileActions;

/// <summary>
/// Shared rule for whether a (type, value) match-criterion is sensible — used by the External Apps
/// and File Type Actions editors to ring an invalid pattern and block Save. An empty value is
/// treated as valid (it's filtered out on save); the check only flags a non-empty value that can't
/// work for its chosen type (e.g. a path/glob entered under an Extension criterion).
/// </summary>
public static class CriterionValidity
{
    public static bool IsValid(string typeName, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;

        if (string.Equals(typeName, nameof(CriteriaType.Extension), StringComparison.Ordinal) ||
            string.Equals(typeName, nameof(CriteriaType.OptionalExtension), StringComparison.Ordinal))
            return !value.Contains('\\') && !value.Contains('/') && !value.Contains("**");

        return true;   // PathPattern and the registry-derived types accept any non-empty value
    }
}
