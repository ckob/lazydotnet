using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;

namespace lazydotnet.UI.Components;

public class TestDetailsModal(string title, List<TestOutputLine> details, Action onClose) : Modal(title, new Markup(""), onClose)
{
    private readonly List<TestOutputLine> _lines = details;
    private int _scrollOffset;
    private int _selectedLogicalIndex = -1;
    private readonly Lock _lock = new();

    public override IEnumerable<KeyBinding> GetKeyBindings()
    {
        foreach (var b in base.GetKeyBindings()) yield return b;

        yield return new KeyBinding("k", "up", () => { MoveUp(); return Task.CompletedTask; },
            k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K ||
                 k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.P }, false);
        yield return new KeyBinding("j", "down", () => { MoveDown(); return Task.CompletedTask; },
            k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J ||
                 k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.N }, false);
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
            if (_lines.Count == 0) return;

            if (_selectedLogicalIndex == -1)
            {
                _selectedLogicalIndex = 0;
                return;
            }

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
        }
    }

    private struct PhysicalLine
    {
        public string Text;
        public int LogicalIndex;
        public string? Style;
    }

    public override IRenderable GetRenderable(int width, int height)
    {
        lock (_lock)
        {
            var modalWidth = width * 8 / 10;
            var modalHeight = height * 8 / 10;
            var renderWidth = modalWidth - 8;
            var visibleRows = modalHeight - 4;

            var physicalLines = BuildPhysicalLines(renderWidth);
            UpdateScrollOffset(physicalLines, visibleRows);

            var table = new Table().Border(TableBorder.None).HideHeaders().NoSafeBorder().Expand();
            table.AddColumn(new TableColumn("Content").NoWrap().Width(renderWidth));

            RenderPhysicalLines(table, physicalLines, visibleRows);

            return new Panel(new Padder(table, new Padding(2, 1, 2, 1)))
            {
                Header = new PanelHeader($"[bold yellow] {Title} [/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Blue),
                Expand = false,
                Width = modalWidth,
                Height = modalHeight
            };
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
            var first = physicalLines.FindIndex(p => p.LogicalIndex == _selectedLogicalIndex);
            var last = physicalLines.FindLastIndex(p => p.LogicalIndex == _selectedLogicalIndex);

        if (first != -1)
        {
            if (first < _scrollOffset) _scrollOffset = first;
            if (last >= _scrollOffset + visibleRows) _scrollOffset = last - visibleRows + 1;
        }
    }
    else
    {
        _scrollOffset = 0;
    }
    _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, Math.Max(0, physicalLines.Count - visibleRows)));
}


    private void RenderPhysicalLines(Table table, List<PhysicalLine> physicalLines, int visibleRows)
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
                table.AddRow(new Markup($"[black on white]{escapedText}[/]"));
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
                    lines.Add(text[start..lastSpace]);
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
