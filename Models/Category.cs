using System.Text.Json.Serialization;

namespace Apex.Models;

/// <summary>
/// Represents a user-defined category that can be assigned to notes.
/// </summary>
public class Category
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#888888";

    public Category() { }

    public Category(string id, string name, string color)
    {
        Id = id;
        Name = name;
        Color = color;
    }
}