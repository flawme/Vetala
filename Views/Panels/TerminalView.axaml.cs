using System;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Vetala.Services;

namespace Vetala.Views.Panels;

public partial class TerminalView : Control
{
    private readonly TerminalEmulator _emulator;
    private Typeface _typeface;
    private double _fontSize;
    private double _cellWidth;
    private double _cellHeight;
    private double _baseline;
    private bool _needsRecalc;
    private ISolidColorBrush _bgBrush;
    private readonly DispatcherTimer _cursorTimer;
    private bool _cursorVisible = true;

    private (int row, int col)? _selStart;
    private (int row, int col)? _selEnd;
    private bool _isSelecting;

    public event Action<string>? OpenFileRequested;
    public event Action<int, int>? TerminalResized;

    private const double PaddingX = 6;
    private const double PaddingY = 4;

    public TerminalView() : this(80, 24) { }

    public TerminalView(int cols, int rows)
    {
        _emulator = new TerminalEmulator(cols, rows);
        _fontSize = 14;
        _bgBrush = new SolidColorBrush(Color.FromArgb(255, 0x0C, 0x0C, 0x0C));
        Focusable = true;
        ClipToBounds = true;

        _emulator.Invalidated += () => Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        _emulator.DataToEmit += (data) => DataToSend?.Invoke(data);
        GotFocus += (_, _) =>
        {
            _cursorVisible = true;
            _cursorTimer.Start();
            InvalidateVisual();
        };
        LostFocus += (_, _) =>
        {
            _cursorTimer.Stop();
            _cursorVisible = false;
            InvalidateVisual();
        };
        AttachedToVisualTree += (_, _) =>
        {
            _needsRecalc = true;
            ResolveFont();
            InvalidateVisual();
        };
        DetachedFromVisualTree += (_, _) => _cursorTimer.Stop();

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorVisible = !_cursorVisible;
            InvalidateVisual();
        };
    }

    public TerminalEmulator Emulator => _emulator;

    public event Action<byte[]>? DataToSend;

    protected override Size MeasureOverride(Size availableSize)
    {
        RecalcMetrics();
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        RecalcMetrics();
        if (_cellWidth <= 0 || _cellHeight <= 0) return finalSize;

        double availW = finalSize.Width - PaddingX * 2;
        double availH = finalSize.Height - PaddingY * 2;
        int cols = Math.Max(1, (int)(availW / _cellWidth));
        int rows = Math.Max(1, (int)(availH / _cellHeight));

        if (cols != _emulator.Cols || rows != _emulator.Rows)
        {
            _emulator.Resize(cols, rows);
            TerminalResized?.Invoke(cols, rows);
        }

        return finalSize;
    }

    private void ResolveFont()
    {
        FontFamily[] families = [
            new FontFamily("avares://Vetala/Assets/Fonts#JetBrains Mono"),
            new FontFamily("DejaVu Sans Mono"),
            new FontFamily("Liberation Mono"),
            FontFamily.Parse("monospace")
        ];
        foreach (var f in families)
        {
            var tf = new Typeface(f);
            if (FontManager.Current.TryGetGlyphTypeface(tf, out _))
            {
                _typeface = tf;
                _needsRecalc = true;
                RecalcMetrics();
                return;
            }
        }
        _typeface = new Typeface(FontFamily.Parse("monospace"));
        _needsRecalc = true;
    }

    private void RecalcMetrics()
    {
        if (_cellWidth > 0 && !_needsRecalc) return;
        _needsRecalc = false;

        if (_typeface == null)
            _typeface = new Typeface("monospace");

        var ft = new FormattedText(
            "M",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeface,
            _fontSize,
            Brushes.White);
            
        _cellWidth = ft.Width > 0 ? ft.Width : _fontSize * 0.6;

        if (FontManager.Current.TryGetGlyphTypeface(_typeface, out var gf))
        {
            double designToPixel = _fontSize / gf.Metrics.DesignEmHeight;
            _cellHeight = gf.Metrics.LineSpacing * designToPixel;
            _baseline = (gf.Metrics.Ascent + gf.Metrics.LineGap) * designToPixel;
        }
        else
        {
            _cellHeight = _fontSize * 1.2;
            _baseline = _fontSize;
        }
    }

    private (int row, int col) GetCellAtPoint(Point p)
    {
        int col = (int)Math.Floor((p.X - PaddingX) / _cellWidth);
        int row = (int)Math.Floor((p.Y - PaddingY) / _cellHeight);
        col = Math.Clamp(col, 0, _emulator.Cols - 1);
        row = Math.Clamp(row, 0, _emulator.Rows - 1);
        return (row - _emulator.ScrollOffset, col);
    }

    private bool IsCellSelected(int globalRow, int col)
    {
        if (_selStart == null || _selEnd == null) return false;
        
        var start = _selStart.Value;
        var end = _selEnd.Value;

        if (start.row > end.row || (start.row == end.row && start.col > end.col))
        {
            var temp = start;
            start = end;
            end = temp;
        }

        if (globalRow < start.row || globalRow > end.row) return false;
        if (globalRow == start.row && globalRow == end.row) return col >= start.col && col <= end.col;
        if (globalRow == start.row) return col >= start.col;
        if (globalRow == end.row) return col <= end.col;
        return true;
    }

    public override void Render(DrawingContext ctx)
    {
        RecalcMetrics();
        if (_cellWidth <= 0 || _cellHeight <= 0) return;

        if (_emulator.Rows == 0) return;

        ctx.DrawRectangle(_bgBrush, null, new Rect(0, 0, Bounds.Width, Bounds.Height));

        double ox = PaddingX;
        double oy = PaddingY;

        var selectionBrush = new SolidColorBrush(Color.FromArgb(100, 50, 100, 255));

        for (int row = 0; row < _emulator.Rows; row++)
        {
            var cells = _emulator.GetLine(row);
            if (cells.Length == 0) continue;
            
            int globalRow = row - _emulator.ScrollOffset;
            double y = oy + row * _cellHeight;

            for (int col = 0; col < cells.Length; col++)
            {
                var cell = cells[col];
                double x = ox + col * _cellWidth;
                bool isSelected = IsCellSelected(globalRow, col);

                var actualFg = FromArgb(cell.Foreground);
                var actualBg = cell.Background == 0x00000000 ? _bgBrush : FromArgb(cell.Background);

                var bg = isSelected ? selectionBrush : (cell.Reverse ? actualFg : actualBg);
                var fg = cell.Reverse ? actualBg : actualFg;

                if (cell.Reverse || cell.Background != 0x00000000 || isSelected)
                {
                    ctx.DrawRectangle(bg, null, new Rect(x, y, _cellWidth, _cellHeight));
                }

                bool hasContent = cell.Char != ' ' && cell.Char != '\0';
                if (hasContent)
                {
                    var ft = new FormattedText(
                        new string(cell.Char, 1),
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        _typeface,
                        _fontSize,
                        fg);
                    ctx.DrawText(ft, new Point(x, y));

                    if (cell.Underline)
                        ctx.DrawRectangle(fg, null, new Rect(x, y + _baseline + 1, _cellWidth, 1));
                }
            }
        }

        // Draw cursor only if we are not scrolled away from it
        if (_emulator.ScrollOffset == 0)
        {
            int cr = _emulator.CursorRow;
            int cc = _emulator.CursorCol;
            if (cr >= 0 && cr < _emulator.Rows && cc >= 0 && cc < _emulator.Cols)
            {
                double cx = ox + cc * _cellWidth;
                double cy = oy + cr * _cellHeight;
                var cursorColor = Color.FromArgb(255, 180, 180, 180);

                if (IsFocused)
                {
                    if (_cursorVisible)
                    {
                        ctx.DrawRectangle(new SolidColorBrush(cursorColor), null, new Rect(cx, cy, _cellWidth, _cellHeight));
                        
                        var cell = _emulator.GetLine(cr)[cc];
                        if (cell.Char != ' ' && cell.Char != '\0')
                        {
                            var ft = new FormattedText(
                                new string(cell.Char, 1),
                                CultureInfo.InvariantCulture,
                                FlowDirection.LeftToRight,
                                _typeface,
                                _fontSize,
                                _bgBrush);
                            ctx.DrawText(ft, new Point(cx, cy));
                        }
                    }
                }
                else
                {
                    var pen = new Pen(new SolidColorBrush(cursorColor), 1);
                    ctx.DrawRectangle(null, pen, new Rect(cx + 0.5, cy + 0.5, _cellWidth - 1, _cellHeight - 1));
                }
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                HandleLinkClick(e.GetPosition(this));
            }
            else
            {
                _selStart = GetCellAtPoint(e.GetPosition(this));
                _selEnd = _selStart;
                _isSelecting = true;
                InvalidateVisual();
            }
        }
        else if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            PasteFromClipboard();
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isSelecting)
        {
            _selEnd = GetCellAtPoint(e.GetPosition(this));
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isSelecting)
        {
            _isSelecting = false;
            CopySelectionToClipboard();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y > 0) // scroll up
        {
            _emulator.ScrollOffset = Math.Min(_emulator.ScrollOffset + 3, _emulator.ScrollbackCount);
            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.Delta.Y < 0) // scroll down
        {
            _emulator.ScrollOffset = Math.Max(0, _emulator.ScrollOffset - 3);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsFocused) return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (ctrl && shift && e.Key == Key.V)
        {
            PasteFromClipboard();
            e.Handled = true;
            return;
        }

        if (ctrl && shift && e.Key == Key.C)
        {
            CopySelectionToClipboard();
            e.Handled = true;
            return;
        }

        _emulator.ScrollOffset = 0;
        _cursorVisible = true;
        _cursorTimer.Stop();
        _cursorTimer.Start();

        byte[]? data = MapKey(e);
        if (data != null)
        {
            DataToSend?.Invoke(data);
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!IsFocused || string.IsNullOrEmpty(e.Text)) return;

        _emulator.ScrollOffset = 0;
        _cursorVisible = true;
        _cursorTimer.Stop();
        _cursorTimer.Start();

        byte[] bytes = Encoding.UTF8.GetBytes(e.Text);
        DataToSend?.Invoke(bytes);
        e.Handled = true;
    }

    private byte[]? MapKey(KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (ctrl)
        {
            return e.Key switch
            {
                Key.A => new byte[] { 1 }, Key.B => new byte[] { 2 }, Key.C => new byte[] { 3 },
                Key.D => new byte[] { 4 }, Key.E => new byte[] { 5 }, Key.F => new byte[] { 6 },
                Key.G => new byte[] { 7 }, Key.H => new byte[] { 8 }, Key.I => new byte[] { 9 },
                Key.J => new byte[] { 10 }, Key.K => new byte[] { 11 }, Key.L => new byte[] { 12 },
                Key.M => new byte[] { 13 }, Key.N => new byte[] { 14 }, Key.O => new byte[] { 15 },
                Key.P => new byte[] { 16 }, Key.Q => new byte[] { 17 }, Key.R => new byte[] { 18 },
                Key.S => new byte[] { 19 }, Key.T => new byte[] { 20 }, Key.U => new byte[] { 21 },
                Key.V => new byte[] { 22 }, Key.W => new byte[] { 23 }, Key.X => new byte[] { 24 },
                Key.Y => new byte[] { 25 }, Key.Z => new byte[] { 26 },
                Key.Oem4 => new byte[] { 0x1B }, // Ctrl + [
                Key.Oem5 => new byte[] { 0x1C }, // Ctrl + \
                Key.Oem6 => new byte[] { 0x1D }, // Ctrl + ]
                Key.Space => new byte[] { 0x00 }, // Ctrl + Space
                _ => null
            };
        }

        return e.Key switch
        {
            Key.Return => new byte[] { 0x0D },
            Key.Back => new byte[] { 0x08 },
            Key.Tab => new byte[] { 0x09 },
            Key.Escape => new byte[] { 0x1B },
            Key.Up => "\x1B[A"u8.ToArray(),
            Key.Down => "\x1B[B"u8.ToArray(),
            Key.Right => "\x1B[C"u8.ToArray(),
            Key.Left => "\x1B[D"u8.ToArray(),
            Key.Home => "\x1B[H"u8.ToArray(),
            Key.End => "\x1B[F"u8.ToArray(),
            Key.Delete => "\x1B[3~"u8.ToArray(),
            Key.Insert => "\x1B[2~"u8.ToArray(),
            Key.PageUp => "\x1B[5~"u8.ToArray(),
            Key.PageDown => "\x1B[6~"u8.ToArray(),
            Key.F1 => "\x1BOP"u8.ToArray(),
            Key.F2 => "\x1BOQ"u8.ToArray(),
            Key.F3 => "\x1BOR"u8.ToArray(),
            Key.F4 => "\x1BOS"u8.ToArray(),
            Key.F5 => "\x1B[15~"u8.ToArray(),
            Key.F6 => "\x1B[17~"u8.ToArray(),
            Key.F7 => "\x1B[18~"u8.ToArray(),
            Key.F8 => "\x1B[19~"u8.ToArray(),
            Key.F9 => "\x1B[20~"u8.ToArray(),
            Key.F10 => "\x1B[21~"u8.ToArray(),
            Key.F11 => "\x1B[23~"u8.ToArray(),
            Key.F12 => "\x1B[24~"u8.ToArray(),
            _ => null
        };
    }

    private async void CopySelectionToClipboard()
    {
        if (_selStart == null || _selEnd == null) return;
        
        var start = _selStart.Value;
        var end = _selEnd.Value;
        if (start.row > end.row || (start.row == end.row && start.col > end.col))
        {
            var temp = start;
            start = end;
            end = temp;
        }

        var sb = new StringBuilder();
        for (int r = start.row; r <= end.row; r++)
        {
            var cells = _emulator.GetLine(r + _emulator.ScrollOffset);
            if (cells.Length == 0) continue;

            int cStart = (r == start.row) ? start.col : 0;
            int cEnd = (r == end.row) ? end.col : cells.Length - 1;

            // Find last non-empty char
            int lastContentCol = cells.Length - 1;
            while (lastContentCol >= 0 && (cells[lastContentCol].Char == ' ' || cells[lastContentCol].Char == '\0'))
                lastContentCol--;

            cEnd = Math.Min(cEnd, lastContentCol);

            for (int c = cStart; c <= cEnd; c++)
            {
                sb.Append(cells[c].Char == '\0' ? ' ' : cells[c].Char);
            }
            if (r < end.row && cEnd <= lastContentCol) sb.AppendLine();
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null && sb.Length > 0)
        {
            await clipboard.SetTextAsync(sb.ToString().TrimEnd('\r', '\n'));
        }
    }

    private async void PasteFromClipboard()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            string? text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"));
                DataToSend?.Invoke(bytes);
            }
        }
    }

    private void HandleLinkClick(Point p)
    {
        var (globalRow, col) = GetCellAtPoint(p);
        var cells = _emulator.GetLine(globalRow + _emulator.ScrollOffset);
        if (cells.Length == 0) return;

        var sb = new StringBuilder();
        for (int c = 0; c < cells.Length; c++)
        {
            sb.Append(cells[c].Char == '\0' ? ' ' : cells[c].Char);
        }
        string line = sb.ToString();

        var matches = System.Text.RegularExpressions.Regex.Matches(line, @"(?:[A-Za-z]:\\|/)[a-zA-Z0-9_\-\./\\]+");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (col >= match.Index && col < match.Index + match.Length)
            {
                string path = match.Value;
                OpenFileRequested?.Invoke(path);
                return;
            }
        }
    }

    private static ISolidColorBrush FromArgb(uint argb)
    {
        byte a = (byte)((argb >> 24) & 0xFF);
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);
        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }
}
