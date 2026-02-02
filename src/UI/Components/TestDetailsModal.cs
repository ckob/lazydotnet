using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;

namespace lazydotnet.UI.Components;

public class TestDetailsModal(TestNode node, Action onClose) : Modal(node.Name, new Markup(""), onClose)
{
    private int _scrollOffset;
    private int _selectedLogicalIndex = -1;
    private readonly Lock _lock = new();
    private int _lastFrameIndex = -1;

    public override bool OnTick()
    {
        if (node.Status == TestStatus.Running)
        {
            var currentFrame = SpinnerHelper.GetCurrentFrameIndex(Spinner.Known.Dots);
            if (currentFrame != _lastFrameIndex)
            {
                _lastFrameIndex = currentFrame;
                return true;
            }
        }
        return false;
    }

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

    private List<TestOutputLine> BuildAllLines()
    {
        var lines = new List<TestOutputLine>();

        // Build metadata lines
        var statusColor = TestDetailsTab.GetStatusColor(node.Status);
        var statusIcon = TestDetailsTab.GetStatusIcon(node.Status);
        lines.Add(new TestOutputLine($"Status: {statusIcon} {node.Status}", statusColor));

        if (node.IsTest)
        {
            lines.Add(new TestOutputLine($"Duration: {node.Duration}ms"));
            if (node.FilePath != null)
            {
                var relativePath = PathHelper.GetRelativePath(node.FilePath);
                lines.Add(new TestOutputLine($"File: {relativePath}", "blue"));
                if (node.LineNumber != null)
                {
                    lines.Add(new TestOutputLine($"Line: {node.LineNumber}"));
                }
            }
        }
        else
        {
            var passedCount = GetCountByStatus(node, TestStatus.Passed);
            var failedCount = GetCountByStatus(node, TestStatus.Failed);
            var maxDuration = GetMaxDuration(node);

            lines.Add(new TestOutputLine($"Total Tests: {node.TestCount}"));
            lines.Add(new TestOutputLine($"Passed: {passedCount}", "green"));
            lines.Add(new TestOutputLine($"Failed: {failedCount}", "red"));
            lines.Add(new TestOutputLine($"Max Duration: {maxDuration}ms"));
        }

        lines.Add(new TestOutputLine(""));

        // Build output/error lines
        var output = node.GetOutputSnapshot();
        if (output.Count > 0)
        {
            lines.Add(new TestOutputLine("Output/Error", "bold underline"));
            lines.Add(new TestOutputLine(""));
            lines.AddRange(output);
        }
        else if (node.IsTest)
        {
            if (node.Status == TestStatus.Passed)
                lines.Add(new TestOutputLine("Test passed successfully.", "green"));
            else if (node.Status == TestStatus.Failed)
                lines.Add(new TestOutputLine("Test failed but no output was captured.", "red"));
            else if (node.Status == TestStatus.Running)
                lines.Add(new TestOutputLine("Test is currently running...", "yellow"));
            else
                lines.Add(new TestOutputLine("Test has not been run yet.", "dim"));
        }

        return lines;
    }

    private static int GetCountByStatus(TestNode node, TestStatus status)
    {
        if (node.IsTest) return node.Status == status ? 1 : 0;
        return node.Children.Sum(c => GetCountByStatus(c, status));
    }

    private static double GetMaxDuration(TestNode node)
    {
        if (node.IsTest) return node.Duration;
        return node.Children.Count > 0 ? node.Children.Max(GetMaxDuration) : 0;
    }

    private void MoveUp()
    {
        lock (_lock)
        {
            var lines = BuildAllLines();
            if (lines.Count == 0) return;
            if (_selectedLogicalIndex == -1)
            {
                _selectedLogicalIndex = lines.Count - 1;
                return;
            }

            var index = _selectedLogicalIndex;
            while (index > 0)
            {
                index--;
                if (!string.IsNullOrWhiteSpace(lines[index].Text))
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
            var lines = BuildAllLines();
            if (lines.Count == 0) return;

            if (_selectedLogicalIndex == -1)
            {
                _selectedLogicalIndex = 0;
                return;
            }

            var index = _selectedLogicalIndex;
            while (index < lines.Count - 1)
            {
                index++;
                if (!string.IsNullOrWhiteSpace(lines[index].Text))
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

            var allLines = BuildAllLines();
            var physicalLines = BuildPhysicalLines(allLines, renderWidth);
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

    private List<PhysicalLine> BuildPhysicalLines(List<TestOutputLine> lines, int renderWidth)
    {
        var physicalLines = new List<PhysicalLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            var logical = lines[i];
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
