using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Services;
using lazydotnet.UI.Components;

namespace lazydotnet.UI;

public class ProjectReferencesTab(SolutionService solutionService) : IProjectTab
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
            // Check if we are still on the same project
            if (_currentProjectPath == projectPath)
            {
                _refsList.SetItems(refs);
            }
        }
        catch (Exception)
        {
            // Ideally log error, but for now we just show empty or previous state
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
        }
        return false;
    }

    public IRenderable GetContent(int availableHeight, int availableWidth)
    {
        var grid = new Grid();
        grid.AddColumn();

        if (_currentProjectPath == null)
        {
             return grid; // Or some empty state
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

        // Subtract for header/etc if needed, but usually just fill available
        // Original code calculation: int visibleRows = Math.Max(1, maxRows - 2);
        // We will assume availableHeight is the content area height.
        int visibleRows = Math.Max(1, availableHeight);
        var (start, end) = _refsList.GetVisibleRange(visibleRows);

        for (int i = start; i < end; i++)
        {
            var refName = _refsList.Items[i];
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

        // Scroll indicator is handled by the parent often, or we can add it here if GetContent returns the whole panel content.
        // The interface says GetContent returns IRenderable. The previous implementation returned a Grid that included the scroll indicator.
        // Let's add it if there is space?
        // Actually, the previous implementation added it as a row.
        // But here we might run out of height if we consumed satisfied visibleRows = availableHeight.
        // Let's use availableHeight - 1 for list if we have indicator?
        // To keep it simple and consistent with previous behavior, let's just return the list rows.
        // The parent ProjectDetailsPane seemed to append the status message at the bottom.
        // Wait, the previous implementation RenderReferencesTab added rows to a passed Grid.
        // Here we return a Grid.

        // Let's re-read ProjectDetailsPane.cs:
        // RenderReferencesTab(grid, availableHeight);
        // int visibleRows = Math.Max(1, maxRows - 2);
        // ...
        // var indicator = _refsList.GetScrollIndicator(visibleRows);
        // if (indicator != null) grid.AddRow(...)

        // So we should probably reserve space for indicator if needed.

        return grid;
    }
}
