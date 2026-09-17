using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vetala.Models;
using Vetala.Services;

namespace Vetala.ViewModels;

public partial class SearchViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private bool _matchCase;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _summary = string.Empty;

    public ObservableCollection<FileSearchResult> ResultFiles { get; } = new();

    /// <summary>Provided by the main view model: opens a file and jumps to a line.</summary>
    public Func<string, int, Task>? OpenResultRequested;

    /// <summary>Provided by the main view model: returns the currently opened project root.</summary>
    public Func<string?>? GetProjectRoot;

    private CancellationTokenSource? _searchCts;

    [RelayCommand]
    private async Task Search()
    {
        var root = GetProjectRoot?.Invoke();
        var query = Query;

        if (string.IsNullOrEmpty(root) || string.IsNullOrWhiteSpace(query))
        {
            ResultFiles.Clear();
            Summary = string.Empty;
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        IsSearching = true;
        Summary = "Searching...";

        try
        {
            var matchCase = MatchCase;
            var results = await Task.Run(() => SearchService.Search(root!, query, matchCase, ct), ct);

            ResultFiles.Clear();
            int matchCount = 0;
            foreach (var result in results)
            {
                ResultFiles.Add(result);
                matchCount += result.Matches.Count;
            }

            Summary = results.Count == 0
                ? "No results"
                : $"{matchCount} result{(matchCount == 1 ? "" : "s")} in {results.Count} file{(results.Count == 1 ? "" : "s")}";
        }
        catch (OperationCanceledException)
        {
            // A newer search replaced this one
        }
        catch (Exception ex)
        {
            Summary = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _searchCts?.Cancel();
        Query = string.Empty;
        ResultFiles.Clear();
        Summary = string.Empty;
        IsSearching = false;
    }

    public async Task OpenResult(SearchMatch match)
    {
        if (OpenResultRequested != null)
            await OpenResultRequested(match.FilePath, match.Line);
    }
}
