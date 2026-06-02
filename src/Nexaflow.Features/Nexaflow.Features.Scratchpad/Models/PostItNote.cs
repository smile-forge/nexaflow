namespace Nexaflow.Features.Scratchpad.Models;

public sealed class PostItNote
{
    public Guid    Id        { get; set; } = Guid.NewGuid();
    public string  Content   { get; set; } = string.Empty;
    public double  X         { get; set; } = 100;
    public double  Y         { get; set; } = 100;
    public double  Width     { get; set; } = 200;
    public double  Height    { get; set; } = 200;
    public double  Rotation  { get; set; } = 0;
    public int     ZIndex    { get; set; } = 0;
    public string  Color     { get; set; } = "Yellow";
    public string  Shape     { get; set; } = "Rounded";
    public DateTimeOffset  CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? ExpiresAt { get; set; }
}
