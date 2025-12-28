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
    public bool IsExpanded { get; set; } = true;
    public int Depth { get; set; }
    public List<ExplorerNode> Children { get; } = [];
    public ExplorerNode? Parent { get; set; }
}

public class SolutionExplorer
{
    private readonly ExplorerNode _root;
    private readonly List<ExplorerNode> _visibleNodes = [];
    private int _selectedIndex = 0;
    private int _scrollOffset = 0;

    public SolutionExplorer(SolutionInfo solution)
    {
        _root = BuildTree(solution);
        RefreshVisibleNodes();
    }

    private static ExplorerNode BuildTree(SolutionInfo solution)
    {
        var root = new ExplorerNode
        {
            Name = solution.Name,
            Depth = 0,
            IsExpanded = true,
            IsProject = false,
            IsSolution = true,
            ProjectPath = solution.Path
        };

        var nodeMap = new Dictionary<string, ExplorerNode>(StringComparer.OrdinalIgnoreCase);


        foreach (var proj in solution.Projects)
        {

            var node = new ExplorerNode
            {
                Name = proj.Name,
                IsProject = !proj.IsSolutionFolder,
                ProjectPath = proj.IsSolutionFolder ? null : proj.Path,
                IsExpanded = true
            };
            nodeMap[proj.Id] = node;
        }


        foreach (var proj in solution.Projects)
        {
            var node = nodeMap[proj.Id];

            if (!string.IsNullOrEmpty(proj.ParentId) && nodeMap.TryGetValue(proj.ParentId, out ExplorerNode? parent))
            {
                parent.Children.Add(node);
                node.Parent = parent;
            }
            else
            {
                root.Children.Add(node);
                node.Parent = root;
            }
        }


        PruneTree(root);

        CalculateDepths(root, 0);
        SortTree(root);
        return root;
    }

    private static bool PruneTree(ExplorerNode node)
    {

        bool hasContent = false;
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {

            bool childKept = PruneTree(node.Children[i]);
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
            if (a.IsProject == b.IsProject) return string.Compare(a.Name, b.Name);

            return a.IsProject ? 1 : -1;
        });

        foreach (var child in node.Children)
            SortTree(child);
    }

    private void RefreshVisibleNodes()
    {
        _visibleNodes.Clear();
        Traverse(_root);
    }

    private void Traverse(ExplorerNode node)
    {
        _visibleNodes.Add(node);
        if (node.IsExpanded)
        {
             foreach(var child in node.Children)
                Traverse(child);
        }
    }

    public bool HandleInput(ConsoleKeyInfo key)
    {
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
            case ConsoleKey.LeftArrow:
                Collapse();
                return true;
            case ConsoleKey.RightArrow:
                Expand();
                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                ToggleExpand();
                return true;
        }
        return false;
    }

    public void ToggleExpand()
    {
        var node = GetSelectedNode();
        if (!node.IsProject || node.Children.Count > 0)
        {
            node.IsExpanded = !node.IsExpanded;
            RefreshVisibleNodes();
        }
    }

    public void Expand()
    {
        var node = GetSelectedNode();

        if ((!node.IsProject || node.Children.Count > 0) && !node.IsExpanded)
        {
            node.IsExpanded = true;
            RefreshVisibleNodes();
        }
    }

    public void Collapse()
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

    public void MoveUp()
    {
        if (_selectedIndex > 0)
        {
            _selectedIndex--;
            if (_selectedIndex < _scrollOffset) _scrollOffset = _selectedIndex;
        }
    }

    public void MoveDown()
    {
        if (_selectedIndex < _visibleNodes.Count - 1)
        {
            _selectedIndex++;
        }
    }

    private void EnsureVisible(int height)
    {
        int contentHeight = Math.Max(1, height);
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
    }

    public ExplorerNode GetSelectedNode()
    {
        if (_visibleNodes.Count == 0) return _root;
        return _visibleNodes[_selectedIndex];
    }

    public ProjectInfo? GetSelectedProject()
    {
        var node = GetSelectedNode();

        if ((node.IsProject || node.IsSolution) && node.ProjectPath != null)
        {
            return new ProjectInfo { Name = node.Name, Path = node.ProjectPath };
        }
        return null;
    }

    public IRenderable GetContent(int availableHeight, int availableWidth)
    {
        EnsureVisible(availableHeight);
        var grid = new Grid();
        grid.AddColumn();

        int end = Math.Min(_scrollOffset + availableHeight, _visibleNodes.Count);

        for (int i = _scrollOffset; i < end; i++)
        {
            var node = _visibleNodes[i];
            bool isSelected = i == _selectedIndex;

            string indent = new(' ', node.Depth * 2);
            string icon;
            if (node.IsSolution)
            {
                 icon = "[purple]SLN[/]";
            }
            else if (node.IsProject)
            {
                 icon = "[green]C#[/]";
            }
            else
            {
                 icon = node.IsExpanded ? "[yellow]v[/]" : "[yellow]>[/]";
            }

            string name = node.Name;
            int usedWidth = (node.Depth * 2) + 6;
            int maxNameWidth = Math.Max(5, availableWidth - usedWidth - 1);
            if (name.Length > maxNameWidth) name = string.Concat(name.AsSpan(0, maxNameWidth - 3), "...");

            string text = $"{indent} {icon} {Markup.Escape(name)}";

            if (isSelected)
            {
                int visibleLength = (node.Depth * 2) + (node.IsSolution ? 4 : (node.IsProject ? 3 : 2)) + name.Length + 2;
                int paddingNeeded = Math.Max(0, availableWidth - visibleLength);
                string padding = new(' ', paddingNeeded);

                grid.AddRow(new Markup($"[black on blue]{text}{padding}[/]"));
            }
            else
            {
                 grid.AddRow(new Markup($"[white]{text}[/]"));
            }
        }
        return grid;
    }
}
