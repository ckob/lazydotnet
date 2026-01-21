using lazydotnet.Core;
using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;

namespace lazydotnet.UI;

public class ExplorerNode
{
    public string Name { get; set; } = string.Empty;
    public string? ProjectPath { get; set; }
    public bool IsProject { get; set; }
    public bool IsSolution { get; set; }
    public bool IsSlnx { get; set; }
    public bool IsSlnf { get; set; }
    public bool IsExpanded { get; set; } = true;
    public int Depth { get; set; }
    public List<ExplorerNode> Children { get; } = [];
    public ExplorerNode? Parent { get; set; }
}

public class SolutionExplorer(IEditorService editorService) : IKeyBindable
{
    private ExplorerNode? _root;
    private readonly List<ExplorerNode> _visibleNodes = [];
    private int _selectedIndex;
    private int _scrollOffset;

    public void SetSolution(SolutionInfo solution)
    {
        editorService.RootPath = Path.GetDirectoryName(solution.Path);
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
                Depth = 0
            };
        }

        var root = new ExplorerNode
        {
            Name = solution.Name,
            Depth = 0,
            IsExpanded = true,
            IsProject = false,
            IsSolution = !isSingleProject,
            IsSlnx = solution.IsSlnx,
            IsSlnf = solution.IsSlnf,
            ProjectPath = solution.Path
        };

        var solutionDir = Path.GetDirectoryName(solution.Path) ?? "";
        var nodeMap = InitializeNodeMap(solution);

        // Second pass: build hierarchy
        foreach (var proj in solution.Projects)
        {
            AddProjectToHierarchy(root, nodeMap, proj, solutionDir);
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
                IsProject = true,
                ProjectPath = proj.Path,
                IsExpanded = true
            };
        }
        return nodeMap;
    }

    private static void AddProjectToHierarchy(ExplorerNode root, Dictionary<string, ExplorerNode> nodeMap, ProjectInfo proj, string solutionDir)
    {
        var node = nodeMap[proj.Id];
        var relativePath = Path.GetRelativePath(solutionDir, Path.GetDirectoryName(proj.Path) ?? "");

        if (relativePath == "." || string.IsNullOrEmpty(relativePath))
        {
            root.Children.Add(node);
            node.Parent = root;
            return;
        }

        var segments = relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var currentParent = root;
        var pathAccumulator = "";

        foreach (var segment in segments)
        {
            pathAccumulator = string.IsNullOrEmpty(pathAccumulator) ? segment : Path.Combine(pathAccumulator, segment);
            currentParent = GetOrCreateFolderNode(currentParent, nodeMap, segment, pathAccumulator, solutionDir);
        }

        currentParent.Children.Add(node);
        node.Parent = currentParent;
    }

    private static ExplorerNode GetOrCreateFolderNode(ExplorerNode currentParent, Dictionary<string, ExplorerNode> nodeMap, string segment, string pathAccumulator, string solutionDir)
    {
        var folderId = $"folder:{pathAccumulator}";

        if (nodeMap.TryGetValue(folderId, out var folderNode))
        {
            return folderNode;
        }

        folderNode = new ExplorerNode
        {
            Name = segment,
            IsProject = false,
            IsSolution = false,
            IsExpanded = true,
            ProjectPath = Path.GetFullPath(Path.Combine(solutionDir, pathAccumulator)),
            Parent = currentParent
        };
        currentParent.Children.Add(folderNode);
        nodeMap[folderId] = folderNode;
        return folderNode;
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


        if (node.IsSolution || node.IsProject) return true;

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
        yield return new KeyBinding("k", "up", () =>
        {
            MoveUp();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.P }, false);

        yield return new KeyBinding("j", "down", () =>
        {
            MoveDown();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.N }, false);

        yield return new KeyBinding("←", "collapse", () =>
        {
            Collapse();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.LeftArrow, false);

        yield return new KeyBinding("→", "expand", () =>
        {
            Expand();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.RightArrow, false);

        yield return new KeyBinding("enter/space", "toggle", () =>
        {
            ToggleExpand();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.Enter || k.Key == ConsoleKey.Spacebar, false);

        yield return new KeyBinding("e/o", "open", OpenInEditorAsync, k => k.Key == ConsoleKey.E || k.Key == ConsoleKey.O);
    }

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

    public bool HandleInput(ConsoleKeyInfo key)
    {
        var binding = GetKeyBindings().FirstOrDefault(b => b.Match(key));
        if (binding != null)
        {
            _ = binding.Action();
            return true;
        }
        return false;
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
        if (_selectedIndex == -1) return _visibleNodes[0];
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

        if (node.ProjectPath != null)
        {
            return new ProjectInfo { Name = node.Name, Path = node.ProjectPath, Id = node.ProjectPath };
        }
        return null;
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

    private static Markup RenderNode(ExplorerNode node, bool isSelected, bool isActive, int availableWidth, bool suppressHighlight)
    {
        var indent = new string(' ', node.Depth * 2);
        var icon = GetNodeIcon(node);
        var name = GetTruncatedName(node.Name, node.Depth, availableWidth);

        if (!isSelected)
            return new Markup($"[white]{indent} {icon} {Markup.Escape(name)}[/]");

        if (isActive)
        {
            return new Markup($"{indent} [black on blue]{Markup.Remove(icon)} {Markup.Escape(name)}[/]");
        }

        if (suppressHighlight)
        {
            return new Markup($"[white]{indent} {icon} {Markup.Escape(name)}[/]");
        }

        return new Markup($"{indent} [bold yellow]{icon} {Markup.Escape(name)}[/]");
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
}
