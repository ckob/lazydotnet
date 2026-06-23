using lazydotnet.Core;
using lazydotnet.Services;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public sealed record TestRunOutputSnapshot(
    IReadOnlyList<TestOutputLine> Output,
    TestRunProgress? Progress,
    TestRunCompleted? Completed,
    int Version);

public sealed class TestRunOutputModal(
    Func<TestRunOutputSnapshot> snapshotProvider,
    Action onClose) : Modal("Test Run Output", new Markup(""), onClose)
{
    private readonly Lock _lock = new();
    private int _scrollOffset;
    private int _lastVersion = -1;

    public override bool OnTick()
    {
        var snapshot = snapshotProvider();
        if (snapshot.Version == _lastVersion) return false;
        _lastVersion = snapshot.Version;
        return true;
    }

    public override IEnumerable<KeyBinding> GetKeyBindings()
    {
        foreach (var b in base.GetKeyBindings()) yield return b;

        yield return new KeyBinding("k/↑", "up", () => { Move(-1); return Task.CompletedTask; },
            k => k is { Key: ConsoleKey.UpArrow or ConsoleKey.K, Modifiers: 0 } ||
                 k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.P }, false);
        yield return new KeyBinding("j/↓", "down", () => { Move(1); return Task.CompletedTask; },
            k => k is { Key: ConsoleKey.DownArrow or ConsoleKey.J, Modifiers: 0 } ||
                 k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.N }, false);
        yield return new KeyBinding("g", "top", () => { JumpTop(); return Task.CompletedTask; },
            k => k is { KeyChar: 'g', Modifiers: 0 }, false);
        yield return new KeyBinding("G", "bottom", () => { JumpBottom(); return Task.CompletedTask; },
            k => k is { KeyChar: 'G' }, false);
        yield return new KeyBinding("y", "copy", () => { CopyAll(); return Task.CompletedTask; },
            k => k is { KeyChar: 'y', Modifiers: 0 }, true, LongDescription: "copy run output");
    }

    public override IRenderable GetRenderable(int width, int height)
    {
        lock (_lock)
        {
            var modalWidth = width * 9 / 10;
            var modalHeight = height * 9 / 10;
            var renderWidth = Math.Max(10, modalWidth - 8);
            var visibleRows = Math.Max(1, modalHeight - 4);

            var snapshot = snapshotProvider();
            _lastVersion = snapshot.Version;
            var lines = BuildLines(snapshot);
            var physicalLines = BuildPhysicalLines(lines, renderWidth);
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, Math.Max(0, physicalLines.Count - visibleRows)));

            var table = new Table().Border(TableBorder.None).HideHeaders().NoSafeBorder().Expand();
            table.AddColumn(new TableColumn("Output").NoWrap().Width(renderWidth));

            var rendered = 0;
            for (var i = _scrollOffset; i < physicalLines.Count && rendered < visibleRows; i++)
            {
                var line = physicalLines[i];
                var escaped = Markup.Escape(line.Text);
                table.AddRow(string.IsNullOrEmpty(line.Style)
                    ? new Markup(escaped)
                    : new Markup($"[{line.Style}]{escaped}[/]"));
                rendered++;
            }

            while (rendered < visibleRows)
            {
                table.AddRow(new Markup(""));
                rendered++;
            }

            var grid = new Grid();
            grid.AddColumn();
            grid.AddRow(table);

            return new Panel(new Padder(grid, new Padding(2, 1, 2, 1)))
            {
                Header = new PanelHeader("[bold yellow] Test Run Output [/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Blue),
                Expand = false,
                Width = modalWidth,
                Height = modalHeight
            };
        }
    }

    private void Move(int delta)
    {
        lock (_lock)
        {
            _scrollOffset = Math.Max(0, _scrollOffset + delta);
        }
    }

    private void JumpTop()
    {
        lock (_lock)
        {
            _scrollOffset = 0;
        }
    }

    private void JumpBottom()
    {
        lock (_lock)
        {
            _scrollOffset = int.MaxValue;
        }
    }

    private void CopyAll()
    {
        var text = string.Join(Environment.NewLine, BuildLines(snapshotProvider()).Select(l => l.Text));
        Clipboard.Copy(text, "Run output copied");
    }

    private static List<TestOutputLine> BuildLines(TestRunOutputSnapshot snapshot)
    {
        var lines = new List<TestOutputLine>();

        AppendSummary(lines, snapshot);

        var output = CleanOutput(snapshot.Output);
        if (output.Count > 0)
        {
            lines.Add(new TestOutputLine(""));
            lines.Add(new TestOutputLine("Runner log", "bold yellow"));
            lines.Add(new TestOutputLine(""));
            lines.AddRange(output);
        }

        if (lines.Count == 0)
        {
            lines.Add(new TestOutputLine("No run output captured yet.", "grey"));
        }
        return lines;
    }

    private static void AppendSummary(List<TestOutputLine> lines, TestRunOutputSnapshot snapshot)
    {
        if (snapshot.Completed != null)
        {
            var completed = snapshot.Completed;
            var duration = completed.DurationMs is { } ms ? $" in {ms}ms" : "";
            var total = completed.Passed + completed.Failed + completed.Skipped;
            lines.Add(new TestOutputLine("Summary", "bold yellow"));
            lines.Add(new TestOutputLine($"{total} completed{duration}", "bold"));
            lines.Add(new TestOutputLine($"{completed.Passed} passed", "green"));
            if (completed.Failed > 0) lines.Add(new TestOutputLine($"{completed.Failed} failed", "red"));
            if (completed.Skipped > 0) lines.Add(new TestOutputLine($"{completed.Skipped} skipped", "yellow"));
            return;
        }

        if (snapshot.Progress != null)
        {
            var progress = snapshot.Progress;
            lines.Add(new TestOutputLine("Summary", "bold yellow"));
            lines.Add(new TestOutputLine($"{progress.Completed}/{progress.Total} completed", "bold"));
            lines.Add(new TestOutputLine($"{progress.Passed} passed", "green"));
            if (progress.Failed > 0) lines.Add(new TestOutputLine($"{progress.Failed} failed", "red"));
            if (progress.Skipped > 0) lines.Add(new TestOutputLine($"{progress.Skipped} skipped", "yellow"));
        }
    }

    private static List<TestOutputLine> CleanOutput(IReadOnlyList<TestOutputLine> output)
    {
        var lines = new List<TestOutputLine>();
        var suppressExceptionBlock = false;

        foreach (var line in output)
        {
            if (line.Text.StartsWith("[ServerTestHost.OnTaskSchedulerUnobservedTaskException]", StringComparison.Ordinal))
            {
                suppressExceptionBlock = true;
                continue;
            }

            if (suppressExceptionBlock)
            {
                if (!line.Text.StartsWith("Finished test session.", StringComparison.Ordinal))
                {
                    continue;
                }
                suppressExceptionBlock = false;
            }

            lines.Add(line);
        }

        return lines;
    }

    private static List<TestOutputLine> BuildPhysicalLines(List<TestOutputLine> lines, int width)
    {
        var physicalLines = new List<TestOutputLine>();
        foreach (var line in lines)
        {
            foreach (var text in WrapText(line.Text, width))
            {
                physicalLines.Add(line with { Text = text });
            }
        }
        return physicalLines;
    }

    private static List<string> WrapText(string text, int width)
    {
        if (string.IsNullOrEmpty(text)) return [""];
        if (text.Length <= width) return [text];

        var lines = new List<string>();
        for (var start = 0; start < text.Length; start += width)
        {
            lines.Add(text.Substring(start, Math.Min(width, text.Length - start)));
        }
        return lines;
    }
}
