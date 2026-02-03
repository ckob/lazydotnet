using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;
using lazydotnet.UI.Components;

namespace lazydotnet.UI;

public enum TestFilter
{
    All,
    Passed,
    Failed,
    Running
}

public class TestDetailsTab(IEditorService editorService) : IProjectTab
{
    private TestNode? _root;
    private readonly List<TestNode> _visibleNodes = [];
    private int _selectedIndex;
    private int _scrollOffset;
    private int _lastFrameIndex = -1;
    private string? _currentPath;
    private TestFilter _filter = TestFilter.All;

    public string Title => _filter switch
    {
        TestFilter.Passed => "Tests (passing)",
        TestFilter.Failed => "Tests (failing)",
        TestFilter.Running => "Tests (running)",
        _ => "Tests"
    };

    public Action? RequestRefresh { get; set; }
    public Action<Modal>? RequestModal { get; set; }
    public Action<string>? RequestSelectProject { get; set; }

    private readonly Lock _lock = new();
    private bool _isLoading;
    private string? _statusMessage;
    private int _runningTestCount;
    private CancellationTokenSource? _discoveryCts;


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
        if (_isLoading) yield break;

        foreach (var b in GetNavigationBindings()) yield return b;

        yield return new KeyBinding("f", "filter", CycleFilterAsync, k => k.Key == ConsoleKey.F);

        if (_visibleNodes.Count == 0) yield break;

        var node = _selectedIndex == -1 ? _visibleNodes[0] : _visibleNodes[_selectedIndex];

        yield return GetExpandBinding(node);
        yield return GetCollapseBinding(node);
        yield return GetToggleBinding(node);
        yield return GetDetailsBinding(node);
        yield return new KeyBinding("r", "run", () => RunSelectedTestAsync(node), k => k.Key == ConsoleKey.R);
        yield return new KeyBinding("e", "edit", () => OpenInEditorAsync(node), k => k.Key == ConsoleKey.E);
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

        yield return new KeyBinding("pgup", "page up", () =>
        {
            PageUp(10);
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.PageUp || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.U), false);

        yield return new KeyBinding("pgdn", "page down", () =>
        {
            PageDown(10);
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.PageDown || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.D), false);
    }

    private KeyBinding GetExpandBinding(TestNode node)
    {
        return new KeyBinding("→", "expand", () =>
        {
            if (node is { IsContainer: true, IsExpanded: false })
            {
                node.IsExpanded = true;
                lock (_lock)
                {
                    RefreshVisibleNodes();
                }
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
                lock (_lock)
                {
                    RefreshVisibleNodes();
                }
            }
            else if (node.Parent != null)
            {
                lock (_lock)
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
                lock (_lock)
                {
                    RefreshVisibleNodes();
                }
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
        var modal = new TestDetailsModal(node, () => RequestModal?.Invoke(null!));

        if (node.FilePath != null)
        {
            modal.SetAdditionalKeyBindings([
                new KeyBinding("e", "edit", async () => {
                    await editorService.OpenFileAsync(node.FilePath, node.LineNumber);
                }, k => k.Key == ConsoleKey.E)
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
        lock (_lock)
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
    }

    public void MoveDown()
    {
        lock (_lock)
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
    }

    public void PageUp(int pageSize)
    {
        lock (_lock)
        {
            if (_visibleNodes.Count == 0) return;
            if (_selectedIndex == -1) _selectedIndex = _visibleNodes.Count - 1;
            _selectedIndex = Math.Max(0, _selectedIndex - pageSize);
        }
    }

    public void PageDown(int pageSize)
    {
        lock (_lock)
        {
            if (_visibleNodes.Count == 0) return;
            if (_selectedIndex == -1) _selectedIndex = 0;
            _selectedIndex = Math.Min(_visibleNodes.Count - 1, _selectedIndex + pageSize);
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
                if (_runningTestCount > 0 && _filter == TestFilter.Running)
                {
                    var spinner = Spinner.Known.Dots;
                    return new Markup($"[yellow]{SpinnerHelper.GetFrame(spinner)}[/] {_statusMessage}");
                }

                if (_filter != TestFilter.All && _root != null && _root.TestCount > 0)
                {
                    var filterName = _filter switch
                    {
                        TestFilter.Passed => "passing",
                        TestFilter.Failed => "failing",
                        TestFilter.Running => "running",
                        _ => "" // Should not happen
                    };
                    return new Markup($"[dim]No {filterName} tests found.[/]");
                }
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

    public static string GetStatusColor(TestStatus status) => status switch
    {
        TestStatus.Passed => "green",
        TestStatus.Failed => "red",
        TestStatus.Running => "yellow",
        _ => "dim"
    };

    public static string GetStatusIcon(TestStatus status) => status switch
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

        var reportedUids = new HashSet<string>();

        await foreach (var res in results)
        {
            var targets = testsToRun.Where(t => t.Uid == res.Id).ToList();
            
            if (targets.Count == 0 && res.DisplayName != null)
            {
                // Fuzzy matching for dynamic test names
                var fuzzyMatch = testsToRun.FirstOrDefault(t => t.Status == TestStatus.Running && IsFuzzyMatch(t, res));
                if (fuzzyMatch != null) targets = [fuzzyMatch];
            }

            foreach (var targetNode in targets)
            {
                reportedUids.Add(targetNode.Uid ?? string.Empty);
                await UpdateTestNodeWithResultAsync(targetNode, res);
                UpdateParentStatus(targetNode);
            }

            lock (_lock)
            {
                RefreshVisibleNodes();
            }
            RequestRefresh?.Invoke();
        }

        // Cleanup: any test that was requested but didn't report a terminal result should be marked as failed
        foreach (var test in testsToRun)
        {
            if (test.Status == TestStatus.Running)
            {
                test.Status = TestStatus.Failed;
                test.ErrorMessage = "Test did not report a result.";
                UpdateParentStatus(test);
            }
        }
        
        lock (_lock)
        {
            RefreshVisibleNodes();
        }
        RequestRefresh?.Invoke();
    }

    private static bool IsFuzzyMatch(TestNode node, TestRunResult res)
    {
        if (string.IsNullOrEmpty(res.DisplayName)) return false;

        // Strip arguments from node.FullName to get the pure FQN of the method
        var nodeFqn = node.FullName;
        var parenIndex = nodeFqn.IndexOf('(');
        if (parenIndex > 0) nodeFqn = nodeFqn[..parenIndex];

        // 1. Exact match on method name part
        var lastDot = nodeFqn.LastIndexOf('.');
        var methodName = lastDot >= 0 ? nodeFqn[(lastDot + 1)..] : nodeFqn;

        // Result display name might be "Method(args)" or "Namespace.Class.Method(args)"
        if (res.DisplayName.StartsWith(methodName) || res.DisplayName.Contains("." + methodName + "(")) return true;

        // 2. Full name containment (stripped)
        if (res.DisplayName.Contains(nodeFqn)) return true;

        return false;
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

        // Update name for dynamic tests (e.g. theories with timestamps)
        if (res.DisplayName != null && targetNode.Parent?.IsTheoryContainer == true)
        {
            var openParen = res.DisplayName.IndexOf('(');
            if (openParen >= 0)
            {
                targetNode.Name = res.DisplayName[openParen..];
            }
        }

        var stackTrace = new List<string>();
        await foreach (var line in res.StackTrace) stackTrace.Add(line);

        var stdOut = new List<string>();
        await foreach (var line in res.StdOut) stdOut.Add(line);

        lock (targetNode.OutputLock)
        {
            targetNode.Output.Clear();
            if (res.DisplayName != null && res.DisplayName != targetNode.FullName)
            {
                targetNode.Output.Add(new TestOutputLine($"Run name: {res.DisplayName}", "dim"));
                targetNode.Output.Add(new TestOutputLine(""));
            }

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

        if (anyRunning) return TestStatus.Running;
        if (anyFailed) return TestStatus.Failed;
        if (allPassed && !anyNone) return TestStatus.Passed;

        return TestStatus.None;
    }

    private static void SetStatusRecursive(TestNode node, TestStatus status)
    {
        node.Status = status;
        foreach(var child in node.Children) SetStatusRecursive(child, status);
    }

    private Task CycleFilterAsync()
    {
        _filter = _filter switch
        {
            TestFilter.All => TestFilter.Running,
            TestFilter.Running => TestFilter.Failed,
            TestFilter.Failed => TestFilter.Passed,
            TestFilter.Passed => TestFilter.All,
            _ => TestFilter.All
        };

        lock (_lock)
        {
            RefreshVisibleNodes();
            _selectedIndex = _visibleNodes.Count > 0 ? 0 : -1;
            _scrollOffset = 0;
        }
        RequestRefresh?.Invoke();
        return Task.CompletedTask;
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
            if (!ShouldShow(node)) return;
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

    private bool ShouldShow(TestNode node)
    {
        if (_filter == TestFilter.All) return true;

        if (node.IsTest)
        {
            return _filter switch
            {
                TestFilter.Passed => node.Status == TestStatus.Passed,
                TestFilter.Failed => node.Status == TestStatus.Failed,
                TestFilter.Running => node.Status == TestStatus.Running,
                _ => true
            };
        }

        return _filter switch
        {
            TestFilter.Passed => GetCountByStatus(node, TestStatus.Passed) > 0,
            TestFilter.Failed => GetCountByStatus(node, TestStatus.Failed) > 0,
            TestFilter.Running => GetCountByStatus(node, TestStatus.Running) > 0,
            _ => true
        };
    }
}
