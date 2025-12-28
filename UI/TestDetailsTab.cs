using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;
using lazydotnet.UI.Components;

namespace lazydotnet.UI;

public class TestDetailsTab(TestService testService) : IProjectTab
{
    private TestNode? _root;
    private readonly List<TestNode> _visibleNodes = [];
    private int _selectedIndex = 0;
    private int _scrollOffset = 0;
    private string? _currentPath;

    private readonly Lock _lock = new(); // Synchronization lock

    private bool _isLoading = false;
    private readonly bool _isRunningTests = false;
    private string? _statusMessage;

    // For tracking ongoing test runs
    // Map full test name to status
    private readonly Dictionary<string, TestResult> _testResults = [];

    private int _runningTestCount = 0;

    private CancellationTokenSource? _discoveryCts;

    public string Title => "Tests";

    public async Task LoadAsync(string projectPath, string projectName)
    {
        if (_currentPath == projectPath && !_isLoading) return;

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

        try
        {
            // Heavy processing offloaded to thread pool
            var result = await Task.Run(async () =>
            {
                var tests = await testService.DiscoverTestsAsync(projectPath, token);
                if (tests.Count != 0)
                {
                    var r = testService.BuildTestTree(tests);
                    // Pre-calculate visible nodes logic or just set expanded here?
                    r.IsExpanded = true;
                    return (RootNode: r, Count: tests.Count);
                }
                return (RootNode: (TestNode?)null, Count: 0);
            }, token);

            if (result.RootNode != null)
            {
                lock (_lock)
                {
                    _root = result.RootNode;
                    RefreshVisibleNodes();
                    _statusMessage = $"Found {result.Count} tests.";
                }
            }
            else
            {
                _statusMessage = "No tests found.";
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore, likely superceded
            // However, we should be careful not to unset loading if we were cancelled by a NEW load
            // But if a NEW load started, it would have set _isLoading=true AFTER we were cancelled?
            // Wait, LoadAsync runs strictly on UI thread or async?
            // If we are cancelled by another LoadAsync call, that call proceeds.
            // We should just return.
            return;
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            // Only set not loading if *we* are still the active token
            if (!token.IsCancellationRequested)
            {
                _isLoading = false;
            }
        }
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

    public void MoveUp()
    {
        if (_selectedIndex > 0)
        {
            _selectedIndex--;
             // Ensure visible logic is usually in GetContent but let's sync offset
            if (_selectedIndex < _scrollOffset) _scrollOffset = _selectedIndex;
        }
    }

    public void MoveDown()
    {
        if (_selectedIndex < _visibleNodes.Count - 1)
        {
            _selectedIndex++;
            // detailed visibility logic depends on height, so we defer to GetContent
            // or we assume a safe check if we knew height.
            // For now, GetContent handles the "into view" logic well enough for down.
        }
    }

    // ...

    public string? GetScrollIndicator()
    {
        if (_visibleNodes.Count == 0) return null;
        return $"{_selectedIndex + 1} of {_visibleNodes.Count}";
    }

    public async Task<bool> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (_isLoading || _isRunningTests) return true;

        lock (_lock)
        {
            if (_visibleNodes.Count == 0) return false;

            var node = _visibleNodes[_selectedIndex];

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                case ConsoleKey.K:
                    MoveUp();
                    return true;
                case ConsoleKey.DownArrow:
                case ConsoleKey.J:
                    MoveDown();
                    return true;
            }

            // Expand/Collapse
            if (key.Key == ConsoleKey.RightArrow)
            {
                 if (node.IsContainer && !node.IsExpanded)
                 {
                     node.IsExpanded = true;
                     RefreshVisibleNodes();
                 }
                 return true;
            }
            if (key.Key == ConsoleKey.LeftArrow)
            {
                 if (node.IsContainer && node.IsExpanded)
                 {
                     node.IsExpanded = false;
                     RefreshVisibleNodes();
                 }
                 else if (node.Parent != null)
                 {
                     // Jump to parent
                     var idx = _visibleNodes.IndexOf(node.Parent);
                     if (idx != -1)
                     {
                         _selectedIndex = idx;
                         if (_selectedIndex < _scrollOffset) _scrollOffset = _selectedIndex;
                     }
                 }
                 return true;
            }

            if (key.Key == ConsoleKey.Spacebar || key.Key == ConsoleKey.Enter)
            {
                 if (node.IsContainer)
                 {
                     node.IsExpanded = !node.IsExpanded;
                     RefreshVisibleNodes();
                 }
                 return true;
            }

            if (key.KeyChar == 'r' || key.KeyChar == 'R')
            {
                 // Run leaf or container
                 _ = RunSelectedTestAsync(node);
                 return true;
            }
        }

        return false;
    }

    // ...

    public IRenderable GetContent(int availableHeight, int availableWidth)
    {
        if (_isLoading)
        {
             return new Markup("[yellow]Loading tests...[/]");
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
            int contentHeight = Math.Max(1, availableHeight);
            if (_selectedIndex < _scrollOffset) _scrollOffset = _selectedIndex;
            if (_selectedIndex >= _scrollOffset + contentHeight) _scrollOffset = _selectedIndex - contentHeight + 1;

            int end = Math.Min(_scrollOffset + contentHeight, _visibleNodes.Count);

            for (int i = _scrollOffset; i < end; i++)
            {
                var node = _visibleNodes[i];
                bool isSelected = i == _selectedIndex;

                string indent = new(' ', (node.Depth - 1) * 2);

                string iconColor = "yellow";
                string iconSymbol = node.IsExpanded ? "v" : ">";

                if (!node.IsContainer)
                {
                    iconSymbol = node.Status switch
                    {
                        TestStatus.Passed => "ok",
                        TestStatus.Failed => "XX",
                        TestStatus.Running => "..",
                        _ => "()"
                    };
                    iconColor = node.Status switch
                    {
                        TestStatus.Passed => "green",
                        TestStatus.Failed => "red",
                        TestStatus.Running => "yellow",
                        _ => "dim"
                    };
                }

                // Truncation logic
                int width = Math.Max(10, availableWidth);
                int prefixLen = indent.Length + 1 + iconSymbol.Length + 1;
                int maxNameLen = width - prefixLen - 1;

                string dispName = node.Name;
                if (dispName.Length > maxNameLen && maxNameLen > 0)
                {
                    dispName = string.Concat(dispName.AsSpan(0, maxNameLen - 1), "…");
                }

                string style = isSelected ? "[black on blue]" : "";
                string closeStyle = isSelected ? "[/]" : "";

                treeGrid.AddRow(new Markup($"{style}{indent} [{iconColor}]{iconSymbol}[/] {Markup.Escape(dispName)}{closeStyle}"));
            }

            return treeGrid;
        }
    }

    private async Task RunSelectedTestAsync(TestNode node)
    {
        if (_currentPath == null) return;

        Interlocked.Increment(ref _runningTestCount);

        // Update status for all involved tests
        if (node.IsContainer)
        {
            SetStatusRecursive(node, TestStatus.Running);
        }
        else
        {
            node.Status = TestStatus.Running;
            UpdateParentStatus(node);
        }

        _statusMessage = $"Running tests ({_runningTestCount} active)...";

        _ = Task.Run(async () =>
        {
            try
            {
                string filter;
                if (node.IsContainer)
                {
                    // Contains filter: "FullyQualifiedName~Namespace"
                    // Note: TestNode.FullName is the fully qualified name or partial for containers (e.g. Namespace.Class)
                    // The ~ operator is "contains".
                    // If we have a container "Lidl.Plus", it matches "Lidl.Plus.Feature.Test"
                    filter = $"FullyQualifiedName~{node.FullName}";
                }
                else
                {
                    // Exact match for leaf
                    filter = $"FullyQualifiedName={node.FullName}";
                }

                 var results = await testService.RunTestAsync(_currentPath, filter);

                 if (results.Count == 0)
                 {
                     if (node.IsContainer)
                     {
                        // If container run returned nothing, maybe just cancel running status?
                        SetStatusRecursive(node, TestStatus.None);
                     }
                     else
                     {
                         node.Status = TestStatus.None;
                     }
                 }
                 else
                 {
                     ProcessTestResults(node, results);
                 }
            }
            catch (Exception)
            {
                if (node.IsContainer) SetStatusRecursive(node, TestStatus.Failed);
                else node.Status = TestStatus.Failed;
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

                // Final status update for parents (important for container run too)
                if (node.Parent != null) UpdateParentStatus(node.Parent); // If node is root (Tests), parent is null
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
                if (child.Status == TestStatus.Running) anyRunning = true;
                if (child.Status == TestStatus.Failed) anyFailed = true;
                if (child.Status != TestStatus.Passed) allPassed = false;
                if (child.Status == TestStatus.None) anyNone = true;
            }

            if (anyRunning) parent.Status = TestStatus.Running;
            else if (anyFailed) parent.Status = TestStatus.Failed;
            else if (allPassed && !anyNone) parent.Status = TestStatus.Passed;
            else parent.Status = TestStatus.None; // Mixed or incomplete

            parent = parent.Parent;
        }
    }

    private void ProcessTestResults(TestNode rootNode, List<TestResult> results)
    {
        // Filter out null names if any
        var resultLookup = results
            .Where(r => r.FullyQualifiedName != null)
            .GroupBy(r => r.FullyQualifiedName!)
            .ToDictionary(g => g.Key, g => g.First());

        // Start from rootNode - in parallel execution rootNode is always a LEAF here
        // But we keep recursion just in case logic changes or for safety
        ApplyResultsRecursive(rootNode, resultLookup);
    }

    private void ApplyResultsRecursive(TestNode node, Dictionary<string, TestResult> lookup)
    {
        if (node.IsTest)
        {
            if (lookup.TryGetValue(node.FullName, out var res))
            {
                 node.Status = res.Outcome;
                 node.ErrorMessage = res.ErrorMessage;
                 node.StackTrace = res.StackTrace;
                 node.Duration = res.Duration;
                 _testResults[node.FullName] = res;
            }
            else
            {
                // If it was marked running but not found in results, maybe it was skipped or didn't run?
                // Leave as is or reset?
                if (node.Status == TestStatus.Running) node.Status = TestStatus.None;
            }
        }
        else
        {
            // Container: status is aggregate of children?
            // Or just container status.
            // Let's recurse first
            bool anyFailed = false;
            bool allPassed = true;

            foreach(var child in node.Children)
            {
                ApplyResultsRecursive(child, lookup);
                if (child.Status == TestStatus.Failed) anyFailed = true;
                if (child.Status != TestStatus.Passed) allPassed = false;
            }

            // Update container status based on children
            if (anyFailed) node.Status = TestStatus.Failed;
            else if (allPassed) node.Status = TestStatus.Passed;
            else node.Status = TestStatus.None; // Mixed or skipped
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
        if (node != _root) _visibleNodes.Add(node);

        if (node.IsExpanded)
        {
            foreach (var child in node.Children) Traverse(child);
        }
    }
}
