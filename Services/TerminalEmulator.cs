using System;
using System.Buffers.Binary;
using System.Text;

namespace Vetala.Services;

public class Cell
{
    public char Char { get; set; } = ' ';
    public uint Foreground { get; set; } = 0xFFFFFFFF;
    public uint Background { get; set; } = 0x00000000;
    public bool Bold { get; set; }
    public bool Underline { get; set; }
    public bool Reverse { get; set; }

    public void Reset()
    {
        Char = ' ';
        Foreground = 0xFFFFFFFF;
        Background = 0x00000000;
        Bold = false;
        Underline = false;
        Reverse = false;
    }
}

public class TerminalEmulator
{
    private Cell[][] _grid = Array.Empty<Cell[]>();
    private int _cols;
    private int _rows;
    private int _cursorRow;
    private int _cursorCol;
    private int _scrollTop;
    private uint _currentFg = 0xFFFFFFFF;
    private uint _currentBg = 0x00000000;
    private bool _currentBold;
    private bool _currentUnderline;
    private bool _currentReverse;
    private bool _wrapPending;
    private byte[] _parseBuffer = new byte[4096];
    private int _parseBufferLen;
    private int _savedCursorRow;
    private int _savedCursorCol;

    private Cell[][]? _altGrid;
    private int _altCursorRow;
    private int _altCursorCol;
    private bool _isAltBuffer;

    private readonly System.Collections.Generic.List<Cell[]> _scrollback = new();
    public int ScrollbackCount => _scrollback.Count;
    public int ScrollOffset { get; set; }

    private static readonly uint[] AnsiColors = {
        0xFF000000, 0xFFCD3131, 0xFF0DBC79, 0xFFE5E510,
        0xFF2472C8, 0xFFBC3FBC, 0xFF11A8CD, 0xFFE5E5E5,
        0xFF666666, 0xFFF14C4C, 0xFF23D18B, 0xFFF5F543,
        0xFF3B8EEA, 0xFFD670D6, 0xFF29B8DB, 0xFFE5E5E5
    };

    public event Action? Invalidated;
    public event Action<byte[]>? DataToEmit;

    public int Cols => _cols;
    public int Rows => _rows;
    public int CursorRow => _cursorRow;
    public int CursorCol => _cursorCol;
    public Cell[][] Grid => _grid;

    public TerminalEmulator(int cols, int rows)
    {
        Resize(cols, rows);
    }

    public void Resize(int newCols, int newRows)
    {
        if (newCols <= 0 || newRows <= 0) return;

        var oldGrid = _grid;
        int oldRows = _rows, oldCols = _cols;

        _cols = newCols;
        _rows = newRows;
        _grid = new Cell[newRows][];
        for (int r = 0; r < newRows; r++)
        {
            _grid[r] = new Cell[newCols];
            for (int c = 0; c < newCols; c++)
                _grid[r][c] = new Cell();
        }

        // Copy old content
        if (oldGrid.Length > 0)
        {
            int shift = 0;
            if (newRows < oldRows && _cursorRow >= newRows)
            {
                shift = _cursorRow - newRows + 1;
                // Push shifted rows to scrollback only if not in alt buffer
                if (!_isAltBuffer)
                {
                    for (int r = 0; r < shift; r++)
                    {
                        _scrollback.Add(oldGrid[r]);
                        if (_scrollback.Count > 2000) _scrollback.RemoveAt(0);
                    }
                }
            }

            int copyRows = Math.Min(oldRows - shift, newRows);
            int copyCols = Math.Min(oldCols, newCols);

            for (int r = 0; r < copyRows; r++)
            {
                for (int c = 0; c < copyCols; c++)
                {
                    _grid[r][c].Char = oldGrid[r + shift][c].Char;
                    _grid[r][c].Foreground = oldGrid[r + shift][c].Foreground;
                    _grid[r][c].Background = oldGrid[r + shift][c].Background;
                    _grid[r][c].Bold = oldGrid[r + shift][c].Bold;
                    _grid[r][c].Underline = oldGrid[r + shift][c].Underline;
                    _grid[r][c].Reverse = oldGrid[r + shift][c].Reverse;
                }
            }

            _cursorRow -= shift;
            if (_cursorRow < 0) _cursorRow = 0;
        }

        if (_isAltBuffer)
        {
            var oldAltGrid = _altGrid;
            _altGrid = new Cell[newRows][];
            for (int r = 0; r < newRows; r++)
            {
                _altGrid[r] = new Cell[newCols];
                for (int c = 0; c < newCols; c++) _altGrid[r][c] = new Cell();
            }
            if (oldAltGrid != null && oldAltGrid.Length > 0)
            {
                int copyRows = Math.Min(oldAltGrid.Length, newRows);
                int copyCols = Math.Min(oldAltGrid[0].Length, newCols);
                for (int r = 0; r < copyRows; r++)
                    for (int c = 0; c < copyCols; c++)
                    {
                        _altGrid[r][c].Char = oldAltGrid[r][c].Char;
                        _altGrid[r][c].Foreground = oldAltGrid[r][c].Foreground;
                        _altGrid[r][c].Background = oldAltGrid[r][c].Background;
                        _altGrid[r][c].Bold = oldAltGrid[r][c].Bold;
                        _altGrid[r][c].Underline = oldAltGrid[r][c].Underline;
                        _altGrid[r][c].Reverse = oldAltGrid[r][c].Reverse;
                    }
            }
        }

        ClampCursor();
    }

    public void ProcessBytes(ReadOnlySpan<byte> data)
    {
        // Append to parse buffer and process
        int remaining = data.Length;
        int offset = 0;
        while (remaining > 0)
        {
            int space = _parseBuffer.Length - _parseBufferLen;
            int take = Math.Min(remaining, space);
            data.Slice(offset, take).CopyTo(_parseBuffer.AsSpan(_parseBufferLen));
            _parseBufferLen += take;
            offset += take;
            remaining -= take;

            ProcessParseBuffer();
        }
    }

    private void ProcessParseBuffer()
    {
        int i = 0;
        while (i < _parseBufferLen)
        {
            byte b = _parseBuffer[i];

            if (b == 0x1B) // ESC
            {
                // Need more data to parse escape sequence
                if (i + 1 >= _parseBufferLen) break;

                byte next = _parseBuffer[i + 1];
                if (next == (byte)'[') // CSI
                {
                    // Find end of CSI sequence
                    int end = i + 2;
                    while (end < _parseBufferLen)
                    {
                        byte c = _parseBuffer[end];
                        if ((c >= 0x40 && c <= 0x7E) || c == 0x1B) break;
                        end++;
                    }
                    if (end >= _parseBufferLen) break; // Need more data

                    ParseCsiSequence(_parseBuffer.AsSpan(i + 2, end - i - 1));
                    i = end + 1;
                }
                else if (next == (byte)']') // OSC
                {
                    // Skip until ST (ESC \) or BEL
                    int end = i + 2;
                    while (end < _parseBufferLen)
                    {
                        if (_parseBuffer[end] == 0x07) { end++; break; }
                        if (_parseBuffer[end] == 0x1B && end + 1 < _parseBufferLen && _parseBuffer[end + 1] == (byte)'\\')
                        { end += 2; break; }
                        end++;
                    }
                    if (end >= _parseBufferLen) break;
                    i = end;
                }
                else if (next == (byte)'M') // RI - Reverse index
                {
                    if (_cursorRow > 0)
                        _cursorRow--;
                    else
                        ScrollDown();
                    _wrapPending = false;
                    i += 2;
                }
                else if (next == (byte)'7') // Save cursor
                {
                    _savedCursorRow = _cursorRow;
                    _savedCursorCol = _cursorCol;
                    i += 2;
                }
                else if (next == (byte)'8') // Restore cursor
                {
                    _cursorRow = _savedCursorRow;
                    _cursorCol = _savedCursorCol;
                    ClampCursor();
                    i += 2;
                }
                else if (next == (byte)'(' || next == (byte)')' || next == (byte)'*' || next == (byte)'+') // Character set
                {
                    if (i + 2 < _parseBufferLen) i += 3;
                    else break;
                }
                else
                {
                    i += 2; // Skip unknown 2-byte ESC sequence
                }
            }
            else if (b == 0x08) // BS
            {
                if (_cursorCol > 0) _cursorCol--;
                i++;
            }
            else if (b == 0x09) // TAB
            {
                _cursorCol = Math.Min((_cursorCol / 8 + 1) * 8, _cols - 1);
                i++;
            }
            else if (b == 0x0A || b == 0x0B || b == 0x0C) // LF / VT / FF
            {
                _cursorCol = 0; // Fix staircase effect
                _wrapPending = false;
                LineFeed();
                i++;
            }
            else if (b == 0x0D) // CR
            {
                _cursorCol = 0;
                _wrapPending = false;
                i++;
            }
            else if (b == 0x07) // BEL
            {
                i++;
            }
            else if (b < 0x20) // Other control chars - skip
            {
                i++;
            }
            else
            {
                // Printable character
                char ch = (char)b;
                // Handle UTF-8 multi-byte
                if (b >= 0x80)
                {
                    int bytes = GetUtf8ByteCount(b);
                    if (i + bytes <= _parseBufferLen)
                    {
                        try
                        {
                            var str = System.Text.Encoding.UTF8.GetString(_parseBuffer, i, bytes);
                            if (str.Length > 0) ch = str[0];
                        }
                        catch { }
                        i += bytes;
                    }
                    else
                    {
                        break; // Need more data
                    }
                }
                else
                {
                    i++;
                }

                WriteChar(ch);
            }
        }

        // Shift unconsumed data
        if (i < _parseBufferLen)
        {
            int remaining = _parseBufferLen - i;
            Buffer.BlockCopy(_parseBuffer, i, _parseBuffer, 0, remaining);
            _parseBufferLen = remaining;
        }
        else
        {
            _parseBufferLen = 0;
        }

        Invalidated?.Invoke();
    }

    private void ParseCsiSequence(ReadOnlySpan<byte> seq)
    {
        if (seq.Length == 0) return;

        byte final = seq[^1];
        ReadOnlySpan<byte> paramBytes = seq[..^1];

        // Parse parameters (semicolon-separated)
        int[] pars = new int[16];
        int parsLen = 0;
        int current = 0;
        bool hasCurrent = false;

        foreach (byte b in paramBytes)
        {
            if (b == (byte)';')
            {
                if (parsLen < pars.Length) pars[parsLen++] = hasCurrent ? current : 0;
                current = 0;
                hasCurrent = false;
            }
            else if (b >= 0x30 && b <= 0x39) // digit
            {
                current = current * 10 + (b - 0x30);
                hasCurrent = true;
            }
        }
        if (parsLen < pars.Length) pars[parsLen++] = hasCurrent ? current : 0;

        int p(int index) => index < parsLen ? pars[index] : 0;
        int p1 = p(0) == 0 ? 1 : p(0); // default 1 for most sequences

        switch (final)
        {
            case (byte)'A': // CUU - Cursor up
                _cursorRow = Math.Max(0, _cursorRow - p1);
                _wrapPending = false;
                break;

            case (byte)'B': // CUD - Cursor down
                _cursorRow = Math.Min(_rows - 1, _cursorRow + p1);
                _wrapPending = false;
                break;

            case (byte)'C': // CUF - Cursor forward
                _cursorCol = Math.Min(_cols - 1, _cursorCol + p1);
                _wrapPending = false;
                break;

            case (byte)'D': // CUB - Cursor back
                _cursorCol = Math.Max(0, _cursorCol - p1);
                _wrapPending = false;
                break;

            case (byte)'E': // CNL - Cursor next line
                _cursorRow = Math.Min(_rows - 1, _cursorRow + p1);
                _cursorCol = 0;
                _wrapPending = false;
                break;

            case (byte)'F': // CPL - Cursor previous line
                _cursorRow = Math.Max(0, _cursorRow - p1);
                _cursorCol = 0;
                _wrapPending = false;
                break;

            case (byte)'G': // CHA - Cursor horizontal absolute
                _cursorCol = Math.Clamp(p1 - 1, 0, _cols - 1);
                _wrapPending = false;
                break;

            case (byte)'H': // CUP - Cursor position
            case (byte)'f':
                _cursorRow = Math.Clamp(p(0) - 1, 0, _rows - 1);
                _cursorCol = Math.Clamp(p(1) - 1, 0, _cols - 1);
                _wrapPending = false;
                break;

            case (byte)'J': // ED - Erase in display
                EraseDisplay(p(0));
                break;

            case (byte)'K': // EL - Erase in line
                EraseLine(p(0));
                break;

            case (byte)'L': // IL - Insert lines
                InsertLines(p1);
                break;

            case (byte)'M': // DL - Delete lines
                DeleteLines(p1);
                break;

            case (byte)'P': // DCH - Delete characters
                DeleteChars(p1);
                break;

            case (byte)'@': // ICH - Insert characters
                InsertChars(p1);
                break;

            case (byte)'S': // SU - Scroll up
                ScrollUp(p1);
                break;

            case (byte)'T': // SD - Scroll down
                ScrollDown(p1);
                break;

            case (byte)'X': // ECH - Erase characters
                EraseChars(p1);
                break;

            case (byte)'d': // VPA - Vertical position absolute
                _cursorRow = Math.Clamp(p1 - 1, 0, _rows - 1);
                _wrapPending = false;
                break;

            case (byte)'m': // SGR - Select graphic rendition
                if (parsLen == 0) ApplySgr(0);
                else
                {
                    for (int i = 0; i < parsLen; i++)
                    {
                        if (pars[i] == 38 || pars[i] == 48)
                        {
                            bool isFg = pars[i] == 38;
                            if (i + 2 < parsLen && pars[i + 1] == 5)
                            {
                                uint color = Get256Color((byte)pars[i + 2]);
                                if (isFg) _currentFg = color;
                                else _currentBg = color;
                                i += 2;
                            }
                            else if (i + 4 < parsLen && pars[i + 1] == 2)
                            {
                                byte r = (byte)pars[i + 2];
                                byte g = (byte)pars[i + 3];
                                byte b = (byte)pars[i + 4];
                                uint color = 0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b;
                                if (isFg) _currentFg = color;
                                else _currentBg = color;
                                i += 4;
                            }
                        }
                        else
                        {
                            ApplySgr(pars[i]);
                        }
                    }
                }
                break;

            case (byte)'n': // DSR - Device status report
                if (p(0) == 6)
                {
                    // Respond with cursor position: ESC[row;colR
                    string response = $"\x1b[{_cursorRow + 1};{_cursorCol + 1}R";
                    DataToEmit?.Invoke(System.Text.Encoding.UTF8.GetBytes(response));
                }
                else if (p(0) == 5)
                {
                    DataToEmit?.Invoke(System.Text.Encoding.UTF8.GetBytes("\x1b[0n"));
                }
                break;

            case (byte)'r': // DECSTBM - Set scrolling region
                _scrollTop = 0;
                break;

            case (byte)'s': // Save cursor
                _savedCursorRow = _cursorRow;
                _savedCursorCol = _cursorCol;
                break;

            case (byte)'u': // Restore cursor
                _cursorRow = _savedCursorRow;
                _cursorCol = _savedCursorCol;
                ClampCursor();
                break;

            case (byte)'h': // Set mode
                if (p(0) == 1049) EnterAlternateBuffer();
                break;

            case (byte)'l': // Reset mode
                if (p(0) == 1049) ExitAlternateBuffer();
                break;
        }
    }

    private void ApplySgr(int code)
    {
        switch (code)
        {
            case 0: ResetAttributes(); break;
            case 1: _currentBold = true; break;
            case 2: break; // Dim
            case 3: break; // Italic
            case 4: _currentUnderline = true; break;
            case 5: break; // Blink
            case 7: _currentReverse = true; break;
            case 21: break; // Bold off (some terminals use 22)
            case 22: _currentBold = false; break;
            case 24: _currentUnderline = false; break;
            case 27: _currentReverse = false; break;
            case 30: case 31: case 32: case 33:
            case 34: case 35: case 36: case 37:
                _currentFg = AnsiColors[code - 30];
                break;
            case 38: break; // Extended fg (TODO)
            case 39: _currentFg = 0xFFFFFFFF; break;
            case 40: case 41: case 42: case 43:
            case 44: case 45: case 46: case 47:
                _currentBg = AnsiColors[code - 40];
                break;
            case 48: break; // Extended bg (TODO)
            case 49: _currentBg = 0x00000000; break;
            case 90: case 91: case 92: case 93:
            case 94: case 95: case 96: case 97:
                _currentFg = AnsiColors[code - 90 + 8];
                break;
            case 100: case 101: case 102: case 103:
            case 104: case 105: case 106: case 107:
                _currentBg = AnsiColors[code - 100 + 8];
                break;
        }
    }

    private uint Get256Color(byte index)
    {
        if (index < 16) return AnsiColors[index];
        if (index < 232)
        {
            index -= 16;
            uint r = (uint)(index / 36);
            uint g = (uint)((index / 6) % 6);
            uint b = (uint)(index % 6);
            r = r == 0 ? 0 : r * 40 + 55;
            g = g == 0 ? 0 : g * 40 + 55;
            b = b == 0 ? 0 : b * 40 + 55;
            return 0xFF000000 | (r << 16) | (g << 8) | b;
        }
        else
        {
            uint gray = (uint)((index - 232) * 10 + 8);
            return 0xFF000000 | (gray << 16) | (gray << 8) | gray;
        }
    }

    private void ResetAttributes()
    {
        _currentFg = 0xFFFFFFFF;
        _currentBg = 0x00000000;
        _currentBold = false;
        _currentUnderline = false;
        _currentReverse = false;
    }

    private void WriteChar(char ch)
    {
        if (_wrapPending)
        {
            _cursorCol = 0;
            LineFeed();
            _wrapPending = false;
        }

        if (_cursorRow < _rows && _cursorCol < _cols)
        {
            var cell = _grid[_cursorRow][_cursorCol];
            cell.Char = ch;
            cell.Foreground = _currentFg;
            cell.Background = _currentBg;
            cell.Bold = _currentBold;
            cell.Underline = _currentUnderline;
            cell.Reverse = _currentReverse;
        }

        _cursorCol++;
        if (_cursorCol >= _cols)
        {
            _cursorCol = _cols - 1;
            _wrapPending = true;
        }
    }

    private void LineFeed()
    {
        if (_cursorRow >= _rows - 1)
        {
            ScrollUp();
        }
        else
        {
            _cursorRow++;
        }
    }

    private void ScrollUp(int lines = 1)
    {
        for (int i = 0; i < lines; i++)
        {
            if (!_isAltBuffer)
            {
                _scrollback.Add(_grid[0]);
                if (_scrollback.Count > 2000) _scrollback.RemoveAt(0);

                if (ScrollOffset > 0) ScrollOffset++;
            }

            // Move lines up
            for (int r = 0; r < _rows - 1; r++)
                _grid[r] = _grid[r + 1];

            // Create new empty line at bottom
            _grid[_rows - 1] = new Cell[_cols];
            for (int c = 0; c < _cols; c++)
                _grid[_rows - 1][c] = new Cell();
        }
    }

    private void ScrollDown(int lines = 1)
    {
        for (int i = 0; i < lines; i++)
        {
            // Move lines down
            for (int r = _rows - 1; r > 0; r--)
                _grid[r] = _grid[r - 1];

            // Create new empty line at top
            _grid[0] = new Cell[_cols];
            for (int c = 0; c < _cols; c++)
                _grid[0][c] = new Cell();
        }
    }

    private void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0: // Cursor to end
                EraseLine(0, _cursorCol, _cols);
                for (int r = _cursorRow + 1; r < _rows; r++)
                    ClearRow(r);
                break;
            case 1: // Start to cursor
                for (int r = 0; r < _cursorRow; r++)
                    ClearRow(r);
                ClearRow(_cursorRow, 0, _cursorCol + 1);
                break;
            case 2: // Full clear
                for (int r = 0; r < _rows; r++)
                    ClearRow(r);
                break;
            case 3: // Full clear + scrollback
                for (int r = 0; r < _rows; r++)
                    ClearRow(r);
                _scrollback.Clear();
                ScrollOffset = 0;
                break;
        }
    }

    public Cell[] GetLine(int row)
    {
        int effectiveRow = row - ScrollOffset;
        if (effectiveRow >= 0 && effectiveRow < _rows)
        {
            return _grid[effectiveRow];
        }
        
        int sbIndex = _scrollback.Count + effectiveRow;
        if (sbIndex >= 0 && sbIndex < _scrollback.Count)
        {
            return _scrollback[sbIndex];
        }

        return Array.Empty<Cell>();
    }

    private void EraseLine(int mode)
    {
        EraseLine(mode, _cursorCol, _cols);
    }

    private void EraseLine(int mode, int fromCol, int toCol)
    {
        if (_cursorRow >= _rows) return;
        var row = _grid[_cursorRow];

        switch (mode)
        {
            case 0: // Cursor to end
                for (int c = fromCol; c < _cols; c++)
                    row[c].Reset();
                break;
            case 1: // Start to cursor
                for (int c = 0; c <= fromCol && c < _cols; c++)
                    row[c].Reset();
                break;
            case 2: // Full line
                ClearRow(_cursorRow);
                break;
        }
    }

    private void EraseChars(int count)
    {
        if (_cursorRow >= _rows) return;
        var row = _grid[_cursorRow];
        int end = Math.Min(_cursorCol + count, _cols);
        for (int c = _cursorCol; c < end; c++)
            row[c].Reset();
    }

    private void InsertLines(int count)
    {
        if (_cursorRow >= _rows - 1) return;
        for (int i = 0; i < count; i++)
        {
            for (int r = _rows - 1; r > _cursorRow; r--)
                _grid[r] = _grid[r - 1];
            _grid[_cursorRow] = new Cell[_cols];
            for (int c = 0; c < _cols; c++)
                _grid[_cursorRow][c] = new Cell();
        }
    }

    private void DeleteLines(int count)
    {
        if (_cursorRow >= _rows) return;
        for (int i = 0; i < count; i++)
        {
            for (int r = _cursorRow; r < _rows - 1; r++)
                _grid[r] = _grid[r + 1];
            _grid[_rows - 1] = new Cell[_cols];
            for (int c = 0; c < _cols; c++)
                _grid[_rows - 1][c] = new Cell();
        }
    }

    private void DeleteChars(int count)
    {
        if (_cursorRow >= _rows) return;
        var row = _grid[_cursorRow];
        int shift = Math.Min(count, _cols - _cursorCol);
        for (int c = _cursorCol; c < _cols - shift; c++)
            row[c] = row[c + shift];
        for (int c = _cols - shift; c < _cols; c++)
            row[c] = new Cell();
    }

    private void InsertChars(int count)
    {
        if (_cursorRow >= _rows) return;
        var row = _grid[_cursorRow];
        int shift = Math.Min(count, _cols - _cursorCol);
        for (int c = _cols - 1; c >= _cursorCol + shift; c--)
            row[c] = row[c - shift];
        for (int c = _cursorCol; c < _cursorCol + shift && c < _cols; c++)
            row[c] = new Cell();
    }

    private void ClearRow(int row)
    {
        ClearRow(row, 0, _cols);
    }

    private void ClearRow(int row, int from, int to)
    {
        if (row < 0 || row >= _rows) return;
        for (int c = from; c < to && c < _cols; c++)
            _grid[row][c].Reset();
    }

    private void ClampCursor()
    {
        if (_cursorRow < 0) _cursorRow = 0;
        if (_cursorRow >= _rows) _cursorRow = _rows - 1;
        if (_cursorCol < 0) _cursorCol = 0;
        if (_cursorCol >= _cols) _cursorCol = _cols - 1;
        _wrapPending = false;
    }

    private static int GetUtf8ByteCount(byte firstByte)
    {
        if ((firstByte & 0x80) == 0) return 1;
        if ((firstByte & 0xE0) == 0xC0) return 2;
        if ((firstByte & 0xF0) == 0xE0) return 3;
        if ((firstByte & 0xF8) == 0xF0) return 4;
        return 1;
    }

    private void EnterAlternateBuffer()
    {
        if (_isAltBuffer) return;
        _isAltBuffer = true;
        
        _altGrid = _grid;
        _altCursorRow = _cursorRow;
        _altCursorCol = _cursorCol;
        
        _grid = new Cell[_rows][];
        for (int r = 0; r < _rows; r++)
        {
            _grid[r] = new Cell[_cols];
            for (int c = 0; c < _cols; c++) _grid[r][c] = new Cell();
        }
        _cursorRow = 0;
        _cursorCol = 0;
        ResetAttributes();
    }

    private void ExitAlternateBuffer()
    {
        if (!_isAltBuffer) return;
        _isAltBuffer = false;
        
        if (_altGrid != null && _altGrid.Length == _rows && _altGrid[0].Length == _cols)
        {
            _grid = _altGrid;
        }
        else 
        {
            _grid = new Cell[_rows][];
            for (int r = 0; r < _rows; r++)
            {
                _grid[r] = new Cell[_cols];
                for (int c = 0; c < _cols; c++) _grid[r][c] = new Cell();
            }
        }
        _cursorRow = _altCursorRow;
        _cursorCol = _altCursorCol;
        ClampCursor();
    }
}
