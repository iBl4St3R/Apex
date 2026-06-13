using System.IO;
using System.Text.RegularExpressions;
using Apex.Models;

namespace Apex.Services;

/// <summary>
/// Parses [[wiki-links]] from .md files and resolves them to NoteCards.
/// Handles ambiguous links (same filename in different folders).
/// </summary>
public static class ConnectionResolver
{
    private static readonly Regex WikiLink =
        new(@"\[\[([^\]]+)\]\]", RegexOptions.Compiled);

    /// <summary>
    /// Returns all (sourceCard, targetCard) pairs for the project.
    /// Ambiguous links (multiple matches) are skipped — user must resolve via selector.
    /// </summary>
    public static List<(NoteCard Source, NoteCard Target)> ResolveAll(ApexProject project)
    {
        var result = new List<(NoteCard, NoteCard)>();
        if (string.IsNullOrEmpty(project.RootFolder)) return result;

        // Build lookup: filename (no ext, lower) → list of cards
        var lookup = BuildLookup(project);

        foreach (var sourceCard in project.Cards)
        {
            string fullPath = FileService.GetFullPath(project.RootFolder, sourceCard.RelativePath);
            if (!File.Exists(fullPath)) continue;

            foreach (string linkTarget in ParseLinks(fullPath))
            {
                string key = linkTarget.ToLowerInvariant();
                if (!lookup.TryGetValue(key, out var candidates)) continue;

                // Unambiguous — draw the line
                if (candidates.Count == 1)
                    result.Add((sourceCard, candidates[0]));

                // Ambiguous (same name in different folders) — skip for now,
                // resolved interactively via WikiLinkSelector in edit mode
            }
        }

        return result;
    }

    /// <summary>
    /// Finds all cards whose filename matches the given link target.
    /// Returns empty list if none, multiple if ambiguous.
    /// </summary>
    public static List<NoteCard> FindCandidates(ApexProject project, string linkTarget)
    {
        var lookup = BuildLookup(project);
        string key = linkTarget.ToLowerInvariant();
        return lookup.TryGetValue(key, out var list) ? list : new List<NoteCard>();
    }

    public static IEnumerable<string> ParseLinks(string fullPath)
    {
        try
        {
            // Read only first 32KB — links are usually near the top
            const int readBytes = 32768;
            byte[] buffer = new byte[readBytes];
            int bytesRead;
            using (var fs = new FileStream(fullPath, FileMode.Open,
                       FileAccess.Read, FileShare.ReadWrite))
                bytesRead = fs.Read(buffer, 0, readBytes);

            string content = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
            return WikiLink.Matches(content)
                .Select(m => m.Groups[1].Value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return Enumerable.Empty<string>(); }
    }

    private static Dictionary<string, List<NoteCard>> BuildLookup(ApexProject project)
    {
        var lookup = new Dictionary<string, List<NoteCard>>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in project.Cards)
        {
            string name = Path.GetFileNameWithoutExtension(card.RelativePath).ToLowerInvariant();
            if (!lookup.TryGetValue(name, out var list))
            {
                list = new List<NoteCard>();
                lookup[name] = list;
            }
            list.Add(card);
        }
        return lookup;
    }
}