using System.Text.Json.Serialization;

namespace Apex.Models;

/// <summary>
/// Metadata for a note template stored in .templates/ folder.
/// The actual content lives in the .md file; this is the sidecar JSON.
/// </summary>
public class NoteTemplate
{
    [JsonPropertyName("templateName")]
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>Relative path of the .md file inside .templates/</summary>
    [JsonPropertyName("mdFileName")]
    public string MdFileName { get; set; } = string.Empty;

    /// <summary>Category ID to auto-assign to notes created from this template.</summary>
    [JsonPropertyName("defaultCategoryId")]
    public string? DefaultCategoryId { get; set; }

    /// <summary>
    /// Project-relative folder where new notes will be placed.
    /// Empty = root folder. If folder doesn't exist, root is used.
    /// </summary>
    [JsonPropertyName("defaultFolder")]
    public string DefaultFolder { get; set; } = string.Empty;

    public NoteTemplate() { }

    public NoteTemplate(string templateName, string mdFileName)
    {
        TemplateName = templateName;
        MdFileName = mdFileName;
    }
}