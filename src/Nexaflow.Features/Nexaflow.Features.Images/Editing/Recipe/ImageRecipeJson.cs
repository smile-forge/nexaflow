using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Nexaflow.Features.Images.Editing.Recipe;

/// <summary>How a stored recipe read back.</summary>
public enum RecipeReadStatus
{
    Read,

    /// <summary>Not a recipe this build can parse.</summary>
    Unreadable,

    /// <summary>Written by a newer build; shown unedited and never overwritten.</summary>
    Newer,
}

/// <summary>A recipe read from text, or why there is none.</summary>
public readonly record struct RecipeReadResult(RecipeReadStatus Status, ImageRecipe? Recipe);

/// <summary>
/// The sidecar's text form: indented JSON carrying a <c>version</c>. The version is read before anything else, so a
/// document from a newer build is recognised and left alone; a later format adds its upgrade from the older shape
/// to <see cref="Deserialize"/>, between that check and the typed read.
/// </summary>
/// <remarks>
/// Read by reflection rather than a source-generated context: generated code sets every init-only property, so a
/// member the file leaves out would arrive as its type's default — null adjustments, quality 0 — instead of keeping
/// the record's own initial value. A recipe is a few hundred bytes read once per photo.
/// </remarks>
public static class ImageRecipeJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented          = true,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters             = { new JsonStringEnumConverter() },
    };

    public static string Serialize(ImageRecipe recipe) => JsonSerializer.Serialize(recipe, Options);

    public static RecipeReadResult Deserialize(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return new(RecipeReadStatus.Unreadable, null);

            int version = root["version"]?.GetValue<int>() ?? ImageRecipe.CurrentVersion;
            if (version > ImageRecipe.CurrentVersion) return new(RecipeReadStatus.Newer, null);

            var recipe = root.Deserialize<ImageRecipe>(Options);
            return recipe is null
                ? new(RecipeReadStatus.Unreadable, null)
                : new(RecipeReadStatus.Read, recipe);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or NotSupportedException)
        {
            return new(RecipeReadStatus.Unreadable, null);
        }
    }
}

