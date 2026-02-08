using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;
using lazydotnet.UI.Components;

namespace lazydotnet.UI;

public class ExecutionTab : IProjectTab
{
    private string? _currentProjectPath;
    private string? _currentProjectName;
    private readonly Lock _lock = new();
    private int _scrollOffset;
    private int _selectedIndex = -1;
    private bool _needsFinalRefresh;

    public string Title
    {
        get
        {
            if (IsStreaming) return "Execution";
            return "Execution (Paused)";
        }
    }


    public Action<Modal>? RequestModal { get; set; }
    public Action? RequestRefresh { get; set; }
    public Action<string>? RequestSelectProject { get; set; }

    public void ClearData()
    {
        lock (_lock)
        {
            _currentProjectPath = null;
            _currentProjectName = null;
        }
    }

    public bool IsLoaded(string projectPath) => _currentProjectPath == projectPath;

    public Task LoadAsync(string projectPath, string projectName, bool force = false)
    {
        lock (_lock)
        {
            if (_currentProjectPath != projectPath)
            {
                _selectedIndex = -1;
                _scrollOffset = 0;
            }
            _currentProjectPath = projectPath;
            _currentProjectName = projectName;
        }
        return Task.CompletedTask;
    }

    private bool IsStreaming => _selectedIndex == -1;

    public bool OnTick()
    {
        if (_currentProjectPath == null) return false;
        var isRunning = ExecutionService.Instance.IsRunning(_currentProjectPath);
        if (!isRunning && _needsFinalRefresh)
        {
            _needsFinalRefresh = false;
            return true;
        }

        if (isRunning)
        {
            _needsFinalRefresh = true;
            return true;
        }

        return false;
    }

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        yield return new KeyBinding("r", "run project", async () =>
        {
            if (_currentProjectPath != null && _currentProjectName != null)
            {
                await ExecutionService.Instance.StartProjectAsync(_currentProjectPath, _currentProjectName);
                RequestRefresh?.Invoke();
            }
        }, k => k.KeyChar == 'r');

        yield return new KeyBinding("s", "stop project", async () =>
        {
            if (_currentProjectPath != null)
            {
                await ExecutionService.Instance.StopProjectAsync(_currentProjectPath);
                RequestRefresh?.Invoke();
            }
        }, k => k.KeyChar == 's');

        yield return new KeyBinding("c", "clear logs", () =>
        {
            if (_currentProjectPath != null && _currentProjectName != null)
            {
                var state = ExecutionService.Instance.GetOrCreateState(_currentProjectPath, _currentProjectName);
                state.ClearLogs();
                RequestRefresh?.Invoke();
            }
            return Task.CompletedTask;
        }, k => k.KeyChar == 'c');

        yield return new KeyBinding("k/↑/ctrl+p", "up", () =>
        {
            MoveUp();
            return Task.CompletedTask;
        }, k => k.Key is ConsoleKey.UpArrow or ConsoleKey.K || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.P }, false);

        yield return new KeyBinding("j/↓/ctrl+n", "down", () =>
        {
            MoveDown();
            return Task.CompletedTask;
        }, k => k.Key is ConsoleKey.DownArrow or ConsoleKey.J || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.N }, false);

        yield return new KeyBinding("pgup/ctrl+u", "page up", () =>
        {
            PageUp(10);
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.PageUp || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.U }, false);

        yield return new KeyBinding("pgdn/ctrl+d", "page down", () =>
        {
            PageDown(10);
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.PageDown || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.D }, false);

        yield return new KeyBinding("esc", "resume stream", () =>
        {
            lock (_lock) { _selectedIndex = -1; }
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.Escape, false);
    }

    private void MoveUp()
    {
        if (_currentProjectPath == null || _currentProjectName == null) return;
        var state = ExecutionService.Instance.GetOrCreateState(_currentProjectPath, _currentProjectName);
        lock (state.LogLock)
        {
            if (state.Logs.Count == 0) return;
            if (_selectedIndex == -1) _selectedIndex = state.Logs.Count - 1;
            else if (_selectedIndex > 0) _selectedIndex--;
        }
    }

    private void MoveDown()
    {
        if (_currentProjectPath == null || _currentProjectName == null) return;
        var state = ExecutionService.Instance.GetOrCreateState(_currentProjectPath, _currentProjectName);
        lock (state.LogLock)
        {
            if (state.Logs.Count == 0 || _selectedIndex == -1) return;
            if (_selectedIndex < state.Logs.Count - 1) _selectedIndex++;
            else _selectedIndex = -1; // Resume auto-scroll
        }
    }

    private void PageUp(int pageSize)
    {
        if (_currentProjectPath == null || _currentProjectName == null) return;
        var state = ExecutionService.Instance.GetOrCreateState(_currentProjectPath, _currentProjectName);
        lock (state.LogLock)
        {
            if (state.Logs.Count == 0) return;
            if (_selectedIndex == -1) _selectedIndex = state.Logs.Count - 1;
            _selectedIndex = Math.Max(0, _selectedIndex - pageSize);
        }
    }

    private void PageDown(int pageSize)
    {
        if (_currentProjectPath == null || _currentProjectName == null) return;
        var state = ExecutionService.Instance.GetOrCreateState(_currentProjectPath, _currentProjectName);
        lock (state.LogLock)
        {
            if (state.Logs.Count == 0 || _selectedIndex == -1) return;
            if (_selectedIndex + pageSize >= state.Logs.Count - 1)
                _selectedIndex = -1;
            else
                _selectedIndex += pageSize;
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
            if (_currentProjectPath == null || _currentProjectName == null)
            {
                return new Markup("[dim]Select a project to see execution logs.[/]");
            }

            var state = ExecutionService.Instance.GetOrCreateState(_currentProjectPath, _currentProjectName);

            var grid = new Grid();
            grid.AddColumn();

            var statusColor = state.Status switch
            {
                ExecutionStatus.Running => "green",
                ExecutionStatus.Building => "blue",
                ExecutionStatus.Crashed => "red",
                ExecutionStatus.Stopped => "yellow",
                _ => "dim"
            };

            grid.AddRow(new Markup($"Status: [{statusColor}]{state.Status}[/]"));
            if (state.ExitCode != null)
            {
                grid.AddRow(new Markup($"Exit Code: [yellow]{state.ExitCode}[/]"));
            }
            grid.AddRow(new Text(""));

            var visibleRows = Math.Max(1, height - 4);
            var renderWidth = Math.Max(1, width - 4);

            var physicalLines = BuildPhysicalLines(state, renderWidth);
            UpdateScrollOffset(physicalLines, visibleRows);

            var table = new Table().Border(TableBorder.None).HideHeaders().NoSafeBorder().Expand();
            table.AddColumn(new TableColumn("Log").NoWrap().Width(renderWidth));

            RenderPhysicalLines(table, physicalLines, visibleRows, isActive);

            grid.AddRow(table);
            return grid;
        }
    }

    private List<PhysicalLine> BuildPhysicalLines(ProjectExecutionState state, int renderWidth)
    {
        var physicalLines = new List<PhysicalLine>();
        lock (state.LogLock)
        {
            for (var i = 0; i < state.Logs.Count; i++)
            {
                var logical = state.Logs[i];
                var (cleanText, style) = ExtractStyle(logical);
                var wrapped = WrapText(cleanText, renderWidth);

                foreach (var w in wrapped)
                {
                    physicalLines.Add(new PhysicalLine { Text = w, LogicalIndex = i, Style = style });
                }
            }
        }
        return physicalLines;
    }

    private void UpdateScrollOffset(List<PhysicalLine> physicalLines, int visibleRows)
    {
        if (_selectedIndex == -1)
        {
            _scrollOffset = Math.Max(0, physicalLines.Count - visibleRows);
        }
        else
        {
            var (first, last) = GetPhysicalIndicesForLogical(_selectedIndex, physicalLines);

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

    private void RenderPhysicalLines(Table table, List<PhysicalLine> physicalLines, int visibleRows, bool isActive)
    {
        var start = _scrollOffset;
        var renderedCount = 0;

        for (var i = start; i < physicalLines.Count && renderedCount < visibleRows; i++)
        {
            var line = physicalLines[i];
            var isSelected = line.LogicalIndex == _selectedIndex;
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
        var match = System.Text.RegularExpressions.Regex.Match(input, @"^\[(?<style>[^\]]+)\](?<text>.*)\[/\]$", System.Text.RegularExpressions.RegexOptions.Compiled);
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
}
