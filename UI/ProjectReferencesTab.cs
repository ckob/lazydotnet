using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;
using lazydotnet.UI.Components;

namespace lazydotnet.UI;

public class ProjectReferencesTab(SolutionService solutionService, IEditorService editorService) : IProjectTab
{
    private readonly ScrollableList<string> _refsList = new();
    private bool _isLoading;
    private string? _currentProjectPath;

    public Action? RequestRefresh { get; set; }

    public string Title => "Project References";

    public void MoveUp() => _refsList.MoveUp();

    public void MoveDown() => _refsList.MoveDown();

    public string? GetScrollIndicator()
    {
        if (_currentProjectPath == null || _isLoading || _refsList.Count == 0) return null;
        return $"{_refsList.SelectedIndex + 1} of {_refsList.Count}";
    }

    public void ClearData()
    {
        _refsList.Clear();
        _currentProjectPath = null;
        _isLoading = false;
    }

    public async Task LoadAsync(string projectPath, string projectName, bool force = false)
    {
        if (!force && _currentProjectPath == projectPath && !_isLoading) return;

        _currentProjectPath = projectPath;
        _isLoading = true;
        _refsList.Clear();

        try
        {
            var refs = await solutionService.GetProjectReferencesAsync(projectPath);
            if (_currentProjectPath == projectPath)
            {
                _refsList.SetItems(refs);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            if (_currentProjectPath == projectPath)
            {
                _isLoading = false;
            }
        }
    }

    public async Task<bool> HandleKeyAsync(ConsoleKeyInfo key)
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
            case ConsoleKey.E:
            case ConsoleKey.O:
                await OpenInEditorAsync();
                return true;
        }
        return false;
    }

    private async Task OpenInEditorAsync()
    {
        if (_refsList.SelectedItem != null)
        {
            await editorService.OpenFileAsync(_refsList.SelectedItem);
        }
    }

    public IRenderable GetContent(int availableHeight, int availableWidth)
    {
        var grid = new Grid();
        grid.AddColumn();

        if (_currentProjectPath == null)
        {
             return grid;
        }

        if (_isLoading)
        {
             grid.AddRow(new Markup("[yellow]Loading references...[/]"));
             return grid;
        }

        if (_refsList.Count == 0)
        {
            grid.AddRow(new Markup("[dim]No project references found.[/]"));
            return grid;
        }

        int visibleRows = Math.Max(1, availableHeight);
        var (start, end) = _refsList.GetVisibleRange(visibleRows);

        for (int i = start; i < end; i++)
        {
            var refPath = _refsList.Items[i];
            var refName = Path.GetFileNameWithoutExtension(refPath);
            bool isSelected = i == _refsList.SelectedIndex;

            if (isSelected)
            {
                grid.AddRow(new Markup($"[black on blue]  → {Markup.Escape(refName)}[/]"));
            }
            else
            {
                grid.AddRow(new Markup($"  [green]→[/] {Markup.Escape(refName)}"));
            }
        }

        return grid;
    }
}
