using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;

namespace Vetala.Models;

public partial class EditorTab : ObservableObject
{
    [ObservableProperty]
    private string _filePath;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private TextDocument _document;

    [ObservableProperty]
    private IHighlightingDefinition? _syntaxHighlighting;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasLoadError;

    [ObservableProperty]
    private string? _loadError;

    public EditorTab(string filePath, string content)
    {
        _filePath = filePath;
        _title = Path.GetFileName(filePath);
        _document = new TextDocument(content);
        _document.TextChanged += (s, e) => IsDirty = true;
        UpdateSyntaxHighlighting();
    }

    public EditorTab(string filePath, string? errorMessage, bool isError)
    {
        _filePath = filePath;
        _title = Path.GetFileName(filePath);
        _document = new TextDocument(errorMessage ?? string.Empty);
        _hasLoadError = isError;
        _loadError = errorMessage;
        _isReadOnly = true;
    }

    public void UpdateSyntaxHighlighting()
    {
        string ext = Path.GetExtension(FilePath);
        SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);
    }

    partial void OnFilePathChanged(string value)
    {
        Title = Path.GetFileName(value);
        UpdateSyntaxHighlighting();
    }
}
