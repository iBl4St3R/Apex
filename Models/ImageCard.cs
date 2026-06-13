using System.Text.Json.Serialization;

namespace Apex.Models;

public class ImageCard
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("relativePath")]  // względem rootFolder, np. ".images/logo.png"
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("boardX")]
    public double BoardX { get; set; }

    [JsonPropertyName("boardY")]
    public double BoardY { get; set; }

    [JsonPropertyName("categoryId")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("locked")]
    public bool Locked { get; set; } = false;

    [JsonPropertyName("customWidth")]
    public double? CustomWidth { get; set; }

    [JsonPropertyName("customHeight")]
    public double? CustomHeight { get; set; }

    public ImageCard() { }

    public ImageCard(string id, string relativePath, double boardX, double boardY)
    {
        Id = id;
        RelativePath = relativePath;
        BoardX = boardX;
        BoardY = boardY;
    }
}