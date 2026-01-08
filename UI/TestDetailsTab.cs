using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;
using lazydotnet.UI.Components;

namespace lazydotnet.UI;

public class TestDetailsTab(TestService testService, IEditorService editorService) : IProjectTab
{
    private TestNode? _root;
    private readonly List<TestNode> _visibleNodes = [];
    private int _selectedIndex = 0;
    private int _scrollOffset = 0;
    private int _lastFrameIndex = -1;
    private string? _currentPath;

    private readonly Lock _lock = new(); // Synchronization lock

    private bool _isLoading = false;
    private readonly bool _isRunningTests = false;
    private string? _statusMessage;

    private readonly Dictionary<string, TestRunResult> _testResults = [];

    private int _runningTestCount = 0;

    private CancellationTokenSource? _discoveryCts;

    public Action? RequestRefresh { get; set; }
    public Action<Modal>? RequestModal { get; set; }
    public Action<string>? RequestSelectProject { get; set; }

    public string Title => "Tests";

    public async Task LoadAsync(string projectPath, string projectName, bool force = false)
    {
        if (!force && _currentPath == projectPath && !_isLoading) return;

        // Cancel previous discovery if running
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
        _testResults.Clear();
        _statusMessage = "Discovering tests...";
        RequestRefresh?.Invoke();

        try
        {
            var tests = await testService.DiscoverTestsAsync(projectPath, token);
            if (tests.Count != 0)
            {
                var r = testService.BuildTestTree(tests);
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
            return;
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
            var spinner = _runningTestCount > 0 ? Spinner.Known.CircleHalves : null;
            int currentFrame = SpinnerHelper.GetCurrentFrameIndex(spinner);
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
            _testResults.Clear();
            _selectedIndex = 0;
            _scrollOffset = 0;
            _statusMessage = null;
            _currentPath = null;
        }
    }

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        if (_isLoading || _isRunningTests) yield break;

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

        if (_visibleNodes.Count == 0) yield break;

        var node = _selectedIndex == -1 ? _visibleNodes[0] : _visibleNodes[_selectedIndex];

        yield return new KeyBinding("→", "expand", () =>
        {
            if (node.IsContainer && !node.IsExpanded)
            {
                node.IsExpanded = true;
                RefreshVisibleNodes();
            }
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.RightArrow, false);

        yield return new KeyBinding("←", "collapse", () =>
        {
            if (node.IsContainer && node.IsExpanded)
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
                    if (_selectedIndex < _scrollOffset) _scrollOffset = _selectedIndex;
                }
            }
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.LeftArrow, false);

        yield return new KeyBinding("enter/space", "toggle", () =>
        {
            if (node.IsContainer)
            {
                node.IsExpanded = !node.IsExpanded;
                RefreshVisibleNodes();
            }
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.Enter || k.Key == ConsoleKey.Spacebar, false);

        yield return new KeyBinding("r", "run", () => RunSelectedTestAsync(node), k => k.KeyChar == 'r' || k.KeyChar == 'R');
        yield return new KeyBinding("e/o", "open", () => OpenInEditorAsync(node), k => k.KeyChar == 'e' || k.KeyChar == 'o');
    }

    public async Task<bool> HandleKeyAsync(ConsoleKeyInfo key)
    {
        var binding = GetKeyBindings().FirstOrDefault(b => b.Match(key));
        if (binding != null)
        {
            await binding.Action();
            return true;
        }
        return false;
    }

    public void MoveUp()
    {
        if (_selectedIndex == -1 && _visibleNodes.Count > 0)
        {
            _selectedIndex = _visibleNodes.Count - 1;
            return;
        }
        if (_selectedIndex > 0)
        {
            _selectedIndex--;
            if (_selectedIndex < _scrollOffset) _scrollOffset = _selectedIndex;
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

    public string? GetScrollIndicator()
    {
        if (_visibleNodes.Count == 0) return null;
        return $"{_selectedIndex + 1} of {_visibleNodes.Count}";
    }


    private async Task OpenInEditorAsync(TestNode node)
    {
        if (node.FilePath != null)
        {
            await editorService.OpenFileAsync(node.FilePath, node.LineNumber);
        }
    }

    // ...

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
        int contentHeight = Math.Max(1, height);
        if (_selectedIndex == -1)
        {
            _scrollOffset = 0;
            return;
        }

        if (_selectedIndex < _scrollOffset) _scrollOffset = _selectedIndex;
        if (_selectedIndex >= _scrollOffset + contentHeight) _scrollOffset = _selectedIndex - contentHeight + 1;
        
        if (_scrollOffset < 0) _scrollOffset = 0;
    }

    public IRenderable GetContent(int availableHeight, int availableWidth, bool isActive)
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

            // Single Grid, No Layout Split
            var treeGrid = new Grid();
            treeGrid.AddColumn(new GridColumn().NoWrap());

            // Handle scrolling
            EnsureVisible(availableHeight);

            int end = Math.Min(_scrollOffset + availableHeight, _visibleNodes.Count);

            for (int i = _scrollOffset; i < end; i++)

            {
                var node = _visibleNodes[i];
                bool isSelected = i == _selectedIndex;

                string indent = new(' ', (node.Depth - 1) * 2);

                string expandIcon = node.IsContainer ? (node.IsExpanded ? "v" : ">") : " ";
                string statusIcon = node.Status switch
                {
                    TestStatus.Passed => "✓",
                    TestStatus.Failed => "✗",
                    TestStatus.Running => SpinnerHelper.GetFrame(Spinner.Known.CircleHalves),
                    _ => "○"
                };
                string statusColor = node.Status switch
                {
                    TestStatus.Passed => "green",
                    TestStatus.Failed => "red",
                    TestStatus.Running => "yellow",
                    _ => "dim"
                };

                string lineIcon = $"[yellow]{expandIcon}[/] [{statusColor}]{statusIcon}[/]";
                int visibleIconLen = 1 + 1 + statusIcon.Length;

                // Truncation logic
                int width = Math.Max(10, availableWidth);
                int prefixLen = indent.Length + 1 + visibleIconLen + 1;
                
                string rawName = node.Name;
                string testCountSuffix = (node.IsContainer && node.TestCount > 0) ? $" ({node.TestCount} tests)" : "";
                
                int cleanNameLen = rawName.Length + testCountSuffix.Length;
                int maxNameLen = width - prefixLen - 1;

                if (cleanNameLen > maxNameLen && maxNameLen > 0)
                {
                    // Truncate raw name before escaping
                    int allowedNameLen = maxNameLen - testCountSuffix.Length - 1;
                    if (allowedNameLen > 0 && rawName.Length > allowedNameLen)
                    {
                        rawName = string.Concat(rawName.AsSpan(0, allowedNameLen), "…");
                    }
                }

                string dispName = Markup.Escape(rawName);
                if (testCountSuffix.Length > 0)
                {
                    dispName += $" [dim]{Markup.Escape(testCountSuffix)}[/]";
                }

                if (isSelected)
                {
                    if (isActive)
                    {
                        treeGrid.AddRow(new Markup($"{indent} [black on blue]{lineIcon} {dispName}[/]"));
                    }
                    else
                    {
                        treeGrid.AddRow(new Markup($"{indent} [bold white]{lineIcon} {dispName}[/]"));
                    }
                }
                else
                {
                    treeGrid.AddRow(new Markup($"{indent} {lineIcon} {dispName}"));
                }
            }

            return treeGrid;
        }
    }

    private async Task RunSelectedTestAsync(TestNode node)
    {
        if (_currentPath == null) return;

        Interlocked.Increment(ref _runningTestCount);

        // Collect tests to run
        var testsToRun = new List<TestNode>();
        if (node.IsTest)
        {
            testsToRun.Add(node);
        }
        else
        {
            testsToRun.AddRange(GetAllLeafNodes(node));
        }

        if (testsToRun.Count == 0)
        {
            Interlocked.Decrement(ref _runningTestCount);
            return;
        }

        // Update status for the targeted node and its subtree
        SetStatusRecursive(node, TestStatus.Running);
        
        _statusMessage = $"Running tests ({_runningTestCount} active)...";

        RequestRefresh?.Invoke();

        _ = Task.Run(async () =>
        {
            try
            {
                var filter = testsToRun
                    .Where(t => t.Uid != null)
                    .Select(t => new RunRequestNode(t.Uid!, t.Name))
                    .ToArray();

                var results = await testService.RunTestsAsync(_currentPath, filter);

                await foreach (var res in results)
                {
                    var targetNode = testsToRun.FirstOrDefault(t => t.Uid == res.Id);
                    if (targetNode != null)
                    {
                        targetNode.Status = res.Outcome.ToLower() switch
                        {
                            "passed" => TestStatus.Passed,
                            "failed" => TestStatus.Failed,
                            _ => TestStatus.None
                        };
                        targetNode.Duration = res.Duration ?? 0;
                        targetNode.ErrorMessage = res.ErrorMessage != null ? string.Join(Environment.NewLine, res.ErrorMessage) : null;
                        
                        var stackTrace = new List<string>();
                        await foreach (var line in res.StackTrace) stackTrace.Add(line);
                        
                        var stdOut = new List<string>();
                        await foreach (var line in res.StdOut) stdOut.Add(line);

                        lock (targetNode.OutputLock)
                        {
                            targetNode.Output.Clear();
                            if (res.ErrorMessage != null && res.ErrorMessage.Length > 0)
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

                        UpdateParentStatus(targetNode);
                        RequestRefresh?.Invoke();
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText("debug.log", $"[TestRun Error]: {ex}\n");
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
                if (_runningTestCount == 0)
                {
                    _statusMessage = "All tests finished.";
                }
                else
                {
                    _statusMessage = $"Running tests ({_runningTestCount} active)...";
                }
                RequestRefresh?.Invoke();
            }
        });
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
            bool anyRunning = false;
            bool anyFailed = false;
            bool allPassed = true;
            bool anyNone = false;

            foreach (var child in parent.Children)
            {
                if (child.Status == TestStatus.Failed) anyFailed = true;
                if (child.Status == TestStatus.Running) anyRunning = true;
                if (child.Status != TestStatus.Passed) allPassed = false;
                if (child.Status == TestStatus.None) anyNone = true;
            }

            if (anyFailed) parent.Status = TestStatus.Failed;
            else if (allPassed && !anyNone) parent.Status = TestStatus.Passed;
            else if (anyRunning) 
            {
                // Only keep Running if it was already Running (set by SetStatusRecursive)
                // or if we want to show progress. User said "only see spinner on node executing and children".
                // So if a parent was NOT part of the execution, it shouldn't become "Running".
                if (parent.Status != TestStatus.Running)
                {
                    parent.Status = TestStatus.None;
                }
            }
            else parent.Status = TestStatus.None;

            parent = parent.Parent;
        }
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

        if (node.IsExpanded)
        {
            foreach (var child in node.Children) Traverse(child);
        }
    }
}
