using System.Text.Json.Serialization;

namespace Apex.Models;

public class TitleCard
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("boardX")]
    public double BoardX { get; set; }

    [JsonPropertyName("boardY")]
    public double BoardY { get; set; }

    [JsonPropertyName("locked")]
    public bool Locked { get; set; } = false;

    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Segoe UI";

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 24;

    [JsonPropertyName("fontColor")]
    public string FontColor { get; set; } = "#CDD6F4";

    [JsonPropertyName("bold")]
    public bool Bold { get; set; } = false;

    [JsonPropertyName("italic")]
    public bool Italic { get; set; } = false;

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = "#00000000"; // przezroczyste domyślnie

    [JsonPropertyName("customWidth")]
    public double? CustomWidth { get; set; }

    [JsonPropertyName("customHeight")]
    public double? CustomHeight { get; set; }

    public TitleCard() { }

    public TitleCard(string id, string text, double boardX, double boardY)
    {
        Id = id;
        Text = text;
        BoardX = boardX;
        BoardY = boardY;
    }
}