using Nexaflow.Features.Images.Editing.Painting;
using Nexaflow.Features.Images.Editing.Recipe;

namespace Nexaflow.Features.Images.Editing.Selection;

/// <summary>
/// What a region's colours reach on the photo they were clicked on. A <see cref="RegionColour"/> is kept as its click, so a
/// region read back from a recipe holds colours not yet worked out: whatever turns it into a mask works them out first, from
/// the photo's pixels as it stands — read only where a colour needs them.
/// </summary>
public static class RegionColours
{
    /// <summary>Whether <paramref name="region"/> holds a colour not yet worked out.</summary>
    public static bool Unresolved(RecipeRegion? region) => region is not null && region.Steps.Any(s => s is RegionColour { Reach: null });

    /// <summary>What <paramref name="colour"/> reaches on <paramref name="photo"/>; nothing where it was clicked off the photo.</summary>
    public static TiledCoverage ReachOn(RegionColour colour, UprightPixels photo)
        => FloodFill.Reach(photo.Rgba, photo.Width, photo.Height, (int)Math.Floor(colour.X), (int)Math.Floor(colour.Y), colour.Tolerance, colour.Joined);

    /// <summary><paramref name="region"/> with every colour in it worked out on <paramref name="photo"/>.</summary>
    public static RecipeRegion? Resolved(RecipeRegion? region, UprightPixels photo)
        => !Unresolved(region) ? region
         : region! with { Steps = [.. region.Steps.Select(s => s is RegionColour { Reach: null } colour ? colour with { Reach = ReachOn(colour, photo) } : s)] };

    /// <summary><paramref name="region"/> with every colour in it worked out on <paramref name="photo"/>'s pixels, read only
    /// where a colour is still to be worked out.</summary>
    public static async Task<RecipeRegion?> ResolvedAsync(ImageEditingServices services, string photo, RecipeRegion? region, CancellationToken ct)
    {
        if (!Unresolved(region)) return region;
        var upright = await Task.Run(() => services.ReadUprightAsync(photo, ct), ct);
        return await Task.Run(() => Resolved(region, upright), ct);
    }
}
