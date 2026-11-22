namespace Nexaflow.Interaction;

/// <summary>Where the pointer is, in device-independent units from the top left of the surface that was asked.</summary>
public readonly record struct DropPoint(double X, double Y);
