using System.Text.Json.Serialization;

namespace Apex.Models;

/// <summary>
/// Represents a note card on the board canvas.
/// Tracks position, assigned category, and the relative path to the .md file.
/// </summary>
public class NoteCard
{
    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("boardX")]
    public double BoardX { get; set; }

    [JsonPropertyName("boardY")]
    public double BoardY { get; set; }

    [JsonPropertyName("categoryId")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("cardSize")] public string CardSize { get; set; } = "minimum";

    [JsonPropertyName("customWidth")]
    public double? CustomWidth { get; set; }

    [JsonPropertyName("customHeight")]
    public double? CustomHeight { get; set; }

    public NoteCard() { }

    public NoteCard(string relativePath, double boardX, double boardY, string? categoryId = null)
    {
        RelativePath = relativePath;
        BoardX = boardX;
        BoardY = boardY;
        CategoryId = categoryId;
    }
}