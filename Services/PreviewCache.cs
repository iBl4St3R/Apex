using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;

namespace Apex.Services;

/// <summary>
/// Thread-safe cache for card preview text.
/// Stores the first N characters of plain text extracted from .md files.
/// Invalidated per-file by FileSystemWatcher events.
/// </summary>
public sealed class PreviewCache : IDisposable
{
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private const int MaxPreviewChars = 10000;

    public void StartWatching(string rootFolder)
    {
        StopWatching();

        if (!Directory.Exists(rootFolder)) return;

        _watcher = new FileSystemWatcher(rootFolder, "*.md")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        _watcher.Changed += (_, e) => _cache.TryRemove(e.FullPath, out string? _);
        _watcher.Renamed += (_, e) =>
        {
            _cache.TryRemove(e.OldFullPath, out string? _);
            _cache.TryRemove(e.FullPath, out string? _);
        };
        _watcher.Deleted += (_, e) => _cache.TryRemove(e.FullPath, out string? _);
    }

    public void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    /// <summary>
    /// Returns cached preview text for the given full path.
    /// Reads from disk only on first access or after invalidation.
    /// </summary>
    public string GetPreview(string fullPath, int maxChars)
    {
        // Clamp to our internal max so we never cache more than needed
        int chars = Math.Min(maxChars, MaxPreviewChars);

        if (_cache.TryGetValue(fullPath, out string? cached))
            return cached.Length <= chars ? cached : cached[..chars] + "…";

        string preview = ReadPreview(fullPath, MaxPreviewChars);
        _cache[fullPath] = preview;

        return preview.Length <= chars ? preview : preview[..chars] + "…";
    }

    public void Invalidate(string fullPath) => _cache.TryRemove(fullPath, out string? _);

    public void Clear() => _cache.Clear();

    private static string ReadPreview(string fullPath, int maxChars)
    {
        try
        {
            const int readBytes = 65536;
            byte[] buffer = new byte[readBytes];
            int bytesRead;
            using (var fs = new FileStream(fullPath, FileMode.Open,
                       FileAccess.Read, FileShare.ReadWrite))
                bytesRead = fs.Read(buffer, 0, readBytes);

            string raw = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

            // Nie usuwamy code blocków — BuildPreviewElement obsługuje je jako ramki
            string plain = raw;

            plain = System.Text.RegularExpressions.Regex.Replace(
                plain, @"\[\[([^\]]+)\]\]", "apex_link§$1§");
            plain = System.Text.RegularExpressions.Regex.Replace(
                plain, @"\[([^\]]+)\]\([^)]+\)", "$1");
            plain = System.Text.RegularExpressions.Regex.Replace(
                plain, @"\n{3,}", "\n\n");
            plain = plain.Trim();

            return plain.Length <= maxChars ? plain : plain[..maxChars] + "…";
        }
        catch { return ""; }
    }

    public void Dispose() => StopWatching();
}