using System.Collections.Generic;

namespace Vetala.Models;

public sealed record SearchMatch(string FilePath, int Line, int Column, string Preview);

public sealed record FileSearchResult(string FilePath, string DisplayPath, IReadOnlyList<SearchMatch> Matches)
{
    public int MatchCount => Matches.Count;
}
