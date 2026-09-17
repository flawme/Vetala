using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Porta.Pty;
using Vetala.ViewModels;

namespace Vetala.Views;

public partial class BottomPanelView : UserControl
{
    private IPtyConnection? _pty;
    private CancellationTokenSource? _cts;
    private bool _killed;
    private bool _launched;
    private int _lastCols;
    private int _lastRows;

    public BottomPanelView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DetachedFromVisualTree += OnDetached;
        LayoutUpdated += OnLayoutUpdated;
        TerminalView.DataToSend += OnDataToSend;
        TerminalView.OpenFileRequested += OnOpenFileRequested;
        TerminalView.TerminalResized += OnTerminalResized;
    }

    private void OnOpenFileRequested(string path)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(vm.OpenProjectPath))
            {
                path = Path.GetFullPath(Path.Combine(vm.OpenProjectPath, path));
            }
            if (File.Exists(path))
            {
                _ = vm.Editor.OpenFile(path);
            }
        }
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Don't launch here — Bounds are zero. Wait for first layout.
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (Bounds.Width > 10 && Bounds.Height > 10 && !_launched)
        {
            _launched = true;
            _ = LaunchTerminalAsync();
        }
    }

    private void OnTerminalResized(int cols, int rows)
    {
        if (_pty != null && (cols != _lastCols || rows != _lastRows))
        {
            _lastCols = cols;
            _lastRows = rows;
            try { _pty.Resize((ushort)cols, (ushort)rows); } catch { }
        }
    }

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        KillTerminal();
    }

    private void OnDataToSend(byte[] data)
    {
        if (_pty == null || data.Length == 0) return;
        try
        {
            _pty.WriterStream.Write(data);
            _pty.WriterStream.Flush();
        }
        catch { }
    }

    private async Task LaunchTerminalAsync()
    {
        KillTerminal();

        _killed = false;
        _cts = new CancellationTokenSource();

        var workDir = DataContext is MainWindowViewModel vm ? vm.OpenProjectPath : Environment.CurrentDirectory;
        if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
            workDir = Environment.CurrentDirectory;

        var shell = GetShellForPlatform();
        int cols = TerminalView.Emulator.Cols;
        int rows = TerminalView.Emulator.Rows;

        _lastCols = cols;
        _lastRows = rows;

        try
        {
            var options = new PtyOptions
            {
                Name = "Vetala",
                Cols = cols,
                Rows = rows,
                Cwd = workDir,
                App = shell,
                Environment = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "TERM", "xterm-256color" },
                    { "COLORTERM", "truecolor" },
                }
            };

            _pty = await PtyProvider.SpawnAsync(options, _cts.Token);
            _pty.ProcessExited += OnPtyProcessExited;

            _ = Task.Run(() => ReadPtyOutput(_pty.ReaderStream, _cts.Token), _cts.Token);
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                TerminalView.Emulator.ProcessBytes(System.Text.Encoding.UTF8.GetBytes($"Failed to launch terminal: {ex.Message}\r\n"));
            });
        }
    }

    private async Task ReadPtyOutput(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[65536];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read == 0) break;

                var data = new byte[read];
                Buffer.BlockCopy(buffer, 0, data, 0, read);
                Dispatcher.UIThread.Post(() => TerminalView.Emulator.ProcessBytes(data));
            }
        }
        catch (OperationCanceledException) { }
        catch { }

        if (!ct.IsCancellationRequested)
            Dispatcher.UIThread.Post(() =>
            {
                TerminalView.Emulator.ProcessBytes(System.Text.Encoding.UTF8.GetBytes("\r\n[Process exited]\r\n"));
            });
    }

    private void OnPtyProcessExited(object? sender, Porta.Pty.PtyExitedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_killed)
                TerminalView.Emulator.ProcessBytes(System.Text.Encoding.UTF8.GetBytes($"\r\n[Process exited with code {e.ExitCode}]\r\n"));
        });
    }

    private static string GetShellForPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "/bin/zsh";
        return "/bin/bash";
    }

    private void OnNewTerminalClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _launched = false;
        _ = LaunchTerminalAsync();
    }

    private void OnKillTerminalClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        KillTerminal();
        Dispatcher.UIThread.Post(() =>
        {
            TerminalView.Emulator.ProcessBytes(System.Text.Encoding.UTF8.GetBytes("\r\n[Terminal killed]\r\n"));
        });
    }

    public void KillTerminal()
    {
        if (_killed) return;
        _killed = true;

        _cts?.Cancel();

        try
        {
            _pty?.ProcessExited -= OnPtyProcessExited;
            _pty?.Dispose();
        }
        catch { }

        _pty = null;
        _cts = null;
    }
}
