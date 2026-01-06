using lazydotnet.Core;
using lazydotnet.Services;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public class ProjectPickerModal : Modal
{
    private readonly ScrollableList<ProjectInfo> _projectList = new();
    private readonly Action<ProjectInfo> _onSelected;
    private readonly string? _rootPath;

    public ProjectPickerModal(string title, List<ProjectInfo> projects, string? rootPath, Action<ProjectInfo> onSelected, Action onClose)
        : base(title, new Markup("Loading..."), onClose)
    {
        _projectList.SetItems(projects);
        _onSelected = onSelected;
        _rootPath = rootPath;
        Width = 80;
    }

    public override IEnumerable<KeyBinding> GetKeyBindings()
    {
        foreach (var b in base.GetKeyBindings()) yield return b;

        yield return new KeyBinding("k", "up", () =>
        {
            _projectList.MoveUp();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K, false);

        yield return new KeyBinding("j", "down", () =>
        {
            _projectList.MoveDown();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J, false);

        yield return new KeyBinding("Enter", "select", () =>
        {
            if (_projectList.SelectedItem != null)
            {
                _onSelected(_projectList.SelectedItem);
            }
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.Enter);
    }

    public override IRenderable GetRenderable(int width, int height)
    {
        var grid = new Grid();
        // Set an explicit width for the grid column as well
        grid.AddColumn(new GridColumn().NoWrap());

        if (_projectList.Count == 0)
        {
            grid.AddRow(new Markup("[dim]No projects available.[/]"));
        }
        else
        {
            int maxItemWidth = 0;
            foreach (var project in _projectList.Items)
            {
                string displayPath = _rootPath != null ? Path.GetRelativePath(_rootPath, project.Path) : project.Path;
                int len = project.Name.Length + displayPath.Length + 4; // name + space + (path)
                if (len > maxItemWidth) maxItemWidth = len;
            }

            // Increase overhead to be absolutely safe
            // Borders (2) + Padder (4) + Safety (4) = 10
            int modalWidth = Math.Min(width - 8, maxItemWidth + 10);
            modalWidth = Math.Max(modalWidth, 40);
            Width = modalWidth;

            // Available width for the grid content
            int gridAvailableWidth = modalWidth - 8;

            int visibleRows = Math.Min(25, height - 10);
            var (start, end) = _projectList.GetVisibleRange(visibleRows);

            for (int i = start; i < end; i++)
            {
                var project = _projectList.Items[i];
                bool isSelected = i == _projectList.SelectedIndex;
                
                string displayPath = _rootPath != null ? Path.GetRelativePath(_rootPath, project.Path) : project.Path;
                string name = project.Name;
                string path = $"({displayPath})";
                
                if (name.Length + path.Length + 1 > gridAvailableWidth)
                {
                    int availableForPath = gridAvailableWidth - name.Length - 1;
                    if (availableForPath >= 7)
                    {
                        int pathTextMax = availableForPath - 2;
                        int side = (pathTextMax - 3) / 2;
                        path = $"({displayPath[..side]}...{displayPath[^side..]})";
                    }
                    else
                    {
                        if (name.Length > gridAvailableWidth - 3)
                            name = name[..(gridAvailableWidth - 3)] + "...";
                        path = "";
                    }
                }

                int currentLen = name.Length + (string.IsNullOrWhiteSpace(path) ? 0 : path.Length + 1);
                string padding = new string(' ', Math.Max(0, gridAvailableWidth - currentLen));

                string line;
                if (isSelected)
                {
                    // Remove nested [dim] for selected items to simplify rendering
                    string plainText = string.IsNullOrWhiteSpace(path) ? name : $"{name} {path}";
                    line = $"[black on blue]{Markup.Escape(plainText)}{padding}[/]";
                }
                else
                {
                    string content = string.IsNullOrWhiteSpace(path) 
                        ? Markup.Escape(name) 
                        : $"{Markup.Escape(name)} [dim]{Markup.Escape(path)}[/]";
                    line = $"{content}{padding}";
                }

                grid.AddRow(new Markup(line));
            }
        }

        // Wrap the grid in a fixed-width panel with explicit expansion off
        var panel = new Panel(new Padder(grid, new Padding(2, 1, 2, 1)))
        {
            Header = new PanelHeader($"[bold yellow] {Title} [/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue),
            Expand = false,
            Width = Width
        };

        return panel;
    }
}
