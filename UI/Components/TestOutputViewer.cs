using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;

namespace lazydotnet.UI.Components;

public class TestOutputViewer : IKeyBindable
{
    private List<TestOutputLine> _lines = [];
    private int _scrollOffset;
    private int _selectedLogicalIndex = -1;
    private readonly Lock _lock = new();

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        yield return new KeyBinding("k", "up", () => Task.Run(MoveUp),
            k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K ||
                 k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.P }, false);
        yield return new KeyBinding("j", "down", () => Task.Run(MoveDown),
            k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J ||
                 k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.N }, false);
    }

    public void SetOutput(List<TestOutputLine> lines)
    {
        lock (_lock)
        {
            _lines = lines;
            if (_selectedLogicalIndex >= _lines.Count)
                _selectedLogicalIndex = _lines.Count > 0 ? _lines.Count - 1 : -1;
        }
    }

    private void MoveUp()
    {
        lock (_lock)
        {
            if (_lines.Count == 0) return;

            if (_selectedLogicalIndex == -1)
            {
                _selectedLogicalIndex = _lines.Count - 1;
                return;
            }

            var index = _selectedLogicalIndex;
            while (index > 0)
            {
                index--;
                if (!string.IsNullOrWhiteSpace(_lines[index].Text))
                {
                    _selectedLogicalIndex = index;
                    return;
                }
            }

            _selectedLogicalIndex = 0;
        }
    }

    private void MoveDown()
    {
        lock (_lock)
        {
            if (_lines.Count == 0 || _selectedLogicalIndex == -1) return;

            var index = _selectedLogicalIndex;
            while (index < _lines.Count - 1)
            {
                index++;
                if (!string.IsNullOrWhiteSpace(_lines[index].Text))
                {
                    _selectedLogicalIndex = index;
                    return;
                }
            }

            _selectedLogicalIndex = -1;
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
            if (_lines.Count == 0) return new Markup("");

            var visibleRows = Math.Max(1, height);
            var renderWidth = Math.Max(1, width - 4);

            var physicalLines = BuildPhysicalLines(renderWidth);
            UpdateScrollOffset(physicalLines, visibleRows);

            var table = CreateOutputTable(renderWidth);
            RenderPhysicalLines(table, physicalLines, visibleRows, isActive);

            return table;
        }
    }

    private List<PhysicalLine> BuildPhysicalLines(int renderWidth)
    {
        var physicalLines = new List<PhysicalLine>();
        for (var i = 0; i < _lines.Count; i++)
        {
            var logical = _lines[i];
            var wrapped = WrapText(logical.Text, renderWidth);

            physicalLines.AddRange(wrapped.Select(w =>
                new PhysicalLine { Text = w, LogicalIndex = i, Style = logical.Style }));
        }
        return physicalLines;
    }

    private void UpdateScrollOffset(List<PhysicalLine> physicalLines, int visibleRows)
    {
        if (_selectedLogicalIndex != -1)
        {
            var (first, last) = GetPhysicalIndicesForLogical(_selectedLogicalIndex, physicalLines);

            if (first != -1)
            {
                if (first < _scrollOffset) _scrollOffset = first;
                if (last >= _scrollOffset + visibleRows) _scrollOffset = last - visibleRows + 1;
            }
        }
        else
        {
            _scrollOffset = Math.Max(0, physicalLines.Count - visibleRows);
        }

        _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, Math.Max(0, physicalLines.Count - visibleRows)));
    }

    private static (int first, int last) GetPhysicalIndicesForLogical(int logicalIndex, List<PhysicalLine> physicalLines)
    {
        var first = -1;
        var last = -1;
        for (var i = 0; i < physicalLines.Count; i++)
        {
            if (physicalLines[i].LogicalIndex == logicalIndex)
            {
                if (first == -1) first = i;
                last = i;
            }
        }
        return (first, last);
    }

    private static Table CreateOutputTable(int renderWidth)
    {
        return new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .NoSafeBorder()
            .Expand()
            .AddColumn(new TableColumn("Output").NoWrap().Width(renderWidth));
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
                var contentMarkup = !string.IsNullOrEmpty(line.Style)
                    ? $"[{line.Style}]{escapedText}[/]"
                    : escapedText;
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
}
