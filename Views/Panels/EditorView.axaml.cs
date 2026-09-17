using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Search;
using Vetala.Models;
using Vetala.Services;
using Vetala.ViewModels;

namespace Vetala.Views;

public partial class EditorView : UserControl
{
    private TextEditor? _currentEditor;
    private Dictionary<TextEditor, FoldingManager> _foldingManagers = new();

    public EditorView()
    {
        InitializeComponent();
    }

    private void OnTextEditorAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextEditor editor) return;

        _currentEditor = editor;

        editor.Options = new TextEditorOptions
        {
            ConvertTabsToSpaces = true,
            IndentationSize = 4,
            EnableHyperlinks = false,
            EnableEmailHyperlinks = false,
            EnableVirtualSpace = false,
            HighlightCurrentLine = true,
            EnableRectangularSelection = true
        };

        editor.TextArea.Caret.CaretBrush = new SolidColorBrush(Color.Parse("#D4D4D4"));
        editor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(110, 38, 121, 194));
        editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.Parse("#15191D"));
        editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.Parse("#20262C")), 1);

        SearchPanel.Install(editor);

        editor.TextArea.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F, KeyModifiers.Control), Command = new SearchCommand(editor) });
        editor.TextArea.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.H, KeyModifiers.Control), Command = new ReplaceCommand(editor) });
        editor.TextArea.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.G, KeyModifiers.Control), Command = new GoToLineCommand(editor) });
        editor.TextArea.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.D, KeyModifiers.Control), Command = new SelectNextOccurrenceCommand(editor) });
        editor.TextArea.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Space, KeyModifiers.Control), Command = new ShowCompletionCommand(editor) });

        editor.TextArea.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Z, KeyModifiers.Control), Command = new UndoCommand(editor) });
        editor.TextArea.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Y, KeyModifiers.Control), Command = new RedoCommand(editor) });
        editor.TextArea.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Z, KeyModifiers.Control | KeyModifiers.Shift), Command = new RedoCommand(editor) });

        if (DataContext is EditorViewModel vm)
        {
            var searchCmd = new SearchCommand(editor);
            var replaceCmd = new ReplaceCommand(editor);
            var goToCmd = new GoToLineCommand(editor);
            vm.TriggerFind = () => searchCmd.Execute(null);
            vm.TriggerReplace = () => replaceCmd.Execute(null);
            vm.TriggerGoToLine = () => goToCmd.Execute(null);

            vm.NavigateToLine = line =>
            {
                if (editor.Document == vm.ActiveTab?.Document)
                    ApplyLineNavigation(editor, line);
                else
                    vm.PendingNavigationLine = line;
            };

            editor.TextArea.Caret.PositionChanged += (s, e) =>
            {
                if (editor.Document == vm.ActiveTab?.Document)
                {
                    vm.CaretLine = editor.TextArea.Caret.Line;
                    vm.CaretColumn = editor.TextArea.Caret.Column;
                }
            };

            vm.CaretLine = editor.TextArea.Caret.Line;
            vm.CaretColumn = editor.TextArea.Caret.Column;

            if (vm.PendingNavigationLine > 0 && editor.Document == vm.ActiveTab?.Document)
            {
                int pending = vm.PendingNavigationLine;
                vm.PendingNavigationLine = 0;
                ApplyLineNavigation(editor, pending);
            }
        }

        SetupFolding(editor);
    }

    internal static void ApplyLineNavigation(TextEditor editor, int line)
    {
        if (editor.Document == null) return;
        if (line < 1) line = 1;
        if (line > editor.Document.LineCount) line = editor.Document.LineCount;

        var docLine = editor.Document.GetLineByNumber(line);
        editor.TextArea.Caret.Offset = docLine.Offset;
        editor.TextArea.Caret.BringCaretToView();
        editor.Focus();
    }

    private void OnTextEditorDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextEditor editor) return;
        
        if (_foldingManagers.TryGetValue(editor, out var manager))
        {
            FoldingManager.Uninstall(manager);
            _foldingManagers.Remove(editor);
        }
    }

    private void SetupFolding(TextEditor editor)
    {
        if (_foldingManagers.TryGetValue(editor, out var oldManager))
        {
            FoldingManager.Uninstall(oldManager);
            _foldingManagers.Remove(editor);
        }

        var manager = FoldingManager.Install(editor.TextArea);
        _foldingManagers[editor] = manager;

        if (editor.Document != null)
            UpdateFoldingForDocument(editor);

        editor.DocumentChanged -= OnEditorDocumentChanged;
        editor.DocumentChanged += OnEditorDocumentChanged;
    }

    private void OnEditorDocumentChanged(object? sender, EventArgs e)
    {
        if (sender is not TextEditor editor) return;
        SetupFolding(editor); // Re-install folding manager for new document
    }

    private void UpdateFoldingForDocument(TextEditor editor)
    {
        if (editor.Document == null) return;

        editor.Document.TextChanged -= OnDocumentTextChanged;
        editor.Document.TextChanged += OnDocumentTextChanged;

        ApplyFolding(editor);
    }

    private DateTime _lastFoldingUpdate;
    private void OnDocumentTextChanged(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastFoldingUpdate).TotalMilliseconds < 300) return;
        _lastFoldingUpdate = now;

        if (_currentEditor == null) return;
        ApplyFolding(_currentEditor);
    }

    private void ApplyFolding(TextEditor editor)
    {
        if (editor.Document == null) return;
        if (!_foldingManagers.TryGetValue(editor, out var manager)) return;

        var ext = GetExtensionForEditor(editor);
        switch (ext)
        {
            case ".xml":
            case ".html":
            case ".htm":
            case ".xaml":
            case ".svg":
            case ".xsd":
            case ".xsl":
                var xmlStrategy = new XmlFoldingStrategy();
                xmlStrategy.UpdateFoldings(manager, editor.Document);
                break;

            case ".cs":
            case ".js":
            case ".ts":
            case ".jsx":
            case ".tsx":
            case ".css":
            case ".less":
            case ".scss":
            case ".json":
            case ".java":
            case ".cpp":
            case ".c":
            case ".h":
            case ".hpp":
            case ".rs":
            case ".go":
            case ".swift":
            case ".kt":
            case ".kts":
            case ".py":
            case ".rb":
            case ".php":
                var braceStrategy = new BraceFoldingStrategy();
                manager.UpdateFoldings(
                    braceStrategy.CreateNewFoldings(editor.Document, out _), -1);
                break;
        }
    }

    private static string? GetExtensionForEditor(TextEditor editor)
    {
        if (editor.DataContext is EditorTab tab && !string.IsNullOrEmpty(tab.FilePath))
            return Path.GetExtension(tab.FilePath).ToLowerInvariant();
        return null;
    }

    private void OnTabContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.DataContext is not EditorTab tab) return;
        if (DataContext is not EditorViewModel vm) return;

        var flyout = new MenuFlyout
        {
            Items =
            {
                new MenuItem
                {
                    Header = "Close",
                    InputGesture = new KeyGesture(Key.W, KeyModifiers.Control),
                    Command = vm.CloseTabCommand,
                    CommandParameter = tab
                },
                new MenuItem
                {
                    Header = "Close Others",
                    Command = vm.CloseTabCommand,
                },
                new MenuItem
                {
                    Header = "Close All",
                    Command = vm.CloseAllTabsCommand,
                },
                new Separator(),
                new MenuItem
                {
                    Header = "Save",
                    InputGesture = new KeyGesture(Key.S, KeyModifiers.Control),
                    Command = vm.SaveTabCommand,
                    CommandParameter = tab
                },
                new MenuItem
                {
                    Header = "Save All",
                    InputGesture = new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift),
                    Command = vm.SaveAllCommand,
                },
                new Separator(),
                new MenuItem
                {
                    Header = "Copy Path",
                },
                new MenuItem
                {
                    Header = "Reveal in File Explorer",
                }
            }
        };

        flyout.ShowAt(border);
        e.Handled = true;
    }
}

internal class SearchCommand : ICommand
{
    private readonly TextEditor _editor;
    public event EventHandler? CanExecuteChanged;

    public SearchCommand(TextEditor editor) { _editor = editor; }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter)
    {
        var panel = SearchPanel.Install(_editor);
        panel.Open();
    }
}

internal class ReplaceCommand : ICommand
{
    private readonly TextEditor _editor;
    public event EventHandler? CanExecuteChanged;

    public ReplaceCommand(TextEditor editor) { _editor = editor; }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter)
    {
        var panel = SearchPanel.Install(_editor);
        panel.Open();
    }
}

internal class SelectNextOccurrenceCommand : ICommand
{
    private readonly TextEditor _editor;
    public event EventHandler? CanExecuteChanged;

    public SelectNextOccurrenceCommand(TextEditor editor) { _editor = editor; }
    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        if (_editor.Document == null) return;
        var doc = _editor.Document;
        var textArea = _editor.TextArea;

        string searchText;
        int searchStart;

        if (textArea.Selection.Length > 0)
        {
            var seg = textArea.Selection.SurroundingSegment;
            if (seg == null) return;
            searchText = doc.GetText(seg);
            searchStart = seg.EndOffset;
        }
        else
        {
            var offset = textArea.Caret.Offset;
            searchText = ExtractWordAtOffset(doc, offset, out int wordStart, out int wordEnd);
            if (string.IsNullOrEmpty(searchText)) return;
            searchStart = offset;
            textArea.Selection = Selection.Create(textArea, wordStart, wordEnd);
        }

        if (string.IsNullOrEmpty(searchText)) return;

        int found = doc.Text.IndexOf(searchText, searchStart, StringComparison.Ordinal);
        if (found < 0)
            found = doc.Text.IndexOf(searchText, 0, Math.Min(searchStart, doc.TextLength), StringComparison.Ordinal);

        if (found >= 0)
        {
            var currentSeg = textArea.Selection.SurroundingSegment;
            if (currentSeg == null) return;
            var currentStart = currentSeg.Offset;
            var currentEnd = currentSeg.EndOffset;
            var newStart = Math.Min(currentStart, found);
            var newEnd = Math.Max(currentEnd, found + searchText.Length);
            textArea.Selection = Selection.Create(textArea, newStart, newEnd);
            textArea.Caret.Offset = found + searchText.Length;
            textArea.Caret.BringCaretToView();
        }
    }

    private static string ExtractWordAtOffset(TextDocument doc, int offset, out int wordStart, out int wordEnd)
    {
        wordStart = offset;
        wordEnd = offset;
        if (offset >= doc.TextLength) return string.Empty;

        while (wordStart > 0 && (char.IsLetterOrDigit(doc.GetCharAt(wordStart - 1)) || doc.GetCharAt(wordStart - 1) == '_'))
            wordStart--;
        while (wordEnd < doc.TextLength && (char.IsLetterOrDigit(doc.GetCharAt(wordEnd)) || doc.GetCharAt(wordEnd) == '_'))
            wordEnd++;

        if (wordStart >= wordEnd) return string.Empty;
        return doc.GetText(wordStart, wordEnd - wordStart);
    }
}

internal class ShowCompletionCommand : ICommand
{
    private readonly TextEditor _editor;
    public event EventHandler? CanExecuteChanged;

    public ShowCompletionCommand(TextEditor editor) { _editor = editor; }
    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        if (_editor.Document == null) return;

        var offset = _editor.TextArea.Caret.Offset;
        var doc = _editor.Document;

        var words = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < doc.TextLength; i++)
        {
            char c = doc.GetCharAt(i);
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
            }
            else
            {
                if (sb.Length > 1)
                    words.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length > 1)
            words.Add(sb.ToString());

        var typed = ExtractPrefix(doc, offset, out _);
        var matches = words
            .Where(w => w.StartsWith(typed, StringComparison.OrdinalIgnoreCase) && !w.Equals(typed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w)
            .Take(50)
            .ToList();

        if (matches.Count == 0) return;

        var completionWindow = new CompletionWindow(_editor.TextArea);
        foreach (var word in matches)
        {
            completionWindow.CompletionList.CompletionData.Add(new WordCompletionData(word));
        }

        completionWindow.Show();
        completionWindow.Closed += (_, _) => completionWindow = null;
    }

    private static string ExtractPrefix(TextDocument doc, int offset, out int wordStart)
    {
        wordStart = offset;
        while (wordStart > 0)
        {
            char c = doc.GetCharAt(wordStart - 1);
            if (!char.IsLetterOrDigit(c) && c != '_') break;
            wordStart--;
        }
        if (wordStart >= offset) return string.Empty;
        return doc.GetText(wordStart, offset - wordStart);
    }
}

internal class WordCompletionData : ICompletionData
{
    public string Text { get; }
    public object? Content => Text;
    public object? Description => null;
    public IImage? Image => null;
    public ICommand? Command => null;
    public double Priority => 0;

    public WordCompletionData(string text) { Text = text; }

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }
}

internal class GoToLineCommand : ICommand
{
    private readonly TextEditor _editor;
    public event EventHandler? CanExecuteChanged;

    public GoToLineCommand(TextEditor editor) { _editor = editor; }
    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        if (_editor.Document == null) return;

        var owner = TopLevel.GetTopLevel(_editor) as Window;
        if (owner == null) return;

        var dialog = new Window
        {
            Title = "Go to Line",
            Width = 300,
            Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3F3F46")),
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel { Margin = new Thickness(16), Spacing = 12 };

        var label = new TextBlock
        {
            Text = $"Enter line number (1-{_editor.Document.LineCount}):",
            Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
            FontSize = 13
        };

        var textBox = new TextBox
        {
            Background = new SolidColorBrush(Color.Parse("#3C3C3C")),
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3F3F46")),
            FontSize = 13
        };

        var goButton = new Button
        {
            Content = "Go",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Thickness(16, 4),
            Background = new SolidColorBrush(Color.Parse("#0E639C")),
            Foreground = new SolidColorBrush(Color.Parse("#FFFFFF")),
            BorderThickness = new Thickness(0)
        };

        goButton.Click += (_, _) =>
        {
            if (int.TryParse(textBox.Text, out int lineNum) && lineNum >= 1 && lineNum <= _editor.Document.LineCount)
            {
                var line = _editor.Document.GetLineByNumber(lineNum);
                _editor.TextArea.Caret.Offset = line.Offset;
                _editor.TextArea.Caret.BringCaretToView();
                dialog.Close();
            }
        };

        textBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
                goButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };

        stack.Children.Add(label);
        stack.Children.Add(textBox);
        stack.Children.Add(goButton);
        dialog.Content = stack;

        textBox.Focus();

        dialog.ShowDialog(owner);
    }
}

internal class UndoCommand : ICommand
{
    private readonly TextEditor _editor;
    public UndoCommand(TextEditor editor) => _editor = editor;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _editor.Document != null && _editor.Document.UndoStack.CanUndo;
    public void Execute(object? parameter) => _editor.Document?.UndoStack.Undo();
}

internal class RedoCommand : ICommand
{
    private readonly TextEditor _editor;
    public RedoCommand(TextEditor editor) => _editor = editor;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _editor.Document != null && _editor.Document.UndoStack.CanRedo;
    public void Execute(object? parameter) => _editor.Document?.UndoStack.Redo();
}
