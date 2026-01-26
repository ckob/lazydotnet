using System.Text.RegularExpressions;
using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public partial class LogViewer : IKeyBindable
{
    private readonly List<string> _logs = [];
    private readonly Lock _lock = new();
    private static readonly Regex StyleRegex = GetStyleRegex();

    private int _scrollOffset;
    private int _selectedLogicalIndex = -1; // -1 means auto-scroll
    public bool IsStreaming => _selectedLogicalIndex == -1;

    private const int MaxLogLines = 1000;

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        yield return new KeyBinding("k", "up", () => Task.Run(MoveUp),
            k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K ||
                 (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.P), false);
        yield return new KeyBinding("j", "down", () => Task.Run(MoveDown),
            k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J ||
                 (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.N), false);
        yield return new KeyBinding("pgup", "page up", () => Task.Run(() => PageUp(10)),
            k => k.Key == ConsoleKey.PageUp ||
                 (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.U), false);
        yield return new KeyBinding("pgdn", "page down", () => Task.Run(() => PageDown(10)),
            k => k.Key == ConsoleKey.PageDown ||
                 (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.D), false);
        yield return new KeyBinding("esc", "resume stream", () =>
        {
            lock (_lock) { _selectedLogicalIndex = -1; }
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.Escape, false);
    }

    public bool HandleInput(ConsoleKeyInfo key)
    {
        var binding = GetKeyBindings().FirstOrDefault(b => b.Match(key));
        if (binding != null)
        {
            _ = binding.Action();
            return true;
        }

        return false;
    }

    public void AddLog(string message)
    {
        lock (_lock)
        {
            _logs.Add(message);
            if (_logs.Count > MaxLogLines)
            {
                _logs.RemoveAt(0);
                if (_selectedLogicalIndex >= 0) _selectedLogicalIndex--;
            }
        }
    }

    public void MoveUp()
    {
        lock (_lock)
        {
            if (_logs.Count == 0) return;

            if (_selectedLogicalIndex == -1)
            {
                _selectedLogicalIndex = _logs.Count - 1;
                return;
            }

            var index = _selectedLogicalIndex;
            while (index > 0)
            {
                index--;
                if (!string.IsNullOrWhiteSpace(_logs[index]))
                {
                    _selectedLogicalIndex = index;
                    return;
                }
            }
        }
    }

    public void MoveDown()
    {
        lock (_lock)
        {
            if (_logs.Count == 0 || _selectedLogicalIndex == -1) return;

            var index = _selectedLogicalIndex;
            while (index < _logs.Count - 1)
            {
                index++;
                if (!string.IsNullOrWhiteSpace(_logs[index]))
                {
                    _selectedLogicalIndex = index;
                    return;
                }
            }

            _selectedLogicalIndex = -1; // Resume auto-scroll
        }
    }

    public void PageUp(int pageSize)
    {
        lock (_lock)
        {
            if (_logs.Count == 0) return;
            if (_selectedLogicalIndex == -1) _selectedLogicalIndex = _logs.Count - 1;
            _selectedLogicalIndex = Math.Max(0, _selectedLogicalIndex - pageSize);
        }
    }

    public void PageDown(int pageSize)
    {
        lock (_lock)
        {
            if (_logs.Count == 0 || _selectedLogicalIndex == -1) return;
            if (_selectedLogicalIndex + pageSize >= _logs.Count - 1)
                _selectedLogicalIndex = -1;
            else
                _selectedLogicalIndex += pageSize;
        }
    }

    private struct PhysicalLine
    {
        public string Text;
        public int LogicalIndex;
        public string? Style;
    }

    public IRenderable GetContent(int height, int width, bool isActive)
    {
        lock (_lock)
        {
            if (_logs.Count == 0) return new Markup("");

            var visibleRows = Math.Max(1, height);
            var renderWidth = Math.Max(1, width - 4);

            var physicalLines = BuildPhysicalLines(renderWidth);
            UpdateScrollOffset(physicalLines, visibleRows);

            var table = CreateLogTable(renderWidth);
            RenderPhysicalLines(table, physicalLines, visibleRows, isActive);

            return table;
        }
    }

    private List<PhysicalLine> BuildPhysicalLines(int renderWidth)
    {
        var physicalLines = new List<PhysicalLine>();
        for (var i = 0; i < _logs.Count; i++)
        {
            var logical = _logs[i];
            var (cleanText, style) = ExtractStyle(logical);
            var wrapped = WrapText(cleanText, renderWidth);

            physicalLines.AddRange(wrapped.Select(w =>
                new PhysicalLine { Text = w, LogicalIndex = i, Style = style }));
        }
        return physicalLines;
    }

    private void UpdateScrollOffset(List<PhysicalLine> physicalLines, int visibleRows)
    {
        if (_selectedLogicalIndex == -1)
        {
            _scrollOffset = Math.Max(0, physicalLines.Count - visibleRows);
        }
        else
        {
            var (first, last) = GetPhysicalIndicesForLogical(_selectedLogicalIndex, physicalLines);

            if (first != -1)
            {
                if (first < _scrollOffset) _scrollOffset = first;
                if (last >= _scrollOffset + visibleRows) _scrollOffset = last - visibleRows + 1;
            }
        }

        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, Math.Max(0, physicalLines.Count - visibleRows)));
    }

    private static (int first, int last) GetPhysicalIndicesForLogical(int logicalIndex, List<PhysicalLine> physicalLines)
    {
        var first = -1;
        var last = -1;
        for (var i = 0; i < physicalLines.Count; i++)
        {
            if (physicalLines[i].LogicalIndex != logicalIndex)
                continue;

            if (first == -1) first = i;
            last = i;
        }
        return (first, last);
    }

    private static Table CreateLogTable(int renderWidth)
    {
        return new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .NoSafeBorder()
            .Expand()
            .AddColumn(new TableColumn("Log").NoWrap().Width(renderWidth));
    }

    private void RenderPhysicalLines(Table table, List<PhysicalLine> physicalLines, int visibleRows, bool isActive)
    {
        var start = _scrollOffset;
        var renderedCount = 0;

        for (var i = start; i < physicalLines.Count && renderedCount < visibleRows; i++)
        {
            var line = physicalLines[i];
            var isSelected = line.LogicalIndex == _selectedLogicalIndex;
            var escapedText = Markup.Escape(line.Text);

            if (isSelected)
            {
                var selectionStyle = isActive ? "black on white" : "black on silver";
                table.AddRow(new Markup($"[{selectionStyle}]{escapedText}[/]"));
            }
            else
            {
                var contentMarkup = string.IsNullOrEmpty(line.Style)
                    ? escapedText
                    : $"[{line.Style}]{escapedText}[/]";
                table.AddRow(new Markup(contentMarkup));
            }

            renderedCount++;
        }

        while (renderedCount < visibleRows)
        {
            table.AddRow(new Markup(""));
            renderedCount++;
        }
    }

    private static (string Text, string? Style) ExtractStyle(string input)
    {
        var match = StyleRegex.Match(input);
        return match.Success
            ? (match.Groups["text"].Value, match.Groups["style"].Value)
            : (input, null);
    }

    private static List<string> WrapText(string text, int width)
    {
        if (string.IsNullOrEmpty(text)) return [""];
        if (text.Length <= width) return [text];

        var lines = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            var remaining = text.Length - start;
            var length = Math.Min(width, remaining);

            if (start + length < text.Length)
            {
                var lastSpace = text.LastIndexOf(' ', start + length, length);
                if (lastSpace > start)
                {
                    lines.Add(text.Substring(start, lastSpace - start));
                    start = lastSpace + 1;
                    continue;
                }
            }

            lines.Add(text.Substring(start, length));
            start += length;
        }

        return lines;
    }

    [GeneratedRegex(@"^\[(?<style>[^\]]+)\](?<text>.*)\[/\]$", RegexOptions.Compiled)]
    private static partial Regex GetStyleRegex();
}