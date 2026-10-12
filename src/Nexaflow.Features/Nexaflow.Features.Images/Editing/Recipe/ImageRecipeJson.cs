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
public static class ImageRecipeJson
{
    public static string Serialize(ImageRecipe recipe) =>
        JsonSerializer.Serialize(recipe, ImageRecipeJsonContext.Default.ImageRecipe);

    public static RecipeReadResult Deserialize(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return new(RecipeReadStatus.Unreadable, null);

            int version = root["version"]?.GetValue<int>() ?? ImageRecipe.CurrentVersion;
            if (version > ImageRecipe.CurrentVersion) return new(RecipeReadStatus.Newer, null);

            var recipe = root.Deserialize(ImageRecipeJsonContext.Default.ImageRecipe);
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

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ImageRecipe))]
internal sealed partial class ImageRecipeJsonContext : JsonSerializerContext;
