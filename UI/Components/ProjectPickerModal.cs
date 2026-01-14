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
        }, k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.P), false);

        yield return new KeyBinding("j", "down", () =>
        {
            _projectList.MoveDown();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.N), false);

        yield return new KeyBinding("enter", "select", () =>
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
        grid.AddColumn(new GridColumn().NoWrap());

        if (_projectList.Count == 0)
        {
            grid.AddRow(new Markup("[dim]No projects available.[/]"));
        }
        else
        {
            var maxItemWidth = 0;
            foreach (var project in _projectList.Items)
            {
                var displayPath = _rootPath != null ? Path.GetRelativePath(_rootPath, project.Path) : project.Path;
                var len = project.Name.Length + displayPath.Length + 4;
                if (len > maxItemWidth) maxItemWidth = len;
            }

            var modalWidth = Math.Min(width - 8, maxItemWidth + 10);
            modalWidth = Math.Max(modalWidth, 40);
            Width = modalWidth;

            var gridAvailableWidth = modalWidth - 8;

            var visibleRows = Math.Min(25, height - 10);
            var (start, end) = _projectList.GetVisibleRange(visibleRows);

            for (var i = start; i < end; i++)
            {
                var project = _projectList.Items[i];
                var isSelected = i == _projectList.SelectedIndex;

                var displayPath = _rootPath != null ? Path.GetRelativePath(_rootPath, project.Path) : project.Path;
                var name = project.Name;
                var path = $"({displayPath})";

                if (name.Length + path.Length + 1 > gridAvailableWidth)
                {
                    var availableForPath = gridAvailableWidth - name.Length - 1;
                    if (availableForPath >= 7)
                    {
                        var pathTextMax = availableForPath - 2;
                        var side = (pathTextMax - 3) / 2;
                        path = $"({displayPath[..side]}...{displayPath[^side..]})";
                    }
                    else
                    {
                        if (name.Length > gridAvailableWidth - 3)
                            name = name[..(gridAvailableWidth - 3)] + "...";
                        path = "";
                    }
                }

                var currentLen = name.Length + (string.IsNullOrWhiteSpace(path) ? 0 : path.Length + 1);
                var padding = new string(' ', Math.Max(0, gridAvailableWidth - currentLen));

                string line;
                if (isSelected)
                {
                    var plainText = string.IsNullOrWhiteSpace(path) ? name : $"{name} {path}";
                    line = $"[black on blue]{Markup.Escape(plainText)}{padding}[/]";
                }
                else
                {
                    var content = string.IsNullOrWhiteSpace(path)
                        ? Markup.Escape(name)
                        : $"{Markup.Escape(name)} [dim]{Markup.Escape(path)}[/]";
                    line = $"{content}{padding}";
                }

                grid.AddRow(new Markup(line));
            }
        }

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
