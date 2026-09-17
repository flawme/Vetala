using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Vetala.Models;

namespace Vetala.Services;

public static class GitService
{
    /// <summary>
    /// Returns the current branch name read directly from .git/HEAD,
    /// or null when the folder is not a git repository.
    /// </summary>
    public static string? GetBranch(string projectPath)
    {
        try
        {
            if (string.IsNullOrEmpty(projectPath)) return null;

            var gitPath = Path.Combine(projectPath, ".git");

            // Worktrees/submodules store a pointer file instead of the directory itself
            if (File.Exists(gitPath))
            {
                var pointer = File.ReadAllText(gitPath).Trim();
                if (!pointer.StartsWith("gitdir:", StringComparison.Ordinal)) return null;
                gitPath = pointer["gitdir:".Length..].Trim();
            }

            if (!Directory.Exists(gitPath)) return null;

            var headFile = Path.Combine(gitPath, "HEAD");
            if (!File.Exists(headFile)) return null;

            var head = File.ReadAllText(headFile).Trim();
            const string refPrefix = "ref: refs/heads/";
            if (head.StartsWith(refPrefix, StringComparison.Ordinal))
                return head[refPrefix.Length..];

            // Detached HEAD: show a short hash
            return head.Length > 7 ? head[..7] : head;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns changed files (staged, unstaged and untracked) via `git status --porcelain`.
    /// Empty when the folder is not a repository or the git binary is unavailable.
    /// </summary>
    public static List<GitStatusEntry> GetChangedFiles(string projectPath)
    {
        var entries = new List<GitStatusEntry>();
        if (string.IsNullOrEmpty(projectPath)) return entries;

        var root = RunGit(projectPath, "rev-parse", "--show-toplevel")?.TrimEnd('\r', '\n');
        var status = RunGit(projectPath, "status", "--porcelain");
        if (status == null) return entries;

        foreach (var rawLine in status.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length < 4) continue;

            var xy = line[..2];
            var path = line[3..];
            if (string.IsNullOrEmpty(path)) continue;

            // Renames/copies are reported as "old -> new": keep the new path
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
                path = path[(arrow + 4)..];

            var code = Classify(xy);

            string absolute;
            try
            {
                var basePath = string.IsNullOrEmpty(root) ? projectPath : root;
                absolute = Path.GetFullPath(Path.Combine(basePath, path));
            }
            catch
            {
                continue;
            }

            entries.Add(new GitStatusEntry(absolute, path.Replace('\\', '/'), code, Describe(code)));
        }

        return entries;
    }

    private static char Classify(string xy)
    {
        if (xy == "??") return 'U';

        var x = xy[0];
        var y = xy[1];

        // Prefer the working-tree status, then the index status
        if (y != ' ' && y != '?') return y;
        if (x != ' ' && x != '?') return x;
        return 'M';
    }

    private static string Describe(char code) => code switch
    {
        'U' => "Untracked",
        'A' => "Added",
        'D' => "Deleted",
        'R' => "Renamed",
        'C' => "Copied",
        'T' => "Type changed",
        _ => "Modified"
    };

    private static string? RunGit(string workingDirectory, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd(); // drain stderr to avoid blocking
            if (!process.WaitForExit(5000)) return null;
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
