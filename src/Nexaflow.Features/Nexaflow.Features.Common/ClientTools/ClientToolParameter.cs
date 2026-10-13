namespace Nexaflow.Features.Common.ClientTools;

/// <summary>One named argument a client tool accepts, described for the model.</summary>
/// <param name="Name">Argument key the model places inside the tool's <c>arguments</c> object.</param>
/// <param name="Description">What the argument means / how to fill it.</param>
/// <param name="Required">Whether the tool needs this argument to run.</param>
/// <param name="Type">Its JSON Schema type (string, number, boolean, array, object).</param>
/// <param name="ItemType">For an array: the JSON Schema type of each item. An array declared without one takes items
/// of any kind.</param>
public sealed record ClientToolParameter(
    string Name,
    string Description,
    bool Required = true,
    string Type = "string",
    string? ItemType = null);
