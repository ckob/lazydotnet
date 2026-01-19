using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;
using lazydotnet.UI.Components;

namespace lazydotnet.UI;

public class TestDetailsTab(IEditorService editorService) : IProjectTab
{
    private TestNode? _root;
    private readonly List<TestNode> _visibleNodes = [];
    private int _selectedIndex;
    private int _scrollOffset;
    private int _lastFrameIndex = -1;
    private string? _currentPath;

    private readonly Lock _lock = new();

    private bool _isLoading;
    private const bool IsRunningTests = false;
    private string? _statusMessage;

    private int _runningTestCount;

    private CancellationTokenSource? _discoveryCts;

    public Action? RequestRefresh { get; set; }
    public Action<Modal>? RequestModal { get; set; }
    public Action<string>? RequestSelectProject { get; set; }

    public static string Title => "Tests";

    public async Task LoadAsync(string projectPath, string projectName, bool force = false)
    {
        if (!force && _currentPath == projectPath && !_isLoading) return;

        if (_discoveryCts != null)
        {
            await _discoveryCts.CancelAsync();
            _discoveryCts.Dispose();
            _discoveryCts = null;
        }

        _discoveryCts = new CancellationTokenSource();
        var token = _discoveryCts.Token;

        _currentPath = projectPath;
        _isLoading = true;
        _root = null;
        _visibleNodes.Clear();
        _statusMessage = "Discovering tests...";
        RequestRefresh?.Invoke();

        try
        {
            var tests = await TestService.DiscoverTestsAsync(projectPath, token);
            if (tests.Count != 0)
            {
                var r = TestService.BuildTestTree(tests);
                r.IsExpanded = true;

                lock (_lock)
                {
                    _root = r;
                    RefreshVisibleNodes();
                    _statusMessage = $"Found {tests.Count} tests.";
                    RequestRefresh?.Invoke();
                }
            }
            else
            {
                _statusMessage = "No tests found.";
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _isLoading = false;
                RequestRefresh?.Invoke();
            }
        }
    }

    public bool OnTick()
    {
        if (_isLoading || _runningTestCount > 0)
        {
            var spinner = _runningTestCount > 0 ? Spinner.Known.Dots : null;
            var currentFrame = SpinnerHelper.GetCurrentFrameIndex(spinner);
            if (currentFrame != _lastFrameIndex)
            {
                _lastFrameIndex = currentFrame;
                return true;
            }
        }
        return false;
    }

    public void ClearData()
    {
        lock (_lock)
        {
            _root = null;
            _visibleNodes.Clear();
            _selectedIndex = 0;
            _scrollOffset = 0;
            _statusMessage = null;
            _currentPath = null;
        }
    }

    public bool IsLoaded(string projectPath) => _currentPath == projectPath && !_isLoading;

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        if (_isLoading || IsRunningTests) yield break;

        foreach (var b in GetNavigationBindings()) yield return b;

        if (_visibleNodes.Count == 0) yield break;

        var node = _selectedIndex == -1 ? _visibleNodes[0] : _visibleNodes[_selectedIndex];

        yield return GetExpandBinding(node);
        yield return GetCollapseBinding(node);
        yield return GetToggleBinding(node);
        yield return GetDetailsBinding(node);
        yield return new KeyBinding("r", "run", () => RunSelectedTestAsync(node), k => k.KeyChar == 'r' || k.KeyChar == 'R');
        yield return new KeyBinding("e/o", "open", () => OpenInEditorAsync(node), k => k.KeyChar == 'e' || k.KeyChar == 'o');
    }

    private IEnumerable<KeyBinding> GetNavigationBindings()
    {
        yield return new KeyBinding("k", "up", () =>
        {
            MoveUp();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.P), false);

        yield return new KeyBinding("j", "down", () =>
        {
            MoveDown();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.N), false);
    }

    private KeyBinding GetExpandBinding(TestNode node)
    {
        return new KeyBinding("→", "expand", () =>
        {
            if (node is { IsContainer: true, IsExpanded: false })
            {
                node.IsExpanded = true;
                RefreshVisibleNodes();
            }
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.RightArrow, false);
    }

    private KeyBinding GetCollapseBinding(TestNode node)
    {
        return new KeyBinding("←", "collapse", () =>
        {
            if (node is { IsContainer: true, IsExpanded: true })
            {
                node.IsExpanded = false;
                RefreshVisibleNodes();
            }
            else if (node.Parent != null)
            {
                var idx = _visibleNodes.IndexOf(node.Parent);
                if (idx != -1)
                {
                    _selectedIndex = idx;
                    if (_selectedIndex < _scrollOffset)
                    {
                        _scrollOffset = _selectedIndex;
                    }
                }
            }
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.LeftArrow, false);
    }

    private KeyBinding GetToggleBinding(TestNode node)
    {
        return new KeyBinding("space", "toggle", () =>
        {
            if (node.IsContainer)
            {
                node.IsExpanded = !node.IsExpanded;
                RefreshVisibleNodes();
            }
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.Spacebar, false);
    }

    private KeyBinding GetDetailsBinding(TestNode node)
    {
        return new KeyBinding("enter", "details", () =>
        {
            ShowTestDetails(node);
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.Enter, true);
    }

    private void ShowTestDetails(TestNode node)
    {
        var lines = new List<TestOutputLine>();

        // Build metadata lines
        var statusColor = GetStatusColor(node.Status);
        var statusIcon = GetStatusIcon(node.Status);
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
            else
                lines.Add(new TestOutputLine("Test has not been run yet.", "dim"));
        }

        var modal = new TestDetailsModal(node.Name, lines, () => RequestModal?.Invoke(null!));

        if (node.FilePath != null)
        {
            modal.SetAdditionalKeyBindings([
                new KeyBinding("e/o", "open in editor", async () => {
                    await editorService.OpenFileAsync(node.FilePath, node.LineNumber);
                }, k => k.KeyChar is 'e' or 'o' or 'E' or 'O')
            ]);
        }

        RequestModal?.Invoke(modal);
    }

    public async Task<bool> HandleKeyAsync(ConsoleKeyInfo key)
    {
        var binding = GetKeyBindings().FirstOrDefault(b => b.Match(key));
        if (binding == null)
        {
            return false;
        }

        await binding.Action();
        return true;
    }

    public void MoveUp()
    {
        switch (_selectedIndex)
        {
            case -1 when _visibleNodes.Count > 0:
                _selectedIndex = _visibleNodes.Count - 1;
                return;
            case > 0:
            {
                _selectedIndex--;
                if (_selectedIndex < _scrollOffset) _scrollOffset = _selectedIndex;
                break;
            }
        }
    }

    public void MoveDown()
    {
        if (_selectedIndex == -1 && _visibleNodes.Count > 0)
        {
            _selectedIndex = 0;
            return;
        }
        if (_selectedIndex < _visibleNodes.Count - 1)
        {
            _selectedIndex++;
        }
    }

    private async Task OpenInEditorAsync(TestNode node)
    {
        if (node.FilePath != null)
        {
            await editorService.OpenFileAsync(node.FilePath, node.LineNumber);
        }
    }

    public TestNode? GetSelectedNode()
    {
        lock (_lock)
        {
            if (_visibleNodes.Count == 0 || _selectedIndex < 0 || _selectedIndex >= _visibleNodes.Count)
                return null;
            return _visibleNodes[_selectedIndex];
        }
    }

    private void EnsureVisible(int height)
    {
        var contentHeight = Math.Max(1, height);
        if (_selectedIndex == -1)
        {
            _scrollOffset = 0;
            return;
        }

        if (_selectedIndex < _scrollOffset) _scrollOffset = _selectedIndex;
        if (_selectedIndex >= _scrollOffset + contentHeight) _scrollOffset = _selectedIndex - contentHeight + 1;

        if (_scrollOffset < 0) _scrollOffset = 0;
    }

    public IRenderable GetContent(int height, int width, bool isActive)
    {
        if (_isLoading)
        {
             return new Markup($"[yellow]{SpinnerHelper.GetFrame()} Discovering tests...[/]");
        }

        lock (_lock)
        {
            if (_visibleNodes.Count == 0)
            {
                 return new Markup(_statusMessage ?? "[dim]No tests available.[/]");
            }

            var treeGrid = new Grid();
            treeGrid.AddColumn(new GridColumn().NoWrap());

            EnsureVisible(height);

            var end = Math.Min(_scrollOffset + height, _visibleNodes.Count);

            for (var i = _scrollOffset; i < end; i++)
            {
                var node = _visibleNodes[i];
                var isSelected = i == _selectedIndex;
                treeGrid.AddRow(RenderNode(node, isSelected, width, isActive));
            }

            return treeGrid;
        }
    }

    private static Markup RenderNode(TestNode node, bool isSelected, int width, bool isActive)
    {
        string indent = new(' ', (node.Depth - 1) * 2);
        var statusColor = GetStatusColor(node.Status);
        var statusIcon = GetStatusIcon(node.Status);
        var expandIcon = node.IsExpanded ? "v" : ">";
        var expandPlaceholder = node.IsContainer ? expandIcon : " ";

        var lineIcon = $"[yellow]{expandPlaceholder}[/] [{statusColor}]{statusIcon}[/]";
        var visibleIconLen = 1 + 1 + statusIcon.Length;

        var maxWidth = Math.Max(10, width);
        var prefixLen = indent.Length + 1 + visibleIconLen + 1;

        var rawName = node.Name;
        var testCountSuffix = node is { IsContainer: true, TestCount: > 0 } ? $" ({node.TestCount} tests)" : "";

        var cleanNameLen = rawName.Length + testCountSuffix.Length;
        var maxNameLen = maxWidth - prefixLen - 1;

        if (cleanNameLen > maxNameLen && maxNameLen > 0)
        {
            rawName = TruncateName(rawName, maxNameLen - testCountSuffix.Length - 1);
        }

        var displayName = Markup.Escape(rawName);
        if (testCountSuffix.Length > 0)
        {
            displayName += $" [dim]{Markup.Escape(testCountSuffix)}[/]";
        }

        if (isSelected && isActive)
        {
            return new Markup($"{indent} [black on blue]{lineIcon} {displayName}[/]");
        }

        return new Markup($"{indent} {lineIcon} {displayName}");
    }

    private static int GetCountByStatus(TestNode node, TestStatus status)
    {
        if (node.IsTest)
        {
            return node.Status == status ? 1 : 0;
        }

        return node.Children.Sum(c => GetCountByStatus(c, status));
    }

    private static double GetMaxDuration(TestNode node)
    {
        if (node.IsTest)
        {
            return node.Duration;
        }

        return node.Children.Count > 0 ? node.Children.Max(GetMaxDuration) : 0;
    }

    private static string GetStatusColor(TestStatus status) => status switch
    {
        TestStatus.Passed => "green",
        TestStatus.Failed => "red",
        TestStatus.Running => "yellow",
        _ => "dim"
    };

    private static string GetStatusIcon(TestStatus status) => status switch
    {
        TestStatus.Passed => "✓",
        TestStatus.Failed => "✗",
        TestStatus.Running => SpinnerHelper.GetFrame(Spinner.Known.Dots),
        _ => "○"
    };

    private static string TruncateName(string name, int allowedLen)
    {
        if (allowedLen > 0 && name.Length > allowedLen)
        {
            return string.Concat(name.AsSpan(0, allowedLen), "…");
        }
        return name;
    }

    private Task RunSelectedTestAsync(TestNode node)
    {
        if (_currentPath == null) return Task.CompletedTask;

        Interlocked.Increment(ref _runningTestCount);

        var testsToRun = GetTestsToRun(node);

        if (testsToRun.Count == 0)
        {
            Interlocked.Decrement(ref _runningTestCount);
            return Task.CompletedTask;
        }

        SetStatusRecursive(node, TestStatus.Running);
        _statusMessage = $"Running tests ({_runningTestCount} active)...";
        RequestRefresh?.Invoke();

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteTestRunAsync(testsToRun);
            }
            catch (Exception ex)
            {
                await File.AppendAllTextAsync("debug.log", $"[TestRun Error]: {ex}\n");
                _statusMessage = $"Run error: {ex.Message}";
                foreach (var t in testsToRun)
                {
                    if (t.Status == TestStatus.Running) t.Status = TestStatus.Failed;
                    UpdateParentStatus(t);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _runningTestCount);
                _statusMessage = _runningTestCount == 0
                    ? "All tests finished."
                    : $"Running tests ({_runningTestCount} active)...";
                RequestRefresh?.Invoke();
            }
        });
        return Task.CompletedTask;
    }

    private static List<TestNode> GetTestsToRun(TestNode node)
    {
        var testsToRun = new List<TestNode>();
        if (node.IsTest)
        {
            testsToRun.Add(node);
        }
        else
        {
            testsToRun.AddRange(GetAllLeafNodes(node));
        }
        return testsToRun;
    }

    private async Task ExecuteTestRunAsync(List<TestNode> testsToRun)
    {
        var filter = testsToRun
            .Where(t => t.Uid != null && t.Source != null)
            .Select(t => new RunRequestNode(t.Uid!, t.Name, t.Source!, t.IsMtp))
            .ToArray();

        var results = await TestService.RunTestsAsync(_currentPath!, filter);

        await foreach (var res in results)
        {
            var targetNode = testsToRun.FirstOrDefault(t => t.Uid == res.Id);
            if (targetNode != null)
            {
                await UpdateTestNodeWithResultAsync(targetNode, res);
                UpdateParentStatus(targetNode);
                RequestRefresh?.Invoke();
            }
        }
    }

    private static async Task UpdateTestNodeWithResultAsync(TestNode targetNode, TestRunResult res)
    {
        targetNode.Status = res.Outcome.ToLower() switch
        {
            "passed" => TestStatus.Passed,
            "failed" => TestStatus.Failed,
            _ => TestStatus.None
        };
        targetNode.Duration = res.Duration ?? 0;
        targetNode.ErrorMessage = string.Join(Environment.NewLine, res.ErrorMessage);

        var stackTrace = new List<string>();
        await foreach (var line in res.StackTrace) stackTrace.Add(line);

        var stdOut = new List<string>();
        await foreach (var line in res.StdOut) stdOut.Add(line);

        lock (targetNode.OutputLock)
        {
            targetNode.Output.Clear();
            if (res.ErrorMessage.Length > 0)
            {
                targetNode.Output.Add(new TestOutputLine("Error:", "red"));
                foreach (var err in res.ErrorMessage)
                {
                    targetNode.Output.Add(new TestOutputLine(err));
                }

                targetNode.Output.Add(new TestOutputLine(""));
            }

            foreach (var line in stackTrace)
            {
                targetNode.Output.Add(new TestOutputLine(line, "dim"));
            }
            foreach (var line in stdOut)
            {
                targetNode.Output.Add(new TestOutputLine(line));
            }
        }
    }

    private static List<TestNode> GetAllLeafNodes(TestNode node)
    {
        var leaves = new List<TestNode>();
        if (node.IsTest) leaves.Add(node);

        foreach (var child in node.Children)
        {
            leaves.AddRange(GetAllLeafNodes(child));
        }
        return leaves;
    }

    private static void UpdateParentStatus(TestNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            parent.Status = CalculateParentStatus(parent);
            parent = parent.Parent;
        }
    }

    private static TestStatus CalculateParentStatus(TestNode parent)
    {
        var anyRunning = false;
        var anyFailed = false;
        var allPassed = true;
        var anyNone = false;

        foreach (var childStatus in parent.Children.Select(x => x.Status))
        {
            if (childStatus == TestStatus.Failed) anyFailed = true;
            if (childStatus == TestStatus.Running) anyRunning = true;
            if (childStatus != TestStatus.Passed) allPassed = false;
            if (childStatus == TestStatus.None) anyNone = true;
        }

        if (anyFailed) return TestStatus.Failed;
        if (allPassed && !anyNone) return TestStatus.Passed;
        if (anyRunning) return parent.Status == TestStatus.Running ? TestStatus.Running : TestStatus.None;

        return TestStatus.None;
    }

    private static void SetStatusRecursive(TestNode node, TestStatus status)
    {
        node.Status = status;
        foreach(var child in node.Children) SetStatusRecursive(child, status);
    }

    private void RefreshVisibleNodes()
    {
        _visibleNodes.Clear();
        if (_root != null) Traverse(_root);
    }

    private void Traverse(TestNode node)
    {
        if (node != _root)
        {
            _visibleNodes.Add(node);
        }

        if (!node.IsExpanded)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            Traverse(child);
        }
    }
}
