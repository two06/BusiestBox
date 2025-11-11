using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

using BusiestBox.UI;

namespace BusiestBox.Utils
{
    internal static class TabComplete
    {
        private static volatile bool _externalRedrawRequested = false;
        private static int _promptAnchorTop = -1;

        public static void MarkPromptAnchor()
        {
            try { _promptAnchorTop = Console.CursorTop; }
            catch { _promptAnchorTop = 0; }
        }

        public static void NotifyExternalInterrupt()
        {
            _externalRedrawRequested = true;
        }

        public static string ReadLineWithTabCompletion(
            string currentDirectory,
            string prompt,
            string[] commands,
            List<string> commandHistory,
            ref int historyIndex,
            ref int lastRenderLen)
        {
            var buffer = new StringBuilder();
            int cursorPos = 0;

            // double-tab / common-prefix state
            string lastTabPartial = null;
            int lastTabTokenStart = -1;
            int lastTabCursorPos = -1;

            // Initial draw (prompt already written by caller)
            RedrawPromptAndBuffer(prompt, buffer, cursorPos, ref lastRenderLen);

            while (true)
            {
                if (_externalRedrawRequested)
                {
                    _externalRedrawRequested = false;
                    RedrawPromptAndBuffer(prompt, buffer, cursorPos, ref lastRenderLen);
                }

                var key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Tab)
                {
                    string text = buffer.ToString();

                    // Token under caret
                    int tokenStart = FindTokenStart(text, cursorPos);
                    int tokenLen = cursorPos - tokenStart;
                    string token = tokenLen > 0 ? text.Substring(tokenStart, tokenLen) : "";
                    bool completingFirstToken = tokenStart == 0;

                    // ---------- ask external providers first (command/netopts/file) ----------
                    string[] matches;
                    var ctx = new CompletionContext
                    {
                        CurrentDirectory = currentDirectory,
                        FullLine = text,
                        CursorPos = cursorPos,
                        Commands = commands,
                        TokenStart = tokenStart,
                        CurrentToken = token,
                        ArgsUpToCursor = SplitArgsPreservingQuotes(text.Substring(0, Math.Max(0, cursorPos)))
                    };

                    bool handled = CompletionRegistry.TryGetCompletions(ctx, out matches);

                    // ---------- same insertion / LCP / double-tab UI as before ----------
                    if (matches == null || matches.Length == 0)
                    {
                        continue; // nothing to do
                    }
                    else if (matches.Length == 1)
                    {
                        string completion = matches[0];
                        InsertCompletion(prompt, buffer, ref cursorPos, ref lastRenderLen,
                                         tokenStart, tokenLen, completion, addDirSlash: true);
                        lastTabPartial = null;
                        continue;
                    }
                    else
                    {
                        string lcp = LongestCommonPrefix(matches);

                        if (!string.IsNullOrEmpty(lcp) && lcp.Length > token.Length)
                        {
                            InsertCompletion(prompt, buffer, ref cursorPos, ref lastRenderLen,
                                             tokenStart, tokenLen, lcp, addDirSlash: false);
                            lastTabPartial = null;
                            lastTabTokenStart = -1;
                            lastTabCursorPos = -1;
                        }
                        else
                        {
                            bool sameContext =
                                lastTabPartial != null &&
                                string.Equals(lastTabPartial, token, StringComparison.Ordinal) &&
                                lastTabTokenStart == tokenStart &&
                                lastTabCursorPos == cursorPos;

                            if (sameContext)
                            {
                                Console.WriteLine();
                                int width;
                                try { width = Console.WindowWidth > 0 ? Console.WindowWidth : Console.BufferWidth; }
                                catch { width = 120; }

                                PrintInColumns(matches, width);

                                // Move anchor to line after the printed list and force a fresh repaint
                                try { _promptAnchorTop = Console.CursorTop; } catch { _promptAnchorTop += 1; }
                                lastRenderLen = 0;
                                RedrawPromptAndBuffer(prompt, buffer, cursorPos, ref lastRenderLen);
                            }
                            else
                            {
                                // arm for next Tab (double-tab to list)
                                lastTabPartial = token;
                                lastTabTokenStart = tokenStart;
                                lastTabCursorPos = cursorPos;
                                // Optional: BeepSoft();
                            }
                        }
                    }
                }

                else if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    string cmd = buffer.ToString();
                    if (!string.IsNullOrWhiteSpace(cmd))
                    {
                        commandHistory.Add(cmd);
                        historyIndex = commandHistory.Count;
                    }
                    return cmd;
                }
                else if (key.Key == ConsoleKey.Backspace)
                {
                    if (cursorPos > 0)
                    {
                        buffer.Remove(cursorPos - 1, 1);
                        cursorPos--;
                        RedrawPromptAndBuffer(prompt, buffer, cursorPos, ref lastRenderLen);
                    }
                    // reset double-tab arming on edit
                    lastTabPartial = null;
                }
                else if (key.Key == ConsoleKey.Delete)
                {
                    if (cursorPos < buffer.Length)
                    {
                        buffer.Remove(cursorPos, 1);
                        RedrawPromptAndBuffer(prompt, buffer, cursorPos, ref lastRenderLen);
                    }
                    lastTabPartial = null;
                }
                else if (key.Key == ConsoleKey.LeftArrow)
                {
                    if (cursorPos > 0)
                    {
                        cursorPos--;
                        RedrawPromptAndBuffer(prompt, buffer, cursorPos, ref lastRenderLen);
                    }
                }
                else if (key.Key == ConsoleKey.RightArrow)
                {
                    if (cursorPos < buffer.Length)
                    {
                        cursorPos++;
                        RedrawPromptAndBuffer(prompt, buffer, cursorPos, ref lastRenderLen);
                    }
                }
                else if (key.Key == ConsoleKey.Home)
                {
                    cursorPos = 0;
                    RedrawPromptAndBuffer(prompt, buffer, cursorPos, ref lastRenderLen);
                }
                else if (key.Key == ConsoleKey.End)
                {
                    cursorPos = buffer.Length;
                    RedrawPromptAndBuffer(prompt, buffer, cursorPos, ref lastRenderLen);
                }
                else if (key.Key == ConsoleKey.UpArrow)
                {
                    if (commandHistory.Count > 0 && historyIndex > 0)
                    {
                        historyIndex--;
                        ReplaceCurrentLine(prompt, commandHistory[historyIndex], buffer, ref cursorPos, ref lastRenderLen);
                    }
                    lastTabPartial = null;
                }
                else if (key.Key == ConsoleKey.DownArrow)
                {
                    if (commandHistory.Count > 0 && historyIndex < commandHistory.Count - 1)
                    {
                        historyIndex++;
                        ReplaceCurrentLine(prompt, commandHistory[historyIndex], buffer, ref cursorPos, ref lastRenderLen);
                    }
                    else if (historyIndex == commandHistory.Count - 1)
                    {
                        historyIndex++;
                        ReplaceCurrentLine(prompt, string.Empty, buffer, ref cursorPos, ref lastRenderLen);
                    }
                    lastTabPartial = null;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    buffer.Insert(cursorPos, key.KeyChar);
                    cursorPos++;
                    RedrawPromptAndBuffer(prompt, buffer, cursorPos, ref lastRenderLen);
                    lastTabPartial = null;
                }
            }
        }

        public static string[] SplitArgsPreservingQuotes(string input)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(input)) return Array.Empty<string>();

            var sb = new StringBuilder();
            bool inQuotes = false;
            bool tokenHadQuotes = false; // marks that this token had quotes (so "" becomes an empty arg)

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '"')
                {
                    // Toggle quote state; remember that quotes were present even if nothing inside them.
                    inQuotes = !inQuotes;
                    tokenHadQuotes = true;
                    continue;
                }

                // Optional: handle simple escapes inside quotes: \" or \\
                if (inQuotes && c == '\\' && i + 1 < input.Length && (input[i + 1] == '"' || input[i + 1] == '\\'))
                {
                    sb.Append(input[++i]);
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    // End of token
                    if (sb.Length > 0 || tokenHadQuotes)
                    {
                        list.Add(sb.ToString());
                        sb.Clear();
                        tokenHadQuotes = false;
                    }
                    // else: skip multiple spaces
                }
                else
                {
                    sb.Append(c);
                }
            }

            // Flush last token
            if (sb.Length > 0 || tokenHadQuotes)
                list.Add(sb.ToString());

            return list.ToArray();
        }

        private static int FindTokenStart(string text, int cursorPos)
        {
            // Simple: tokens separated by space; ignore quotes for now
            int i = Math.Min(Math.Max(cursorPos - 1, 0), text.Length - 1);
            for (; i >= 0; i--)
            {
                if (char.IsWhiteSpace(text[i]))
                    return i + 1;
            }
            return 0;
        }

        private static void InsertCompletion(
            string prompt,
            StringBuilder buffer,
            ref int cursorPos,
            ref int lastRenderLen,
            int tokenStart,
            int tokenLen,
            string completion,
            bool addDirSlash)
        {
            // Replace [tokenStart .. tokenStart+tokenLen) with "completion"
            buffer.Remove(tokenStart, tokenLen);
            buffer.Insert(tokenStart, completion);
            cursorPos = tokenStart + completion.Length;

            // If it's a directory and we have a single full match, optionally add trailing slash.
            if (addDirSlash && completion.Length > 0)
            {
                string full = completion;
                try
                {
                    // If it's a rooted path, check directly; else we can’t reliably know yet.
                    if (Path.IsPathRooted(full) && Directory.Exists(full))
                    {
                        char sep = Path.DirectorySeparatorChar;
                        if (!full.EndsWith(sep.ToString()) && !full.EndsWith("/"))
                        {
                            buffer.Insert(cursorPos, sep);
                            cursorPos++;
                        }
                    }
                }
                catch { }
            }
            // redraw
            RedrawPromptAndBuffer(prompt, buffer, cursorPos, ref lastRenderLen);
        }

        private static string LongestCommonPrefix(string[] items)
        {
            if (items == null || items.Length == 0) return "";
            if (items.Length == 1) return items[0];

            string first = items[0];
            int max = first.Length;

            for (int i = 1; i < items.Length; i++)
            {
                max = Math.Min(max, items[i].Length);
                int j = 0;
                for (; j < max; j++)
                {
                    if (char.ToLowerInvariant(first[j]) != char.ToLowerInvariant(items[i][j]))
                        break;
                }
                max = j;
                if (max == 0) break;
            }
            return first.Substring(0, max);
        }

        private static void RedrawPromptAndBuffer(string prompt, StringBuilder buffer, int cursorPos, ref int lastRenderLen)
        {
            string text = buffer?.ToString() ?? string.Empty;
            string line = (prompt ?? string.Empty) + text;

            // Use viewport width (what wraps visually)
            int w;
            try
            {
                int win = 0, buf = 0;
                try { win = Console.WindowWidth; } catch { }
                try { buf = Console.BufferWidth; } catch { }
                w = Math.Max(1, win > 0 ? win : (buf > 0 ? buf : 120));
            }
            catch { w = 120; }

            if (_promptAnchorTop < 0)
            {
                try { _promptAnchorTop = Console.CursorTop; } catch { _promptAnchorTop = 0; }
            }

            // How many rows did last render occupy?
            int prevRows = Math.Max(1, (lastRenderLen + w - 1) / w);
            if (lastRenderLen > 0 && (lastRenderLen % w) == 0) prevRows++; // exact wrap boundary -> clear one extra

            // Hide cursor to reduce flicker
            bool vis = true;
            try { vis = Console.CursorVisible; Console.CursorVisible = false; } catch { }

            // ---- CLEAR previous area without triggering ConPTY autowrap ----
            int run = Math.Max(0, w - 1);
            string blanks = new string(' ', run);

            for (int r = 0; r < prevRows; r++)
            {
                int row = _promptAnchorTop + r;
                try { Console.SetCursorPosition(0, row); } catch { }
                if (run > 0) Console.Write(blanks);                 // write w-1 spaces
                try { Console.SetCursorPosition(Math.Max(0, w - 1), row); } catch { }
                Console.Write(' ');                                  // last cell, no wrap
            }

            // ---- WRITE current content from the (old) anchor ----
            try { Console.SetCursorPosition(0, _promptAnchorTop); } catch { }
            Console.Write(line);

            // Desired caret position computed from logical cursor
            int logicalCursor = Math.Max(0, Math.Min(line.Length, (prompt?.Length ?? 0) + cursorPos));
            int caretRowWanted = _promptAnchorTop + (logicalCursor / w);
            int caretCol = logicalCursor % w;

            // Place caret (this may scroll if near bottom)
            try { Console.SetCursorPosition(caretCol, caretRowWanted); } catch { }

            // ---- RESYNC ANCHOR if ConPTY scrolled the viewport ----
            try
            {
                int caretRowActual = Console.CursorTop;             // after SetCursorPosition
                int rowsFromAnchor = logicalCursor / w;
                int newAnchor = caretRowActual - rowsFromAnchor;    // recompute top row for this logical line
                if (newAnchor < 0) newAnchor = 0;
                _promptAnchorTop = newAnchor;
            }
            catch { /* keep previous anchor if host blocks */ }

            lastRenderLen = line.Length;

            try { Console.CursorVisible = vis; } catch { }
        }

        private static void ReplaceCurrentLine(string prompt, string newText, StringBuilder buffer, ref int cursorPos, ref int lastRenderLen)
        {
            buffer.Clear();
            buffer.Append(newText);
            cursorPos = buffer.Length;
            RedrawPromptAndBuffer(prompt, buffer, cursorPos, ref lastRenderLen);
        }
        private static void PrintInColumns(string[] items, int consoleWidth)
        {
            if (items == null || items.Length == 0) return;
            if (consoleWidth < 1) consoleWidth = 80;

            int longest = 0;
            for (int i = 0; i < items.Length; i++)
                if (items[i] != null && items[i].Length > longest) longest = items[i].Length;
            longest += 2;

            int cols = Math.Max(1, consoleWidth / Math.Max(1, longest));

            for (int i = 0; i < items.Length; i++)
            {
                string s = items[i] ?? "";
                if (s.Length > consoleWidth) s = s.Substring(0, Math.Max(0, consoleWidth - 1));
                Console.Write(s.PadRight(longest));
                if ((i + 1) % cols == 0)
                    Console.WriteLine();
            }
            Console.WriteLine();
        }
    }
}
