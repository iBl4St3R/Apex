using System.IO;
using System.Text.Json;
using Apex.Models;

namespace Apex.Services;

/// <summary>
/// Manages note templates stored in {rootFolder}/.templates/
/// Each template = one .md file + one .json sidecar (same base name).
/// </summary>
public static class TemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string GetTemplatesFolder(string rootFolder) =>
        Path.Combine(rootFolder, ".templates");

    private static string GetSidecarPath(string rootFolder, string mdFileName) =>
        Path.Combine(GetTemplatesFolder(rootFolder),
            Path.GetFileNameWithoutExtension(mdFileName) + ".json");

    // ──────────────────────────────────────────────
    //  Load all templates
    // ──────────────────────────────────────────────

    public static List<NoteTemplate> LoadAll(string rootFolder)
    {
        string folder = GetTemplatesFolder(rootFolder);
        if (!Directory.Exists(folder)) return new();

        var result = new List<NoteTemplate>();
        foreach (string mdPath in Directory.EnumerateFiles(folder, "*.md")
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileName(mdPath);
            var template = LoadMeta(rootFolder, fileName)
                           ?? new NoteTemplate(
                               Path.GetFileNameWithoutExtension(fileName),
                               fileName);
            result.Add(template);
        }
        return result;
    }

    // ──────────────────────────────────────────────
    //  Load / Save metadata sidecar
    // ──────────────────────────────────────────────

    public static NoteTemplate? LoadMeta(string rootFolder, string mdFileName)
    {
        string path = GetSidecarPath(rootFolder, mdFileName);
        if (!File.Exists(path)) return null;
        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<NoteTemplate>(json, JsonOptions);
        }
        catch { return null; }
    }

    public static void SaveMeta(string rootFolder, NoteTemplate template)
    {
        EnsureFolder(rootFolder);
        string path = GetSidecarPath(rootFolder, template.MdFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(template, JsonOptions));
    }

    // ──────────────────────────────────────────────
    //  Create template from existing note
    // ──────────────────────────────────────────────

    /// <summary>
    /// Copies a note's .md content into .templates/ and creates an empty sidecar.
    /// Returns the new NoteTemplate, or null on failure.
    /// </summary>
    public static NoteTemplate? CreateFromNote(
        string rootFolder,
        string sourceFullPath,
        string templateName)
    {
        EnsureFolder(rootFolder);
        string folder = GetTemplatesFolder(rootFolder);

        // Sanitize file name
        string safe = string.Concat(templateName.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (string.IsNullOrWhiteSpace(safe)) safe = "template";

        string mdFileName = safe + ".md";
        string destPath = Path.Combine(folder, mdFileName);

        // Avoid overwriting — append number
        int n = 1;
        while (File.Exists(destPath))
        {
            mdFileName = safe + n + ".md";
            destPath = Path.Combine(folder, mdFileName);
            n++;
        }

        File.Copy(sourceFullPath, destPath);

        var template = new NoteTemplate(templateName, mdFileName);
        SaveMeta(rootFolder, template);
        return template;
    }

    // ──────────────────────────────────────────────
    //  Get full path of template .md
    // ──────────────────────────────────────────────

    public static string GetMdFullPath(string rootFolder, string mdFileName) =>
        Path.Combine(GetTemplatesFolder(rootFolder), mdFileName);

    // ──────────────────────────────────────────────
    //  Read content
    // ──────────────────────────────────────────────

    public static string ReadContent(string rootFolder, NoteTemplate template)
    {
        string path = GetMdFullPath(rootFolder, template.MdFileName);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    // ──────────────────────────────────────────────
    //  Resolve target folder
    // ──────────────────────────────────────────────

    /// <summary>
    /// Returns the absolute folder path where a note from this template should land.
    /// Falls back to rootFolder if the configured folder doesn't exist.
    /// </summary>
    public static string ResolveTargetFolder(string rootFolder, NoteTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.DefaultFolder))
            return rootFolder;

        string full = Path.GetFullPath(
            Path.Combine(rootFolder, template.DefaultFolder.Replace('/', Path.DirectorySeparatorChar)));

        return Directory.Exists(full) ? full : rootFolder;
    }

    // ──────────────────────────────────────────────
    //  Delete template
    // ──────────────────────────────────────────────

    public static void Delete(string rootFolder, NoteTemplate template)
    {
        string mdPath = GetMdFullPath(rootFolder, template.MdFileName);
        string sidecar = GetSidecarPath(rootFolder, template.MdFileName);
        if (File.Exists(mdPath)) File.Delete(mdPath);
        if (File.Exists(sidecar)) File.Delete(sidecar);
    }

    // ──────────────────────────────────────────────
    //  Helper
    // ──────────────────────────────────────────────

    public static void EnsureFolder(string rootFolder)
    {
        string folder = GetTemplatesFolder(rootFolder);
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
    }
}