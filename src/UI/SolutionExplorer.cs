using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;

namespace lazydotnet.UI;

public class ExplorerNode
{
    public string Name { get; init; } = string.Empty;
    public string? ProjectPath { get; init; }
    public bool IsProject { get; init; }
    public bool IsSolution { get; init; }
    public bool IsSlnx { get; init; }
    public bool IsSlnf { get; init; }
    public bool IsSolutionFolder { get; init; }
    public string? SolutionFolderGuid { get; init; }
    public bool IsExpanded { get; set; } = true;
    public int Depth { get; set; }
    public List<ExplorerNode> Children { get; } = [];
    public ExplorerNode? Parent { get; set; }
    public ProjectInfo? ProjectInfo { get; init; }
}

public class SolutionExplorer(IEditorService editorService, Action? onSearchRequested = null) : IKeyBindable, ISearchable
{
    private ExplorerNode? _root;
    private string? _solutionRootPath;
    private readonly List<ExplorerNode> _visibleNodes = [];
    private int _selectedIndex;
    private int _scrollOffset;

    public void SetSolution(SolutionInfo solution)
    {
        _solutionRootPath = Path.GetDirectoryName(solution.Path);
        editorService.RootPath = _solutionRootPath;
        _root = BuildTree(solution);
        RefreshVisibleNodes();
    }

    private static ExplorerNode BuildTree(SolutionInfo solution)
    {
        var isSingleProject = solution.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

        if (isSingleProject && solution.Projects.Count == 1)
        {
            var proj = solution.Projects[0];
            return new ExplorerNode
            {
                Name = proj.Name,
                IsProject = true,
                ProjectPath = proj.Path,
                IsExpanded = true,
                Depth = 0,
                ProjectInfo = proj
            };
        }

        var root = new ExplorerNode
        {
            Name = solution.Name,
            Depth = 0,
            IsExpanded = true,
            IsProject = false,
            IsSolution = !isSingleProject && !solution.IsDirectoryBased,
            IsSlnx = solution.IsSlnx,
            IsSlnf = solution.IsSlnf,
            ProjectPath = solution.Path
        };

        var nodeMap = InitializeNodeMap(solution);

        // Second pass: build hierarchy using ParentProjectGuid
        foreach (var proj in solution.Projects)
        {
            AddProjectToHierarchy(root, nodeMap, proj);
        }

        PruneTree(root);
        CalculateDepths(root, 0);
        SortTree(root);
        return root;
    }

    private static Dictionary<string, ExplorerNode> InitializeNodeMap(SolutionInfo solution)
    {
        var nodeMap = new Dictionary<string, ExplorerNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var proj in solution.Projects)
        {
            nodeMap[proj.Id] = new ExplorerNode
            {
                Name = proj.Name,
                IsProject = !proj.IsSolutionFolder,
                IsSolutionFolder = proj.IsSolutionFolder,
                SolutionFolderGuid = proj.Id,
                ProjectPath = proj.IsSolutionFolder ? null : proj.Path,
                IsExpanded = true,
                ProjectInfo = proj
            };
        }
        return nodeMap;
    }

    private static void AddProjectToHierarchy(ExplorerNode root, Dictionary<string, ExplorerNode> nodeMap, ProjectInfo proj)
    {
        var node = nodeMap[proj.Id];

        // If no parent (root level), add directly to root
        if (string.IsNullOrEmpty(proj.ParentId))
        {
            root.Children.Add(node);
            node.Parent = root;
            return;
        }

        // Try to find parent in the map
        if (nodeMap.TryGetValue(proj.ParentId, out var parentNode))
        {
            parentNode.Children.Add(node);
            node.Parent = parentNode;
        }
        else
        {
            // Parent not found (shouldn't happen with valid solutions), add to root
            root.Children.Add(node);
            node.Parent = root;
        }
    }

    private static bool PruneTree(ExplorerNode node)
    {
        var hasContent = false;
        for (var i = node.Children.Count - 1; i >= 0; i--)
        {

            var childKept = PruneTree(node.Children[i]);
            if (childKept)
            {
                hasContent = true;
            }
            else
            {
                node.Children.RemoveAt(i);
            }
        }

        // Always keep solution root and projects
        if (node.IsSolution || node.IsProject) return true;

        // For solution folders, only keep if they have content (projects or non-empty subfolders)
        if (node.IsSolutionFolder) return hasContent;

        return hasContent;
    }

    private static void CalculateDepths(ExplorerNode node, int depth)
    {
        node.Depth = depth;
        foreach (var child in node.Children)
        {
            CalculateDepths(child, depth + 1);
        }
    }

    private static void SortTree(ExplorerNode node)
    {
        node.Children.Sort((a, b) =>
        {
            if (a.IsProject == b.IsProject) return string.CompareOrdinal(a.Name, b.Name);

            return a.IsProject ? 1 : -1;
        });

        foreach (var child in node.Children)
            SortTree(child);
    }

    private void RefreshVisibleNodes()
    {
        _visibleNodes.Clear();
        if (_root != null)
        {
            Traverse(_root);
        }
    }

    private void Traverse(ExplorerNode node)
    {
        _visibleNodes.Add(node);
        if (!node.IsExpanded)
            return;
        foreach(var child in node.Children)
            Traverse(child);
    }

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        yield return new KeyBinding("k/↑/ctrl+p", "up", DoMoveUp, MatchUpKey, false);
        yield return new KeyBinding("j/↓/ctrl+n", "down", DoMoveDown, MatchDownKey, false);
        yield return new KeyBinding("pgup/ctrl+u", "page up", DoPageUp, MatchPageUpKey, false);
        yield return new KeyBinding("pgdn/ctrl+d", "page down", DoPageDown, MatchPageDownKey, false);
        yield return new KeyBinding("←", "collapse", DoCollapse, k => k.Key == ConsoleKey.LeftArrow, false);
        yield return new KeyBinding("→", "expand", DoExpand, k => k.Key == ConsoleKey.RightArrow, false);
        yield return new KeyBinding("enter/space", "toggle", DoToggle, k => k.Key is ConsoleKey.Enter or ConsoleKey.Spacebar, false);
        yield return new KeyBinding("e", "edit", OpenInEditorAsync, k => k.Key == ConsoleKey.E);
        yield return new KeyBinding("b", "build", DoBuild, k => k.KeyChar == 'b');
        yield return new KeyBinding("/", "search", DoStartSearch, k => k.KeyChar == '/');

        var selectedProject = GetSelectedProject();
        var isRunning = selectedProject != null && ExecutionService.Instance.IsRunning(selectedProject.Path);

        yield return new KeyBinding("r", isRunning ? "re-run" : "run", DoRun, k => k.KeyChar == 'r');

        if (isRunning)
        {
            yield return new KeyBinding("s", "stop", DoStop, k => k.KeyChar == 's');
        }
    }

    private static bool MatchUpKey(ConsoleKeyInfo k) =>
        k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.P };

    private static bool MatchDownKey(ConsoleKeyInfo k) =>
        k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.N };

    private static bool MatchPageUpKey(ConsoleKeyInfo k) =>
        k.Key == ConsoleKey.PageUp || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.U };

    private static bool MatchPageDownKey(ConsoleKeyInfo k) =>
        k.Key == ConsoleKey.PageDown || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.D };

    private Task DoMoveUp() { MoveUp(); return Task.CompletedTask; }
    private Task DoMoveDown() { MoveDown(); return Task.CompletedTask; }
    private Task DoPageUp() { PageUp(10); return Task.CompletedTask; }
    private Task DoPageDown() { PageDown(10); return Task.CompletedTask; }
    private Task DoCollapse() { Collapse(); return Task.CompletedTask; }
    private Task DoExpand() { Expand(); return Task.CompletedTask; }
    private Task DoToggle() { ToggleExpand(); return Task.CompletedTask; }

    private Task DoBuild()
    {
        var node = GetSelectedNode();

        // If it's a solution or project, build it directly
        if ((node.IsSolution || node.IsProject) && node.ProjectPath != null)
        {
            OnRequestBuild?.Invoke(node.ProjectPath, node.Name);
            return Task.CompletedTask;
        }

        // For folder nodes, build all child projects
        var projects = GetAllChildProjects(node).ToList();
        if (projects.Count > 0) OnRequestBuildProjects?.Invoke(projects);
        return Task.CompletedTask;
    }

    private Task DoRun()
    {
        var node = GetSelectedNode();
        if (node is { IsProject: true, ProjectInfo: not null })
        {
            OnRequestRun?.Invoke(node.ProjectInfo);
        }
        else
        {
            var runnableProjects = GetAllChildProjects(node).Where(p => p.IsRunnable).ToList();
            if (runnableProjects.Count > 0) OnRequestRunProjects?.Invoke(runnableProjects);
        }
        return Task.CompletedTask;
    }

    private Task DoStop()
    {
        var project = GetSelectedProject();
        if (project != null) OnRequestStop?.Invoke(project);
        return Task.CompletedTask;
    }

    private Task DoStartSearch()
    {
        onSearchRequested?.Invoke();
        return Task.CompletedTask;
    }

    public void RequestSearch() => onSearchRequested?.Invoke();

    public Action? OnSearchRequested
    {
        get => onSearchRequested;
        set => onSearchRequested = value;
    }

    public Action<ProjectInfo>? OnRequestRun { get; set; }
    public Action<ProjectInfo>? OnRequestStop { get; set; }
    public Action<string, string>? OnRequestBuild { get; set; }
    public Action<List<ProjectInfo>>? OnRequestBuildProjects { get; set; }
    public Action<List<ProjectInfo>>? OnRequestRunProjects { get; set; }

    private async Task OpenInEditorAsync()
    {
        var node = GetSelectedNode();
        if (node.ProjectPath != null)
        {
            await editorService.OpenFileAsync(node.ProjectPath);
        }
    }

    private void MoveUp()
    {
        if (_selectedIndex == -1 && _visibleNodes.Count > 0)
        {
            _selectedIndex = _visibleNodes.Count - 1;
            return;
        }
        if (_selectedIndex <= 0)
            return;
        _selectedIndex--;
        if (_selectedIndex < _scrollOffset)
            _scrollOffset = _selectedIndex;
    }

    private void MoveDown()
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

    private void PageUp(int pageSize)
    {
        if (_visibleNodes.Count == 0) return;
        if (_selectedIndex == -1) _selectedIndex = _visibleNodes.Count - 1;
        _selectedIndex = Math.Max(0, _selectedIndex - pageSize);
    }

    private void PageDown(int pageSize)
    {
        if (_visibleNodes.Count == 0) return;
        if (_selectedIndex == -1) _selectedIndex = 0;
        _selectedIndex = Math.Min(_visibleNodes.Count - 1, _selectedIndex + pageSize);
    }

    private void Collapse()
    {
        var node = GetSelectedNode();

        if (node.IsExpanded && (!node.IsProject || node.Children.Count > 0))
        {
            node.IsExpanded = false;
            RefreshVisibleNodes();
        }
        else if (node.Parent != null)
        {
            var parentIndex = _visibleNodes.IndexOf(node.Parent);
            if (parentIndex >= 0) _selectedIndex = parentIndex;
        }
    }

    private void Expand()
    {
        var node = GetSelectedNode();

        if (node is { IsProject: true, Children.Count: <= 0 } || node.IsExpanded) return;
        node.IsExpanded = true;
        RefreshVisibleNodes();
    }

    private void ToggleExpand()
    {
        var node = GetSelectedNode();
        if (node is { IsProject: true, Children.Count: <= 0 }) return;
        node.IsExpanded = !node.IsExpanded;
        RefreshVisibleNodes();
    }

    private void EnsureVisible(int height)
    {
        var contentHeight = Math.Max(1, height);

        // Handle unselected state
        if (_selectedIndex == -1)
        {
            _scrollOffset = 0;
            return;
        }

        if (_selectedIndex < _scrollOffset)
        {
            _scrollOffset = _selectedIndex;
        }
        else if (_selectedIndex >= _scrollOffset + contentHeight)
        {
            _scrollOffset = _selectedIndex - contentHeight + 1;
        }

        if (_scrollOffset > _visibleNodes.Count - contentHeight)
            _scrollOffset = Math.Max(0, _visibleNodes.Count - contentHeight);

        if (_scrollOffset < 0) _scrollOffset = 0;
    }

    private ExplorerNode GetSelectedNode()
    {
        if (_root == null) return new ExplorerNode { Name = "Loading..." };
        if (_visibleNodes.Count == 0) return _root;
        if (_selectedIndex < 0 || _selectedIndex >= _visibleNodes.Count) 
            _selectedIndex = 0;
        return _visibleNodes[_selectedIndex];
    }

    public void SelectProjectByPath(string path)
    {
        if (_root == null) return;

        var allNodes = new List<ExplorerNode>();
        CollectAll(_root);

        var targetNode = allNodes
            .FirstOrDefault(n => n is { IsProject: true, ProjectPath: not null } &&
                                 Path.GetFullPath(n.ProjectPath)
                                     .Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

        if (targetNode == null)
            return;

        var current = targetNode.Parent;
        while (current != null)
        {
            current.IsExpanded = true;
            current = current.Parent;
        }

        RefreshVisibleNodes();

        var index = _visibleNodes.IndexOf(targetNode);
        if (index >= 0)
        {
            _selectedIndex = index;
        }

        return;

        void CollectAll(ExplorerNode node)
        {
            allNodes.Add(node);
            foreach (var child in node.Children)
            {
                CollectAll(child);
            }
        }
    }

    public ProjectInfo? GetSelectedProject()
    {
        if (_root == null) return null;
        var node = GetSelectedNode();

        if (node.ProjectInfo != null)
        {
            // For solution folders, try to find a physical directory
            if (node.ProjectInfo.IsSolutionFolder && _solutionRootPath != null)
            {
                var physicalPath = FindPhysicalPathForFolder(node);
                if (physicalPath != null && Directory.Exists(physicalPath))
                {
                    return new ProjectInfo 
                    { 
                        Name = node.Name, 
                        Path = physicalPath, 
                        Id = physicalPath,
                        IsSolutionFolder = true
                    };
                }
            }
            return node.ProjectInfo;
        }

        if (node.ProjectPath != null)
        {
            return new ProjectInfo { Name = node.Name, Path = node.ProjectPath, Id = node.ProjectPath };
        }
        return null;
    }

    private string? FindPhysicalPathForFolder(ExplorerNode node)
    {
        if (_solutionRootPath == null) return null;
        
        // Build path from folder hierarchy
        var pathParts = new List<string>();
        var current = node;
        while (current != null && current != _root)
        {
            if (!current.IsSolution && !current.IsProject && !string.IsNullOrEmpty(current.Name))
            {
                pathParts.Insert(0, current.Name);
            }
            current = current.Parent;
        }
        
        if (pathParts.Count == 0) return null;
        
        var potentialPath = Path.Combine(_solutionRootPath, Path.Combine(pathParts.ToArray()));
        return Directory.Exists(potentialPath) ? potentialPath : null;
    }

    private static IEnumerable<ProjectInfo> GetAllChildProjects(ExplorerNode node)
    {
        if (node is { IsProject: true, ProjectInfo: not null })
        {
            yield return node.ProjectInfo;
            yield break;
        }

        foreach (var project in node.Children.SelectMany(GetAllChildProjects))
        {
            yield return project;
        }
    }

    public IRenderable GetContent(int availableHeight, int availableWidth, bool isActive, bool suppressHighlight = false)
    {
        if (_root == null)
        {
            return new Markup("[yellow]Loading projects...[/]");
        }

        EnsureVisible(availableHeight);
        var grid = new Grid();
        grid.AddColumn();

        var end = Math.Min(_scrollOffset + availableHeight, _visibleNodes.Count);

        for (var i = _scrollOffset; i < end; i++)
        {
            var node = _visibleNodes[i];
            var isSelected = i == _selectedIndex;
            grid.AddRow(RenderNode(node, isSelected, isActive, availableWidth, suppressHighlight));
        }
        return grid;
    }

    private Markup RenderNode(ExplorerNode node, bool isSelected, bool isActive, int availableWidth, bool suppressHighlight)
    {
        var indent = new string(' ', node.Depth * 2);
        var icon = GetNodeIcon(node);
        var name = GetTruncatedName(node.Name, node.Depth, availableWidth);

        var runningStatus = "";
        if (node is { IsProject: true, ProjectPath: not null } && ExecutionService.Instance.IsRunning(node.ProjectPath))
        {
            runningStatus = " [bold green](R)[/]";
        }

        // Highlight search matches in the name
        var displayName = string.IsNullOrEmpty(_searchQuery)
            ? Markup.Escape(name)
            : HighlightMatch(name, _searchQuery);

        if (!isSelected)
            return new Markup($"[white]{indent} {icon} {displayName}{runningStatus}[/]");

        if (isActive)
        {
            return new Markup($"{indent} [black on blue]{Markup.Remove(icon)} {displayName}[/]{runningStatus}");
        }

        if (suppressHighlight)
        {
            return new Markup($"[white]{indent} {icon} {displayName}{runningStatus}[/]");
        }

        return new Markup($"{indent} [bold yellow]{icon} {displayName}[/]{runningStatus}");
    }

    private static string GetNodeIcon(ExplorerNode node)
    {
        if (node.IsSlnx) return "[dodgerblue1]SLNX[/]";
        if (node.IsSlnf) return "[cyan]SLNF[/]";
        if (node.IsSolution) return "[purple]SLN[/]";
        if (node.IsProject) return "[green]C#[/]";
        return node.IsExpanded ? "[yellow]v[/]" : "[yellow]>[/]";
    }

    private static string GetTruncatedName(string name, int depth, int availableWidth)
    {
        var usedWidth = depth * 2 + 6;
        var maxNameWidth = Math.Max(5, availableWidth - usedWidth - 1);
        return name.Length > maxNameWidth
            ? string.Concat(name.AsSpan(0, maxNameWidth - 3), "...")
            : name;
    }

    private List<int> _searchMatches = [];
    private int _currentSearchMatchIndex = -1;
    private string _searchQuery = string.Empty;

    private void ClearSearch()
    {
        _searchMatches = [];
        _currentSearchMatchIndex = -1;
        _searchQuery = string.Empty;
    }

    public void StartSearch() => ClearSearch();

    public void ExitSearch() => ClearSearch();

    public List<int> UpdateSearchQuery(string query)
    {
        _searchQuery = query;
        if (string.IsNullOrWhiteSpace(query) || _root == null)
        {
            _searchMatches = [];
            _currentSearchMatchIndex = -1;
            return _searchMatches;
        }

        _searchMatches = [];
        var comparer = StringComparison.OrdinalIgnoreCase;

        for (var i = 0; i < _visibleNodes.Count; i++)
        {
            if (_visibleNodes[i].Name.Contains(query, comparer))
            {
                _searchMatches.Add(i);
            }
        }

        _currentSearchMatchIndex = _searchMatches.Count > 0 ? 0 : -1;

        // Jump to first match
        if (_currentSearchMatchIndex >= 0)
        {
            _selectedIndex = _searchMatches[_currentSearchMatchIndex];
        }

        return _searchMatches;
    }

    private static string HighlightMatch(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Markup.Escape(text);

        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return Markup.Escape(text);

        var before = text[..index];
        var match = text.Substring(index, query.Length);
        var after = text[(index + query.Length)..];

        return $"{Markup.Escape(before)}[yellow]{Markup.Escape(match)}[/]{HighlightMatch(after, query)}";
    }

    public void NextSearchMatch()
    {
        if (_searchMatches.Count == 0) return;
        _currentSearchMatchIndex = (_currentSearchMatchIndex + 1) % _searchMatches.Count;
        _selectedIndex = _searchMatches[_currentSearchMatchIndex];
    }

    public void PreviousSearchMatch()
    {
        if (_searchMatches.Count == 0) return;
        _currentSearchMatchIndex = (_currentSearchMatchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
        _selectedIndex = _searchMatches[_currentSearchMatchIndex];
    }
}
