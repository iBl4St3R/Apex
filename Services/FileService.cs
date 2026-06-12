using System.IO;
using System.Text.Json;
using Apex.Models;

namespace Apex.Services;

/// <summary>
/// Handles all file-system operations for Apex projects.
/// Manages .apex project files, .md note files, and recent-projects tracking.
/// </summary>
public static class FileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string RecentProjectsPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Apex",
            "recent.json");

    // ──────────────────────────────────────────────
    //  .apex project file operations
    // ──────────────────────────────────────────────

    /// <summary>
    /// Creates a new .apex project file in the specified folder.
    /// The folder must exist and must not already contain an .apex file.
    /// </summary>
    public static ApexProject CreateNewProject(string folderPath, string projectName)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

        string apexFilePath = GetApexFilePath(folderPath);
        if (File.Exists(apexFilePath))
            throw new InvalidOperationException($"An .apex file already exists in: {folderPath}");

        var project = new ApexProject(projectName, folderPath);

        string json = JsonSerializer.Serialize(project, JsonOptions);
        File.WriteAllText(apexFilePath, json);

        AddRecentProject(apexFilePath, projectName);
        return project;
    }

    /// <summary>
    /// Loads an .apex project from the given file path.
    /// Returns null if the file is missing or malformed.
    /// </summary>
    public static ApexProject? LoadProject(string apexFilePath)
    {
        if (!File.Exists(apexFilePath))
            return null;

        try
        {
            string json = File.ReadAllText(apexFilePath);
            var project = JsonSerializer.Deserialize<ApexProject>(json, JsonOptions);

            if (project == null || string.IsNullOrEmpty(project.ProjectName))
                return null;

            // Ensure the root folder matches the actual file location
            string actualRoot = Path.GetDirectoryName(Path.GetFullPath(apexFilePath))!;
            project.RootFolder = actualRoot;

            AddRecentProject(apexFilePath, project.ProjectName);
            return project;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the project state back to the .apex file on disk.
    /// Call this immediately whenever card positions or categories change.
    /// </summary>
    public static void SaveProject(ApexProject project)
    {
        if (string.IsNullOrEmpty(project.RootFolder))
            throw new InvalidOperationException("Project root folder is not set.");

        string apexFilePath = GetApexFilePath(project.RootFolder);
        string json = JsonSerializer.Serialize(project, JsonOptions);
        File.WriteAllText(apexFilePath, json);
    }

    /// <summary>
    /// Finds an .apex file in the given folder, or null if none exists.
    /// </summary>
    public static string? FindApexFile(string folderPath)
    {
        string path = GetApexFilePath(folderPath);
        return File.Exists(path) ? path : null;
    }

    // ──────────────────────────────────────────────
    //  .md note file operations
    // ──────────────────────────────────────────────

    /// <summary>
    /// Creates a new Markdown note file at the given relative path.
    /// Returns the full file path of the created file.
    /// </summary>
    public static string CreateNoteFile(ApexProject project, string relativePath)
    {
        if (string.IsNullOrEmpty(project.RootFolder))
            throw new InvalidOperationException("Project root folder is not set.");

        string fullPath = GetFullPath(project.RootFolder, relativePath);

        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(fullPath))
            throw new InvalidOperationException($"Note already exists: {relativePath}");

        string title = Path.GetFileNameWithoutExtension(relativePath);
        string content = $"# {title}\n\n";
        File.WriteAllText(fullPath, content);

        return fullPath;
    }

    /// <summary>
    /// Renames a Markdown note file from oldRelativePath to newRelativePath.
    /// Also updates the card's relative path if the card exists in the project.
    /// </summary>
    public static void RenameNoteFile(ApexProject project, string oldRelativePath, string newRelativePath)
    {
        if (string.IsNullOrEmpty(project.RootFolder))
            throw new InvalidOperationException("Project root folder is not set.");

        string oldFullPath = GetFullPath(project.RootFolder, oldRelativePath);
        string newFullPath = GetFullPath(project.RootFolder, newRelativePath);

        if (!File.Exists(oldFullPath))
            throw new FileNotFoundException($"Note not found: {oldRelativePath}");

        if (File.Exists(newFullPath))
            throw new InvalidOperationException($"A note already exists at: {newRelativePath}");

        // Ensure target directory exists (in case the rename also changes the folder)
        string? newDirectory = Path.GetDirectoryName(newFullPath);
        if (!string.IsNullOrEmpty(newDirectory) && !Directory.Exists(newDirectory))
            Directory.CreateDirectory(newDirectory);

        File.Move(oldFullPath, newFullPath);

        // Update the card reference in the project
        var card = project.Cards.FirstOrDefault(c =>
            string.Equals(c.RelativePath, oldRelativePath, StringComparison.OrdinalIgnoreCase));
        if (card != null)
        {
            card.RelativePath = newRelativePath;
            SaveProject(project);
        }
    }

    /// <summary>
    /// Moves a Markdown note file to the Windows Recycle Bin.
    /// Throws if the file does not exist.
    /// </summary>
    public static void DeleteNoteFile(ApexProject project, string relativePath)
    {
        if (string.IsNullOrEmpty(project.RootFolder))
            throw new InvalidOperationException("Project root folder is not set.");

        string fullPath = GetFullPath(project.RootFolder, relativePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Note not found: {relativePath}");

        // Move to Recycle Bin using Microsoft.VisualBasic interop
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
            fullPath,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

        // Remove the card from the project
        project.Cards.RemoveAll(c =>
            string.Equals(c.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
        SaveProject(project);
    }

    /// <summary>
    /// Creates a subfolder within the project root.
    /// </summary>
    public static string CreateFolder(ApexProject project, string relativePath)
    {
        if (string.IsNullOrEmpty(project.RootFolder))
            throw new InvalidOperationException("Project root folder is not set.");

        string fullPath = GetFullPath(project.RootFolder, relativePath);

        if (Directory.Exists(fullPath))
            throw new InvalidOperationException($"Folder already exists: {relativePath}");

        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    // ──────────────────────────────────────────────
    //  Recent projects
    // ──────────────────────────────────────────────

    /// <summary>
    /// Loads the list of recently opened projects.
    /// Filters out paths that no longer exist on disk.
    /// </summary>
    public static List<RecentProjectEntry> LoadRecentProjects()
    {
        try
        {
            if (!File.Exists(RecentProjectsPath))
                return new List<RecentProjectEntry>();

            string json = File.ReadAllText(RecentProjectsPath);
            var entries = JsonSerializer.Deserialize<List<RecentProjectEntry>>(json, JsonOptions);

            if (entries == null)
                return new List<RecentProjectEntry>();

            // Remove entries whose .apex file no longer exists, and deduplicate by path
            return entries
                .Where(e => File.Exists(e.Path))
                .GroupBy(e => e.Path)
                .Select(g => g.First())
                .OrderByDescending(e => e.LastOpened)
                .Take(10)
                .ToList();
        }
        catch
        {
            return new List<RecentProjectEntry>();
        }
    }

    /// <summary>
    /// Adds or updates a project in the recent projects list.
    /// </summary>
    public static void AddRecentProject(string apexFilePath, string projectName)
    {
        try
        {
            var entries = LoadRecentProjects();

            // Remove existing entry for this path (if any)
            entries.RemoveAll(e =>
                string.Equals(e.Path, apexFilePath, StringComparison.OrdinalIgnoreCase));

            // Add to the top
            entries.Insert(0, new RecentProjectEntry
            {
                Path = apexFilePath,
                Name = projectName,
                LastOpened = DateTime.UtcNow
            });

            // Keep at most 10
            if (entries.Count > 10)
                entries = entries.Take(10).ToList();

            // Ensure directory exists
            string? dir = Path.GetDirectoryName(RecentProjectsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(entries, JsonOptions);
            File.WriteAllText(RecentProjectsPath, json);
        }
        catch
        {
            // Silently fail — recent projects are a convenience, not critical data
        }
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Gets the path to the .apex file within a project folder.
    /// </summary>
    public static string GetApexFilePath(string rootFolder)
    {
        return Path.Combine(rootFolder, ".apex");
    }

    /// <summary>
    /// Resolves a project-relative path to an absolute filesystem path.
    /// </summary>
    public static string GetFullPath(string rootFolder, string relativePath)
    {
        // Normalize separators
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(rootFolder, normalized));
    }

    /// <summary>
    /// Computes a project-relative path from an absolute file path.
    /// </summary>
    public static string GetRelativePath(string rootFolder, string fullPath)
    {
        string normalizedRoot = Path.GetFullPath(rootFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedFull = Path.GetFullPath(fullPath);
        return normalizedFull.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? normalizedFull[normalizedRoot.Length..]
            : fullPath;
    }
}

/// <summary>
/// Lightweight model for the recent projects list stored in AppData.
/// </summary>
public class RecentProjectEntry
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime LastOpened { get; set; }
}