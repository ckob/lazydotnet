using lazydotnet.Core;
using lazydotnet.Services;
using Spectre.Console;
using Spectre.Console.Rendering;
using TextCopy;

namespace lazydotnet.UI.Components;

public class TestDetailsModal : Modal
{
    private readonly TestNode _node;
    private readonly IEditorService? _editorService;

    private int _scrollOffset;
    private int _selectedLogicalIndex = -1;
    private readonly Lock _lock = new();
    private int _lastFrameIndex = -1;

    private bool _isVisualMode;
    private int _visualSelectionStart = -1;
    private int _visualSelectionEnd = -1;

    private List<DetailLine>? _cachedLines;
    private int _cachedOutputCount = -1;
    private TestStatus? _cachedStatus;
    private bool _autoJumpedToFailure;

    public TestDetailsModal(TestNode node, Action onClose, IEditorService? editorService = null)
        : base(node.Name, new Markup(""), onClose)
    {
        _node = node;
        _editorService = editorService;
    }

    private enum SectionKind { Test, Info, Failure, Stack, Stdout }

    private sealed record DetailLine(string Text, string? Style, SectionKind Section, bool IsHeader = false, bool Selectable = true);

    public override bool OnTick()
    {
        if (_node.Status == TestStatus.Running)
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

        yield return new KeyBinding("k/↑", "up", () => { MoveUp(); return Task.CompletedTask; },
            k => k is { Key: ConsoleKey.UpArrow or ConsoleKey.K, Modifiers: 0 }
                 || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.P }, false);

        yield return new KeyBinding("j/↓", "down", () => { MoveDown(); return Task.CompletedTask; },
            k => k is { Key: ConsoleKey.DownArrow or ConsoleKey.J, Modifiers: 0 }
                 || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.N }, false);

        yield return new KeyBinding("g", "top", () => { JumpTop(); return Task.CompletedTask; },
            k => k is { KeyChar: 'g', Modifiers: 0 }, false);

        yield return new KeyBinding("G", "bottom", () => { JumpBottom(); return Task.CompletedTask; },
            k => k is { KeyChar: 'G' }, false);

        yield return new KeyBinding("v", "range select", () => { ToggleVisualMode(); return Task.CompletedTask; },
            k => k is { KeyChar: 'v', Modifiers: 0 }, false);

        yield return new KeyBinding("y", "copy line", () => { CopySelection(); return Task.CompletedTask; },
            k => k is { KeyChar: 'y', Modifiers: 0 }, true, LongDescription: "copy selected line(s)");

        yield return new KeyBinding("Y", "copy report", () => { CopyFullReport(); return Task.CompletedTask; },
            k => k is { KeyChar: 'Y' }, true, LongDescription: "copy full failure report (markdown)");

        yield return new KeyBinding("c", "copy error", () => { CopyErrorAndStack(); return Task.CompletedTask; },
            k => k is { KeyChar: 'c', Modifiers: 0 }, true, LongDescription: "copy error + stack trace");

        if (_node.FilePath != null && _editorService != null)
        {
            yield return new KeyBinding("e", "edit", async () =>
            {
                await _editorService.OpenFileAsync(_node.FilePath, _node.LineNumber);
            }, k => k is { Key: ConsoleKey.E, Modifiers: 0 }, true, LongDescription: "open file in editor");
        }
    }

    private List<DetailLine> GetLines()
    {
        var outputSnapshot = _node.GetOutputSnapshot();
        if (_cachedLines != null
            && _cachedOutputCount == outputSnapshot.Count
            && _cachedStatus == _node.Status)
        {
            return _cachedLines;
        }

        _cachedLines = BuildLines(outputSnapshot);
        _cachedOutputCount = outputSnapshot.Count;
        _cachedStatus = _node.Status;
        return _cachedLines;
    }

    private List<DetailLine> BuildLines(List<TestOutputLine> output)
    {
        var lines = new List<DetailLine>();
        AppendTestSection(lines);

        var errorLines = output.Where(o => o.Section == TestOutputSection.Error).ToList();
        var stackLines = output.Where(o => o.Section == TestOutputSection.Stack).ToList();
        var stdoutLines = output.Where(o => o.Section == TestOutputSection.Stdout).ToList();
        var infoLines = output.Where(o => o.Section == TestOutputSection.Generic).ToList();

        AppendOutputSection(lines, infoLines, SectionKind.Info, "Info", "dim");
        AppendOutputSection(lines, errorLines, SectionKind.Failure, "Failure", "red");
        AppendOutputSection(lines, stackLines, SectionKind.Stack, "Stack Trace", "dim");
        AppendOutputSection(lines, stdoutLines, SectionKind.Stdout, "Output", null);

        if (infoLines.Count == 0 && errorLines.Count == 0 && stackLines.Count == 0 && stdoutLines.Count == 0 && _node.IsTest)
        {
            AppendBlank(lines, SectionKind.Test);
            var (msg, style) = StatusFallbackMessage(_node.Status);
            lines.Add(new DetailLine(msg, style, SectionKind.Test, Selectable: false));
        }

        return lines;
    }

    private void AppendTestSection(List<DetailLine> lines)
    {
        AppendSectionHeader(lines, SectionKind.Test, "Test");
        var statusColor = TestDetailsTab.GetStatusColor(_node.Status);
        var statusIcon = TestDetailsTab.GetStatusIcon(_node.Status);
        lines.Add(new DetailLine($"Status:   {statusIcon} {_node.Status}", statusColor, SectionKind.Test));

        if (_node.IsTest)
        {
            lines.Add(new DetailLine($"Duration: {_node.Duration}ms", null, SectionKind.Test));
            if (_node.FilePath != null)
            {
                var relativePath = PathHelper.GetRelativePath(_node.FilePath);
                var fileLine = _node.LineNumber != null ? $"{relativePath}:{_node.LineNumber}" : relativePath;
                lines.Add(new DetailLine($"File:     {fileLine}", "blue", SectionKind.Test));
            }
            return;
        }

        lines.Add(new DetailLine($"Total:    {_node.TestCount}", null, SectionKind.Test));
        lines.Add(new DetailLine($"Passed:   {GetCountByStatus(_node, TestStatus.Passed)}", "green", SectionKind.Test));
        lines.Add(new DetailLine($"Failed:   {GetCountByStatus(_node, TestStatus.Failed)}", "red", SectionKind.Test));
        lines.Add(new DetailLine($"Max dur:  {GetMaxDuration(_node)}ms", null, SectionKind.Test));
    }

    private static void AppendOutputSection(List<DetailLine> lines, List<TestOutputLine> sourceLines, SectionKind section, string title, string? defaultStyle)
    {
        if (sourceLines.Count == 0) return;
        AppendBlank(lines, section);
        AppendSectionHeader(lines, section, title);
        foreach (var l in sourceLines)
        {
            lines.Add(new DetailLine(l.Text, l.Style ?? defaultStyle, section));
        }
    }

    private static (string Message, string Style) StatusFallbackMessage(TestStatus status) => status switch
    {
        TestStatus.Passed => ("Test passed successfully.", "green"),
        TestStatus.Failed => ("Test failed but no output was captured.", "red"),
        TestStatus.Running => ("Test is currently running...", "yellow"),
        _ => ("Test has not been run yet.", "dim")
    };

    private static void AppendSectionHeader(List<DetailLine> lines, SectionKind section, string title)
    {
        lines.Add(new DetailLine($"▌ {title}", "bold yellow", section, IsHeader: true, Selectable: false));
    }

    private static void AppendBlank(List<DetailLine> lines, SectionKind section)
    {
        lines.Add(new DetailLine("", null, section, Selectable: false));
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
            var lines = GetLines();
            if (lines.Count == 0) return;
            if (_selectedLogicalIndex == -1)
            {
                _selectedLogicalIndex = FindSelectable(lines, lines.Count - 1, -1);
                if (_isVisualMode) _visualSelectionEnd = _selectedLogicalIndex;
                return;
            }
            var next = FindSelectable(lines, _selectedLogicalIndex - 1, -1);
            if (next == -1) return;
            _selectedLogicalIndex = next;
            if (_isVisualMode) _visualSelectionEnd = next;
        }
    }

    private void MoveDown()
    {
        lock (_lock)
        {
            var lines = GetLines();
            if (lines.Count == 0) return;
            if (_selectedLogicalIndex == -1)
            {
                _selectedLogicalIndex = FindSelectable(lines, 0, 1);
                if (_isVisualMode) _visualSelectionEnd = _selectedLogicalIndex;
                return;
            }
            var next = FindSelectable(lines, _selectedLogicalIndex + 1, 1);
            if (next == -1) return;
            _selectedLogicalIndex = next;
            if (_isVisualMode) _visualSelectionEnd = next;
        }
    }

    private void JumpTop()
    {
        lock (_lock)
        {
            var lines = GetLines();
            _selectedLogicalIndex = FindSelectable(lines, 0, 1);
            if (_isVisualMode) _visualSelectionEnd = _selectedLogicalIndex;
        }
    }

    private void JumpBottom()
    {
        lock (_lock)
        {
            var lines = GetLines();
            _selectedLogicalIndex = FindSelectable(lines, lines.Count - 1, -1);
            if (_isVisualMode) _visualSelectionEnd = _selectedLogicalIndex;
        }
    }

    private void ToggleVisualMode()
    {
        lock (_lock)
        {
            if (_isVisualMode)
            {
                _isVisualMode = false;
                _visualSelectionStart = -1;
                _visualSelectionEnd = -1;
                return;
            }
            var lines = GetLines();
            if (lines.Count == 0) return;
            if (_selectedLogicalIndex == -1)
            {
                _selectedLogicalIndex = FindSelectable(lines, 0, 1);
                if (_selectedLogicalIndex == -1) return;
            }
            _isVisualMode = true;
            _visualSelectionStart = _selectedLogicalIndex;
            _visualSelectionEnd = _selectedLogicalIndex;
        }
    }

    private static int FindSelectable(List<DetailLine> lines, int start, int step)
    {
        if (start < 0 || start >= lines.Count) start = step > 0 ? 0 : lines.Count - 1;
        for (var i = start; i >= 0 && i < lines.Count; i += step)
        {
            if (lines[i].Selectable) return i;
        }
        return -1;
    }

    private void CopySelection()
    {
        lock (_lock)
        {
            var lines = GetLines();
            if (lines.Count == 0) return;
            string text;
            if (_isVisualMode)
            {
                var (s, e) = OrderedSelection();
                if (s < 0) return;
                text = string.Join(Environment.NewLine, lines.Skip(s).Take(e - s + 1).Where(l => l.Selectable).Select(l => l.Text));
            }
            else
            {
                if (_selectedLogicalIndex < 0) return;
                text = lines[_selectedLogicalIndex].Text;
            }
            CopyToClipboard(text, _isVisualMode ? "Range copied" : "Line copied");
        }
    }

    private void CopyErrorAndStack()
    {
        lock (_lock)
        {
            var text = TestDetailsReport.BuildErrorAndStack(_node);
            if (string.IsNullOrEmpty(text))
            {
                Notification.Show("No error to copy", NotificationType.Error);
                return;
            }
            CopyToClipboard(text, "Error + stack copied");
        }
    }

    private void CopyFullReport()
    {
        lock (_lock)
        {
            var text = TestDetailsReport.BuildMarkdownReport(_node);
            CopyToClipboard(text, "Report copied");
        }
    }

    private static void CopyToClipboard(string text, string successMessage)
    {
        try
        {
            ClipboardService.SetText(Markup.Remove(text));
            Notification.Show(successMessage);
        }
        catch (Exception ex)
        {
            Notification.Show($"Clipboard failed: {ex.Message}", NotificationType.Error);
        }
    }

    private (int start, int end) OrderedSelection()
    {
        if (_visualSelectionStart < 0 || _visualSelectionEnd < 0) return (-1, -1);
        return _visualSelectionStart <= _visualSelectionEnd
            ? (_visualSelectionStart, _visualSelectionEnd)
            : (_visualSelectionEnd, _visualSelectionStart);
    }

    private struct PhysicalLine
    {
        public string Text;
        public int LogicalIndex;
        public string? Style;
        public bool IsHeader;
    }

    public override IRenderable GetRenderable(int width, int height)
    {
        lock (_lock)
        {
            var modalWidth = width * 9 / 10;
            var modalHeight = height * 9 / 10;
            var renderWidth = Math.Max(10, modalWidth - 8);
            var footerRows = 2;
            var visibleRows = Math.Max(1, modalHeight - 4 - footerRows);

            var lines = GetLines();
            MaybeAutoJumpToFailure(lines);

            var physicalLines = BuildPhysicalLines(lines, renderWidth);
            UpdateScrollOffset(physicalLines, visibleRows);

            var contentTable = new Table().Border(TableBorder.None).HideHeaders().NoSafeBorder().Expand();
            contentTable.AddColumn(new TableColumn("Content").NoWrap().Width(renderWidth));
            RenderPhysicalLines(contentTable, physicalLines, visibleRows);

            var footer = BuildFooter();

            var grid = new Grid();
            grid.AddColumn();
            grid.AddRow(contentTable);
            grid.AddRow(new Rule { Style = new Style(Color.Grey) });
            grid.AddRow(footer);

            return new Panel(new Padder(grid, new Padding(2, 1, 2, 1)))
            {
                Header = new PanelHeader($"[bold yellow] {BuildTitle()} [/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Blue),
                Expand = false,
                Width = modalWidth,
                Height = modalHeight
            };
        }
    }

    private string BuildTitle()
    {
        var icon = TestDetailsTab.GetStatusIcon(_node.Status);
        var color = TestDetailsTab.GetStatusColor(_node.Status);
        var visual = _isVisualMode ? " [dim](visual)[/]" : "";
        return $"[{color}]{icon}[/] {Markup.Escape(_node.Name)}{visual}";
    }

    private IRenderable BuildFooter()
    {
        var hasError = _node.Status == TestStatus.Failed;
        var copy = hasError ? "[bold]y[/] line · [bold]Y[/] report · [bold]c[/] error+stack" : "[bold]y[/] line · [bold]Y[/] report";
        var edit = _node.FilePath != null && _editorService != null ? " · [bold]e[/] edit" : "";
        var visual = _isVisualMode ? "[yellow]v[/] exit range" : "[bold]v[/] range";
        return new Markup($"[dim]{copy} · {visual} · g/G top/bot · esc close{edit}[/]");
    }

    private void MaybeAutoJumpToFailure(List<DetailLine> lines)
    {
        if (_autoJumpedToFailure) return;
        if (_node.Status != TestStatus.Failed) return;
        var idx = lines.FindIndex(l => l.Section == SectionKind.Failure && l.Selectable);
        if (idx >= 0)
        {
            _selectedLogicalIndex = idx;
        }
        _autoJumpedToFailure = true;
    }

    private List<PhysicalLine> BuildPhysicalLines(List<DetailLine> lines, int renderWidth)
    {
        var physicalLines = new List<PhysicalLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            var logical = lines[i];
            var wrapped = WrapText(logical.Text, renderWidth);
            physicalLines.AddRange(wrapped.Select(w =>
                new PhysicalLine { Text = w, LogicalIndex = i, Style = logical.Style, IsHeader = logical.IsHeader }));
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
                const int margin = 2;
                if (first < _scrollOffset + margin) _scrollOffset = Math.Max(0, first - margin);
                if (last >= _scrollOffset + visibleRows - margin) _scrollOffset = last - visibleRows + margin + 1;
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
        var (vs, ve) = OrderedSelection();
        var renderedCount = 0;
        var start = _scrollOffset;

        for (var i = start; i < physicalLines.Count && renderedCount < visibleRows; i++)
        {
            var line = physicalLines[i];
            var isSelected = line.LogicalIndex == _selectedLogicalIndex;
            var inRange = _isVisualMode && vs >= 0 && line.LogicalIndex >= vs && line.LogicalIndex <= ve;
            var escaped = Markup.Escape(line.Text);

            if (isSelected || inRange)
            {
                var bg = inRange && !isSelected ? "white" : "blue";
                table.AddRow(new Markup($"[black on {bg}]{escaped}[/]"));
            }
            else if (!string.IsNullOrEmpty(line.Style))
            {
                table.AddRow(new Markup($"[{line.Style}]{escaped}[/]"));
            }
            else
            {
                table.AddRow(new Markup(escaped));
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
