using lazydotnet.Core;
using lazydotnet.Services;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI.Components;

public class WorkspacePickerModal : Modal
{
    private readonly string _rootPath;
    private readonly SolutionService _solutionService;
    private readonly Action<string> _onSelected;
    private readonly Action _requestRefresh;
    
    private readonly ScrollableList<SolutionInfo> _workspaceList = new();
    private bool _isLoading = true;
    private int _lastFrameIndex = -1;

    public WorkspacePickerModal(
        string rootPath, 
        SolutionService solutionService, 
        Action<string> onSelected, 
        Action onClose,
        Action requestRefresh)
        : base("Select Workspace", new Markup("Searching..."), onClose)
    {
        _rootPath = rootPath;
        _solutionService = solutionService;
        _onSelected = onSelected;
        _requestRefresh = requestRefresh;
        Width = 100;
        
        _ = LoadWorkspacesAsync();
    }

    private async Task LoadWorkspacesAsync()
    {
        try
        {
            var workspaces = await _solutionService.DiscoverWorkspacesAsync(_rootPath);
            _workspaceList.SetItems(workspaces);
        }
        finally
        {
            _isLoading = false;
            _requestRefresh();
        }
    }

    public override IEnumerable<KeyBinding> GetKeyBindings()
    {
        foreach (var b in base.GetKeyBindings()) yield return b;

        if (_isLoading) yield break;

        yield return new KeyBinding("k", "up", () =>
        {
            _workspaceList.MoveUp();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.P), false);

        yield return new KeyBinding("j", "down", () =>
        {
            _workspaceList.MoveDown();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.N), false);

        yield return new KeyBinding("pgup", "page up", () =>
        {
            _workspaceList.PageUp(10);
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.PageUp || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.U), false);

        yield return new KeyBinding("pgdn", "page down", () =>
        {
            _workspaceList.PageDown(10);
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.PageDown || (k.Modifiers == ConsoleModifiers.Control && k.Key == ConsoleKey.D), false);

        yield return new KeyBinding("enter", "select", () =>
        {
            if (_workspaceList.SelectedItem != null)
            {
                _onSelected(_workspaceList.SelectedItem.Path);
                OnClose();
            }
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.Enter);
    }

    public override bool OnTick()
    {
        if (_isLoading)
        {
            var currentFrame = SpinnerHelper.GetCurrentFrameIndex();
            if (currentFrame != _lastFrameIndex)
            {
                _lastFrameIndex = currentFrame;
                return true;
            }
        }
        return false;
    }

    public override IRenderable GetRenderable(int width, int height)
    {
        if (_isLoading)
        {
            var grid = new Grid().AddColumn();
            grid.AddRow(new Markup($"[yellow]{SpinnerHelper.GetFrame()} Discovering workspaces...[/]"));
            return CreatePanel(grid);
        }

        if (_workspaceList.Count == 0)
        {
            var grid = new Grid().AddColumn();
            grid.AddRow(new Markup("[dim]No workspaces found.[/]"));
            return CreatePanel(grid);
        }

        // Calculate needed width instead of taking all available
        var maxNameLen = _workspaceList.Items.Select(w => w.Name.Length).DefaultIfEmpty(0).Max();
        var maxPathLen = _workspaceList.Items.Select(w => GetDisplayPath(w.Path).Length).DefaultIfEmpty(0).Max();
        
        var contentWidth = 6 + maxNameLen + 4 + maxPathLen + 4;
        Width = Math.Clamp(contentWidth + 8, 60, width - 8);

        var table = new Table().Border(TableBorder.None).HideHeaders().NoSafeBorder().Expand();
        table.AddColumn(new TableColumn("Icon").Width(6).NoWrap());
        table.AddColumn(new TableColumn("Name").NoWrap());
        table.AddColumn(new TableColumn("Path").NoWrap());

        var visibleRows = Math.Max(1, height - 10);
        var (start, end) = _workspaceList.GetVisibleRange(visibleRows);

        for (var i = start; i < end; i++)
        {
            RenderWorkspaceRow(table, _workspaceList.Items[i], i == _workspaceList.SelectedIndex);
        }

        return CreatePanel(table);
    }

    private string GetDisplayPath(string fullPath)
    {
        try
        {
            var relativePath = Path.GetRelativePath(_rootPath, fullPath);
            var directory = Path.GetDirectoryName(relativePath);
            return string.IsNullOrEmpty(directory) || directory == "." ? "/" : directory;
        }
        catch { return ""; }
    }

    private void RenderWorkspaceRow(Table table, SolutionInfo workspace, bool isSelected)
    {
        var displayPath = GetDisplayPath(workspace.Path);
        var name = workspace.Name;
        var ext = Path.GetExtension(workspace.Path).ToLower();
        
        string icon = ext switch
        {
            ".slnx" => "[dodgerblue1]SLNX[/]",
            ".slnf" => "[cyan]SLNF[/]",
            ".sln" => "[purple]SLN[/]",
            ".csproj" => "[green]C#[/]",
            _ => "[grey]??[/]"
        };

        var nameMarkup = Markup.Escape(name);
        var pathMarkup = $"[dim]{Markup.Escape(displayPath)}[/]";

        if (isSelected)
        {
            table.AddRow(
                new Markup($"[black on blue]{Markup.Remove(icon)}[/]"),
                new Markup($"[black on blue]{Markup.Remove(nameMarkup)}[/]"),
                new Markup($"[black on blue]{Markup.Remove(pathMarkup)}[/]")
            );
        }
        else
        {
            table.AddRow(
                new Markup(icon),
                new Markup(nameMarkup),
                new Markup(pathMarkup)
            );
        }
    }

    private Panel CreatePanel(IRenderable content)
    {
        return new Panel(new Padder(content, new Padding(2, 1, 2, 1)))
        {
            Header = new PanelHeader($"[bold yellow] {Title} [/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue),
            Expand = false,
            Width = Width
        };
    }
}
