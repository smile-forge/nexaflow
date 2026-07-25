using System.Windows.Media.Media3D;
using Nexaflow.Features.Model3D.Loaders;

namespace Nexaflow.Features.Model3D.Viewing;

/// <summary>A projection camera's placement, independent of any viewport.</summary>
public readonly record struct CameraPose(Point3D Position, Vector3D LookDirection, Vector3D UpDirection);

/// <summary>
/// The camera moves behind the viewport: the turn gesture, and the orbit / roll / zoom / pan the AI tools
/// drive. Pure <see cref="CameraPose"/> → <see cref="CameraPose"/> so each rotation is assertable without a
/// live <c>HelixViewport3D</c> — the view keeps only "read the camera, apply, write it back".
/// </summary>
public static class CameraMath
{
    private const double Epsilon = 1e-9;

    /// <summary>Orbits about the model: yaw about the camera's own up, pitch about its right vector.</summary>
    public static CameraPose Orbit(CameraPose pose, double yawDegrees, double pitchDegrees)
    {
        var up = pose.UpDirection;
        var right = Vector3D.CrossProduct(pose.LookDirection, up);
        if (right.Length < Epsilon) right = new Vector3D(1, 0, 0);
        right.Normalize();
        up.Normalize();

        var matrix = new Matrix3D();
        matrix.Rotate(new Quaternion(up, yawDegrees) * new Quaternion(right, pitchDegrees));

        return RotateAboutTarget(pose, matrix, up);
    }

    /// <summary>
    /// Turns the model about the world-up (green / Y) axis — the rotation the Z-up turntable doesn't expose,
    /// and what Alt + right-drag performs.
    /// </summary>
    public static CameraPose TurnAboutWorldUp(CameraPose pose, double degrees)
    {
        var matrix = new Matrix3D();
        matrix.Rotate(new Quaternion(new Vector3D(0, 1, 0), degrees));
        return RotateAboutTarget(pose, matrix, pose.UpDirection);
    }

    /// <summary>Rolls about the line of sight; positive degrees look clockwise to the viewer.</summary>
    public static CameraPose Roll(CameraPose pose, double degrees)
    {
        var axis = pose.LookDirection;
        if (axis.Length < Epsilon) return pose;
        axis.Normalize();

        var matrix = new Matrix3D();
        matrix.Rotate(new Quaternion(axis, -degrees));
        return pose with { UpDirection = matrix.Transform(pose.UpDirection) };
    }

    /// <summary>Zooms toward the look-at point: a factor above 1 moves in, between 0 and 1 moves out.</summary>
    public static CameraPose Zoom(CameraPose pose, double factor)
    {
        if (factor <= 0) return pose;

        var target = pose.Position + pose.LookDirection;
        var look = pose.LookDirection / factor;   // factor > 1 → shorter look vector → closer
        return pose with { Position = target - look, LookDirection = look };
    }

    /// <summary>
    /// Pans across the view plane by signed fractions of the current distance (dx right, dy up). The look
    /// vector is unchanged, so this slides the frame rather than turning it.
    /// </summary>
    public static CameraPose Pan(CameraPose pose, double dx, double dy)
    {
        var look = pose.LookDirection;
        var right = Vector3D.CrossProduct(look, pose.UpDirection);
        if (right.Length < Epsilon) return pose;
        right.Normalize();
        var up = Vector3D.CrossProduct(right, look);
        up.Normalize();

        var distance = look.Length;
        return pose with { Position = pose.Position + right * (dx * distance) + up * (dy * distance) };
    }

    /// <summary>
    /// Turns a file's authored viewpoint into a usable pose. The file gives a position and a direction but no
    /// distance, so the framing distance comes from the model bounds — the camera aims at the model centre.
    /// Returns null when the authored direction is degenerate (the caller should zoom-to-extents instead).
    /// </summary>
    public static CameraPose? FrameAuthoredView(ModelCameraView view, Rect3D bounds)
    {
        var look = view.LookDirection;
        if (look.Length < Epsilon) return null;
        look.Normalize();

        var distance = 1.0;
        if (!bounds.IsEmpty)
        {
            var centre = new Point3D(bounds.X + bounds.SizeX / 2,
                                     bounds.Y + bounds.SizeY / 2,
                                     bounds.Z + bounds.SizeZ / 2);
            var projected = Vector3D.DotProduct(centre - view.Position, look);
            distance = projected > 1e-3 ? projected : (centre - view.Position).Length;
        }

        return new CameraPose(view.Position, look * Math.Max(distance, 1e-3), view.UpDirection);
    }

    // Rotates the camera around its look-at point, carrying the up vector with it.
    private static CameraPose RotateAboutTarget(CameraPose pose, Matrix3D rotation, Vector3D up)
    {
        var target = pose.Position + pose.LookDirection;
        var position = target + rotation.Transform(pose.Position - target);
        return new CameraPose(position, target - position, rotation.Transform(up));
    }
}
