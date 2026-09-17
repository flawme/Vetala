using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Vetala.Models;

namespace Vetala.Services;

public static class SearchService
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".svn", ".hg",
        "bin", "obj", "node_modules", "packages", "dist", "build", "target", "vendor"
    };

    private const int MaxMatches = 10000;
    private const int MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Searches every text file under <paramref name="rootPath"/> for <paramref name="query"/>.
    /// Skips common generated/VCS directories, binary files and files larger than 2 MB.
    /// </summary>
    public static List<FileSearchResult> Search(string rootPath, string query, bool matchCase, CancellationToken ct)
    {
        var results = new List<FileSearchResult>();
        if (string.IsNullOrEmpty(rootPath) || string.IsNullOrWhiteSpace(query)) return results;

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int totalMatches = 0;

        foreach (var file in EnumerateFiles(rootPath))
        {
            ct.ThrowIfCancellationRequested();
            if (totalMatches >= MaxMatches) break;

            List<SearchMatch>? matches = null;
            try
            {
                var fileInfo = new FileInfo(file);
                if (!fileInfo.Exists || fileInfo.Length > MaxFileBytes || LooksBinary(file)) continue;

                int lineNo = 0;
                using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNo++;
                    int idx = line.IndexOf(query, comparison);
                    while (idx >= 0)
                    {
                        matches ??= new List<SearchMatch>();
                        matches.Add(new SearchMatch(file, lineNo, idx + 1, TrimPreview(line)));
                        totalMatches++;
                        if (totalMatches >= MaxMatches) break;
                        idx = line.IndexOf(query, idx + query.Length, comparison);
                    }
                    if (totalMatches >= MaxMatches) break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }

            if (matches is { Count: > 0 })
                results.Add(new FileSearchResult(file, GetDisplayPath(rootPath, file), matches));
        }

        return results;
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            string[] subDirs = Array.Empty<string>();
            try { subDirs = Directory.GetDirectories(dir); } catch { }
            foreach (var sub in subDirs)
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(sub)))
                    pending.Push(sub);
            }

            string[] files = Array.Empty<string>();
            try { files = Directory.GetFiles(dir); } catch { }
            foreach (var f in files)
                yield return f;
        }
    }

    private static bool LooksBinary(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            var buffer = new byte[8192];
            int read = stream.Read(buffer, 0, buffer.Length);
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == 0) return true;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static string TrimPreview(string line)
    {
        var t = line.Trim();
        return t.Length > 200 ? t[..200] : t;
    }

    private static string GetDisplayPath(string root, string file)
    {
        try
        {
            var rel = Path.GetRelativePath(root, file);
            return rel.Replace(Path.DirectorySeparatorChar, '/');
        }
        catch
        {
            return file;
        }
    }
}
