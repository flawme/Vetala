using System;
using System.Collections.Generic;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace Vetala.Services;

public class BraceFoldingStrategy
{
    public bool ShowAttributesWhenFolded { get; set; }

    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        int firstErrorOffset = -1;
        var foldings = CreateNewFoldings(document, out firstErrorOffset);
        manager.UpdateFoldings(foldings, firstErrorOffset);
    }

    public List<NewFolding> CreateNewFoldings(TextDocument document, out int firstErrorOffset)
    {
        firstErrorOffset = -1;
        var foldings = new List<NewFolding>();
        var stack = new Stack<(int Offset, char OpeningChar)>();

        for (int i = 0; i < document.TextLength; i++)
        {
            char c = document.GetCharAt(i);
            if (c == '/' && i + 1 < document.TextLength)
            {
                char next = document.GetCharAt(i + 1);
                if (next == '/')
                {
                    i = SkipToEndOfLine(document, i);
                    continue;
                }
                if (next == '*')
                {
                    i = SkipBlockComment(document, i);
                    continue;
                }
            }
            if (c == '\'' || c == '"')
            {
                i = SkipStringLiteral(document, i, c);
                continue;
            }
            if (c == '{' || c == '[' || c == '(')
            {
                stack.Push((i, c));
            }
            else if (c == '}' || c == ']' || c == ')')
            {
                char expected = c switch
                {
                    '}' => '{',
                    ']' => '[',
                    ')' => '(',
                    _ => '\0'
                };

                if (stack.Count > 0)
                {
                    var (startOffset, openingChar) = stack.Pop();
                    if (openingChar == expected)
                    {
                        int length = i - startOffset;
                        if (length > 1)
                        {
                            var folding = new NewFolding(startOffset, i + 1);
                            foldings.Add(folding);
                        }
                    }
                    else
                    {
                        stack.Push((startOffset, openingChar));
                    }
                }
            }
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }

    private static int SkipToEndOfLine(TextDocument document, int index)
    {
        int i = index + 2;
        while (i < document.TextLength)
        {
            char c = document.GetCharAt(i);
            if (c == '\n' || c == '\r')
                return i;
            i++;
        }
        return document.TextLength;
    }

    private static int SkipBlockComment(TextDocument document, int index)
    {
        int i = index + 2;
        while (i + 1 < document.TextLength)
        {
            if (document.GetCharAt(i) == '*' && document.GetCharAt(i + 1) == '/')
                return i + 1;
            i++;
        }
        return document.TextLength;
    }

    private static int SkipStringLiteral(TextDocument document, int index, char quote)
    {
        int i = index + 1;
        while (i < document.TextLength)
        {
            char c = document.GetCharAt(i);
            if (c == '\\')
            {
                i += 2;
                continue;
            }
            if (c == quote)
                return i;
            i++;
        }
        return document.TextLength;
    }
}
