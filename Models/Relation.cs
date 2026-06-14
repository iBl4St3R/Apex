using System.Text.Json.Serialization;

namespace Apex.Models;

/// <summary>
/// Represents a directed relation (arrow) between two board elements.
/// Source and target are identified by type + id/path.
/// </summary>
public class Relation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = string.Empty; // "note" | "title" | "image"

    [JsonPropertyName("sourceRef")]
    public string SourceRef { get; set; } = string.Empty; // relativePath or card id

    [JsonPropertyName("targetType")]
    public string TargetType { get; set; } = string.Empty;

    [JsonPropertyName("targetRef")]
    public string TargetRef { get; set; } = string.Empty;

    /// <summary>
    /// Control point offset from the midpoint of the straight line.
    /// Allows bending the arrow by dragging the midpoint handle.
    /// </summary>
    [JsonPropertyName("bendX")]
    public double BendX { get; set; } = 0;

    [JsonPropertyName("bendY")]
    public double BendY { get; set; } = 0;

    public Relation() { }

    public Relation(string id,
        string sourceType, string sourceRef,
        string targetType, string targetRef)
    {
        Id = id;
        SourceType = sourceType;
        SourceRef = sourceRef;
        TargetType = targetType;
        TargetRef = targetRef;
    }

    public bool IsSameAs(Relation other) =>
        string.Equals(SourceType, other.SourceType, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(SourceRef,  other.SourceRef,  StringComparison.OrdinalIgnoreCase) &&
        string.Equals(TargetType, other.TargetType, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(TargetRef,  other.TargetRef,  StringComparison.OrdinalIgnoreCase);
}