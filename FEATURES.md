# Vetala Features

## Keyboard Shortcuts

### File Operations
| Shortcut | Action |
|----------|--------|
| `Ctrl+S` | Save current file |
| `Ctrl+Shift+S` | Save all files |
| `Ctrl+W` | Close current tab |

### Navigation
| Shortcut | Action |
|----------|--------|
| `Ctrl+P` | Quick Open - search files by name |
| `Ctrl+Shift+P` | Command Palette |
| `Ctrl+K` | Quick Actions menu |
| `Ctrl+G` | Go to Line |
| `Ctrl+B` | Toggle Explorer sidebar |

### Search and Source Control
| Shortcut | Action |
|----------|--------|
| `Ctrl+Shift+F` | Find in files (project-wide search) |
| `Ctrl+F` | Find in current file |
| `Ctrl+H` | Find and Replace |
| `Ctrl+Shift+G` | Toggle Source Control panel |

### Editing
| Shortcut | Action |
|----------|--------|
| `Ctrl+D` | Select next occurrence of selected text / word |
| `Ctrl+Space` | Show code completion |
| `Ctrl+Z` / `Ctrl+Y` | Undo / Redo |
| `Ctrl+A` / `Ctrl+C` / `Ctrl+V` / `Ctrl+X` | Select All / Copy / Paste / Cut |

### Terminal
| Shortcut | Action |
|----------|--------|
| `Enter` | Execute command |
| `Ctrl+C` | Send interrupt (when no text selected) |
| `Up Arrow` | Previous command in history |
| `Down Arrow` | Next command in history |
| `Ctrl+L` | Clear terminal |

## Panels

### Explorer
- Virtualized flat file tree of the opened project
- Create, rename, duplicate and delete files and folders (toolbar buttons and context menu)
- Single click selects, double click opens, `F2` renames

### Search
- Project-wide find in files, grouped by file with per-line previews
- Match-case toggle; result count summary
- Clicking a result opens the file at that exact line
- Skips generated and VCS directories (`.git`, `bin`, `obj`, `node_modules`, ...)

### Source Control
- Shows the current branch of the opened project (read from `.git/HEAD`)
- Lists changed and untracked files from `git status`, with a per-file status badge
- Click a file to open it in the editor
- Requires `git` on the PATH

### Editor
- Multi-tab editing with dirty indicators and breadcrumbs
- Syntax highlighting for C#, C, TypeScript, JavaScript, Python, Rust, Go, XML, JSON, YAML, Markdown and more
- Code folding (XML: `XmlFoldingStrategy`, braces: custom `BraceFoldingStrategy`)
- Word-based code completion (`Ctrl+Space`)
- Find / replace / go-to-line built in

### Terminal (bottom panel)
- Real bash / zsh / cmd.exe integration via Porta.Pty (forkpty on Unix, ConPTY on Windows)
- Custom ANSI parser + cell-based renderer written in C#
- Command history, resize-aware, clickable file paths

### Status Bar
- Live git branch of the opened project
- Live caret line / column
- Language detection from the active file's extension

## Context Menus

### File Explorer (Right-click)
- New File
- New Folder
- Rename
- Duplicate
- Copy Path
- Delete

### Editor Tab (Right-click)
- Close
- Close Others
- Close All
- Save
- Save All
- Copy Path
- Reveal in File Explorer

## Tech Stack
- **Language:** C# (.NET 10.0)
- **UI Framework:** Avalonia UI 12.0.4
- **Editor:** AvaloniaEdit 12.0.0
- **MVVM:** CommunityToolkit.Mvvm 8.4.1
- **Terminal:** Porta.Pty 1.0.7
- **Theme:** VS Code Dark+

## Architecture

```
Vetala/
├── Assets/           # Icons, fonts, images
├── Models/           # Data models (EditorTab, FileSystemNode, search results, git entries)
├── Services/         # Business logic (FileSystemService, SearchService, GitService, TerminalEmulator, DialogService)
├── ViewModels/       # MVVM ViewModels (MainWindow, Editor, Sidebar, Search, SourceControl)
├── Views/            # XAML Views and code-behind
│   ├── Pages/        # Welcome, Licenses pages
│   ├── Panels/       # Editor, Sidebar, Search, Source Control, Bottom Panel
│   └── Dialogs/      # Permission, Input dialogs
└── Styles/           # DesignSystem (VS Code Dark+ theme)
```

## Roadmap

- C# language tooling via the Language Server Protocol:
  - [OmniSharp/csharp-language-server-protocol](https://github.com/OmniSharp/csharp-language-server-protocol) - C# LSP + DAP
  - [matarillo/LanguageServerProtocol](https://github.com/matarillo/LanguageServerProtocol) - Lighter C# LSP SDK
  - [opencode-ai/opencode](https://github.com/opencode-ai/opencode) - Go LSP client reference
