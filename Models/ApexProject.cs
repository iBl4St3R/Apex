using System.Text.Json.Serialization;

namespace Apex.Models;

/// <summary>
/// Top-level data model for the .apex project file.
/// Stores project metadata, user categories, and card positions.
/// Note content is stored exclusively in .md files on disk.
/// </summary>
public class ApexProject
{
    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; } = string.Empty;

    [JsonPropertyName("rootFolder")]
    public string RootFolder { get; set; } = string.Empty;

    [JsonPropertyName("lastView")]
    public string LastView { get; set; } = "board";

    [JsonPropertyName("lastZoom")]
    public double LastZoom { get; set; } = 1.0;

    [JsonPropertyName("themeOverride")]
    public string ThemeOverride { get; set; } = "system";

    [JsonPropertyName("categories")]
    public List<Category> Categories { get; set; } = new();

    [JsonPropertyName("cards")]
    public List<NoteCard> Cards { get; set; } = new();

    [JsonPropertyName("imageCards")]
    public List<ImageCard> ImageCards { get; set; } = new();

    [JsonPropertyName("titleCards")]
    public List<TitleCard> TitleCards { get; set; } = new();

    public ApexProject() { }

    public ApexProject(string projectName, string rootFolder)
    {
        ProjectName = projectName;
        RootFolder = rootFolder;
    }
}