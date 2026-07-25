using System;
using System.Windows.Media.Media3D;
using Nexaflow.Features.Model3D.Loaders;
using Nexaflow.Features.Model3D.Viewing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Model3D;

/// <summary>
/// The camera moves behind the viewport — the Alt+right-drag turn gesture and the orbit / roll / zoom / pan
/// / reset the AI tools drive. Every one of them is a rotation or translation of a camera pose, so they are
/// asserted here directly rather than through a live <c>HelixViewport3D</c>, which needs a rendered window.
/// <para>
/// What matters is the invariants, not the matrices: an orbit must keep the camera the same distance from
/// what it is looking at (or the model appears to lurch), a roll must not move the camera at all, a zoom
/// must not change the direction of view, and a pan must not change it either. Those are exactly the ways
/// camera code goes subtly wrong.
/// </para>
/// </summary>
[TestClass]
public class CameraMathTests
{
    private const double Tolerance = 1e-9;

    /// <summary>Looking at the origin from 10 units down the +Z axis, Y up — the usual starting pose.</summary>
    private static CameraPose Facing() =>
        new(new Point3D(0, 0, 10), new Vector3D(0, 0, -10), new Vector3D(0, 1, 0));

    private static Point3D Target(CameraPose p) => p.Position + p.LookDirection;

    private static void AssertClose(double expected, double actual, string because = "") =>
        Assert.IsTrue(Math.Abs(expected - actual) < 1e-6, $"{because} (expected {expected}, got {actual})");

    // ── Orbit ─────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("model3d-camera")]
    public void Orbit_KeepsTheLookAtPointAndTheDistanceToIt()
    {
        var before = Facing();

        var after = CameraMath.Orbit(before, yawDegrees: 37, pitchDegrees: -12);

        AssertClose(before.LookDirection.Length, after.LookDirection.Length,
                    "an orbit circles the model, so the distance to it must not change");
        var target = Target(after);
        AssertClose(0, (target - Target(before)).Length, "the point being looked at must not move");
        Assert.IsTrue((after.Position - before.Position).Length > Tolerance, "the camera should have moved");
    }

    [TestMethod]
    [CoversNode("model3d-camera")]
    public void Orbit_OfAQuarterTurn_PutsTheCameraOnTheOtherAxis()
    {
        // Yaw is about the camera's own up (+Y here): 90° swings +Z round to +X.
        var after = CameraMath.Orbit(Facing(), yawDegrees: 90, pitchDegrees: 0);

        AssertClose(10, after.Position.X, "a quarter turn about up lands on the +X axis");
        AssertClose(0, after.Position.Z, "and leaves the original axis");
    }

    [TestMethod]
    [CoversNode("model3d-camera")]
    public void Orbit_ByZero_IsANoOp()
    {
        var before = Facing();

        var after = CameraMath.Orbit(before, 0, 0);

        AssertClose(0, (after.Position - before.Position).Length, "no rotation must not nudge the camera");
        AssertClose(0, (after.LookDirection - before.LookDirection).Length, "nor the direction of view");
    }

    // ── Turn about world-up (the Alt + right-drag gesture) ────────────────────

    [TestMethod]
    [CoversNode("model3d-turn-gesture")]
    public void Turn_RotatesAboutWorldUp_NotTheCamerasOwnUp()
    {
        // A camera rolled onto its side: its own up is +X, but the turn gesture must still swing the model
        // about world +Y — that is the whole point of the gesture (the turntable can't reach this rotation).
        var rolled = new CameraPose(new Point3D(0, 0, 10), new Vector3D(0, 0, -10), new Vector3D(1, 0, 0));

        var after = CameraMath.TurnAboutWorldUp(rolled, 90);

        AssertClose(10, after.Position.X, "a quarter turn about world +Y lands on the +X axis");
        AssertClose(0, after.Position.Y, "and stays in the same horizontal plane");
        AssertClose(0, after.Position.Z);
    }

    [TestMethod]
    [CoversNode("model3d-turn-gesture")]
    public void Turn_PreservesTheDistanceToTheModel()
    {
        var before = Facing();

        var after = CameraMath.TurnAboutWorldUp(before, 17);

        AssertClose(before.LookDirection.Length, after.LookDirection.Length,
                    "turning must not also zoom");
        AssertClose(0, (Target(after) - Target(before)).Length, "nor drift off the model");
    }

    [TestMethod]
    [CoversNode("model3d-turn-gesture")]
    public void Turn_IsReversible()
    {
        var before = Facing();

        var round = CameraMath.TurnAboutWorldUp(CameraMath.TurnAboutWorldUp(before, 40), -40);

        AssertClose(0, (round.Position - before.Position).Length,
                    "dragging back the same distance must land where it started");
    }

    // ── Roll ──────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("model3d-camera")]
    public void Roll_TurnsTheHorizon_WithoutMovingTheCamera()
    {
        var before = Facing();

        var after = CameraMath.Roll(before, 90);

        Assert.AreEqual(before.Position, after.Position, "a roll is a twist in place");
        Assert.AreEqual(before.LookDirection, after.LookDirection, "and looks at the same thing");
        // Looking down -Z with +Y up, a positive (clockwise to the viewer) roll takes up toward -X.
        AssertClose(-1, after.UpDirection.X, "up should have rotated a quarter turn");
        AssertClose(0, after.UpDirection.Y);
    }

    // ── Zoom ──────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("model3d-camera")]
    public void Zoom_MovesAlongTheLineOfSight_KeepingTheLookAtPoint()
    {
        var before = Facing();

        var closer = CameraMath.Zoom(before, 2);

        AssertClose(5, closer.LookDirection.Length, "a factor of 2 halves the distance");
        AssertClose(0, (Target(closer) - Target(before)).Length, "zoom must not slide off the model");
        AssertClose(5, closer.Position.Z);

        var further = CameraMath.Zoom(before, 0.5);
        AssertClose(20, further.LookDirection.Length, "a factor below 1 pulls back");
    }

    [TestMethod]
    [CoversNode("model3d-camera")]
    public void Zoom_ByANonPositiveFactor_IsRefused()
    {
        var before = Facing();

        Assert.AreEqual(before, CameraMath.Zoom(before, 0), "a zero factor would collapse the camera");
        Assert.AreEqual(before, CameraMath.Zoom(before, -3), "and a negative one would invert it");
    }

    // ── Pan ───────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("model3d-camera")]
    public void Pan_SlidesTheFrame_WithoutTurningIt()
    {
        var before = Facing();

        var after = CameraMath.Pan(before, dx: 0.25, dy: 0.5);

        Assert.AreEqual(before.LookDirection, after.LookDirection,
                        "a pan slides the frame; turning it would be an orbit");
        // Right is look × up = (0,0,-1)×(0,1,0) = (1,0,0) — dx is a fraction of the viewing distance (10).
        AssertClose(2.5, after.Position.X, "dx moves right by a fraction of the distance");
        AssertClose(5, after.Position.Y, "dy moves up by a fraction of the distance");
        AssertClose(10, after.Position.Z, "and not along the line of sight");
    }

    [TestMethod]
    [CoversNode("model3d-camera")]
    public void Pan_WithADegenerateUpVector_IsRefusedRatherThanProducingNonsense()
    {
        // Up parallel to the look direction gives no right vector to pan along.
        var degenerate = new CameraPose(new Point3D(0, 0, 10), new Vector3D(0, 0, -10), new Vector3D(0, 0, -1));

        Assert.AreEqual(degenerate, CameraMath.Pan(degenerate, 1, 1));
    }

    // ── Framing a file's authored viewpoint ───────────────────────────────────

    [TestMethod]
    [CoversNode("model3d-camera")]
    public void AuthoredView_IsFramedFromTheModelBounds()
    {
        // A glTF camera 20 units out on +Z, aimed at a unit cube centred on the origin. The file gives a
        // direction but no distance, so the framing distance has to come from the bounds.
        var view = new ModelCameraView(new Point3D(0, 0, 20), new Vector3D(0, 0, -5), new Vector3D(0, 1, 0));
        var bounds = new Rect3D(-0.5, -0.5, -0.5, 1, 1, 1);

        var pose = CameraMath.FrameAuthoredView(view, bounds);

        Assert.IsNotNull(pose);
        Assert.AreEqual(view.Position, pose!.Value.Position, "the authored position is kept verbatim");
        AssertClose(20, pose.Value.LookDirection.Length, "the look vector reaches the model centre");
        AssertClose(-1, pose.Value.LookDirection.Z / pose.Value.LookDirection.Length,
                    "and keeps the authored direction");
    }

    [TestMethod]
    [CoversNode("model3d-camera")]
    public void AuthoredView_WithNoDirection_IsRejectedSoTheViewCanZoomToExtents()
    {
        var view = new ModelCameraView(new Point3D(0, 0, 20), new Vector3D(0, 0, 0), new Vector3D(0, 1, 0));

        Assert.IsNull(CameraMath.FrameAuthoredView(view, new Rect3D(-1, -1, -1, 2, 2, 2)),
                      "a zero look direction cannot be framed — the caller must fall back");
    }

    [TestMethod]
    [CoversNode("model3d-camera")]
    public void AuthoredView_WithEmptyBounds_StillProducesAUsablePose()
    {
        var view = new ModelCameraView(new Point3D(3, 0, 0), new Vector3D(-1, 0, 0), new Vector3D(0, 1, 0));

        var pose = CameraMath.FrameAuthoredView(view, Rect3D.Empty);

        Assert.IsNotNull(pose);
        Assert.IsTrue(pose!.Value.LookDirection.Length > 0, "a degenerate camera would render nothing at all");
    }
}
