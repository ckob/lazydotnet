using lazydotnet.Core;
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
    public Action<Modal>? RequestModal { get; set; }
    public Action<string>? RequestSelectProject { get; set; }

    public string Title => "Project References";

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        if (_isLoading) yield break;

        yield return new KeyBinding("k", "up", () =>
        {
            MoveUp();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K, false);

        yield return new KeyBinding("j", "down", () =>
        {
            MoveDown();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J, false);

        yield return new KeyBinding("a", "add", AddReferenceAsync, k => k.KeyChar == 'a');

        if (_refsList.SelectedItem != null)
        {
            yield return new KeyBinding("Enter", "select in explorer", () =>
            {
                RequestSelectProject?.Invoke(_refsList.SelectedItem);
                return Task.CompletedTask;
            }, k => k.Key == ConsoleKey.Enter);
            yield return new KeyBinding("e/o", "open", OpenInEditorAsync, k => k.Key == ConsoleKey.E || k.Key == ConsoleKey.O);
            yield return new KeyBinding("d", "delete", RemoveReferenceAsync, k => k.KeyChar == 'd');
        }
    }

    public void MoveUp() => _refsList.MoveUp();

    public void MoveDown() => _refsList.MoveDown();

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

    private async Task OpenInEditorAsync()
    {
        if (_refsList.SelectedItem != null)
        {
            await editorService.OpenFileAsync(_refsList.SelectedItem);
        }
    }

    private async Task AddReferenceAsync()
    {
        if (_currentProjectPath == null || solutionService.CurrentSolution == null) return;

        var currentRefs = _refsList.Items.Select(Path.GetFullPath).ToList();
        var projects = solutionService.CurrentSolution.Projects
            .Where(p => Path.GetFullPath(p.Path) != Path.GetFullPath(_currentProjectPath)
                     && !currentRefs.Contains(Path.GetFullPath(p.Path)))
            .OrderBy(p => p.Name)
            .ToList();

        var slnDir = Path.GetDirectoryName(solutionService.CurrentSolution.Path);
        var picker = new ProjectPickerModal(
            "Add Project Reference",
            projects,
            slnDir,
            async selected =>
            {
                RequestModal?.Invoke(null!);
                _isLoading = true;
                RequestRefresh?.Invoke();
                try
                {
                    await solutionService.AddProjectReferenceAsync(_currentProjectPath, selected.Path);
                    await LoadAsync(_currentProjectPath, "", force: true);
                }
                finally
                {
                    _isLoading = false;
                    RequestRefresh?.Invoke();
                }
            },
            () => RequestModal?.Invoke(null!)
        );

        RequestModal?.Invoke(picker);
    }

    private async Task RemoveReferenceAsync()
    {
        if (_currentProjectPath == null || _refsList.SelectedItem == null) return;

        var targetPath = _refsList.SelectedItem;
        var refName = Path.GetFileNameWithoutExtension(targetPath);

        var confirm = new ConfirmationModal(
            "Remove Reference",
            $"Are you sure you want to remove reference to [bold]{Markup.Escape(refName)}[/]?",
            async () =>
            {
                _isLoading = true;
                RequestRefresh?.Invoke();

                try
                {
                    await solutionService.RemoveProjectReferenceAsync(_currentProjectPath, targetPath);
                    await LoadAsync(_currentProjectPath, "", force: true);
                }
                finally
                {
                    _isLoading = false;
                    RequestRefresh?.Invoke();
                }
            },
            () => RequestModal?.Invoke(null!)
        );

        RequestModal?.Invoke(confirm);
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

            string displayPath = refPath;
            if (solutionService.CurrentSolution != null)
            {
                var slnDir = Path.GetDirectoryName(solutionService.CurrentSolution.Path);
                if (slnDir != null)
                {
                    displayPath = Path.GetRelativePath(slnDir, refPath);
                }
            }

            string pathMarkup = $"({displayPath})";
            // Allow more space and avoid truncation unless strictly necessary
            int availableTextWidth = availableWidth - 6; 
            if (refName.Length + pathMarkup.Length + 4 > availableTextWidth)
            {
                int maxPathLen = availableTextWidth - refName.Length - 8;
                if (maxPathLen > 15)
                {
                    // Middle truncation for paths to preserve both ends
                    pathMarkup = $"({displayPath[..(maxPathLen / 2)]}...{displayPath[^(maxPathLen / 2)..]})";
                }
            }

            if (isSelected)
            {
                grid.AddRow(new Markup($"[black on blue]  → {Markup.Escape(refName)} [dim]{Markup.Escape(pathMarkup)}[/][/]"));
            }
            else
            {
                grid.AddRow(new Markup($"  [green]→[/] {Markup.Escape(refName)} [dim]{Markup.Escape(pathMarkup)}[/]"));
            }
        }

        return grid;
    }
}
