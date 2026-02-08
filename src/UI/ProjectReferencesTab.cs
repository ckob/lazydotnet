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
    private int _lastFrameIndex = -1;
    private string? _currentProjectPath;

    public Action? RequestRefresh { get; set; }
    public Action<Modal>? RequestModal { get; set; }
    public Action<string>? RequestSelectProject { get; set; }

    public string Title => "Project References";

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        if (_isLoading) yield break;

        yield return new KeyBinding("k/↑/ctrl+p", "up", () =>
        {
            MoveUp();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.UpArrow || k.Key == ConsoleKey.K || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.P }, false);

        yield return new KeyBinding("j/↓/ctrl+n", "down", () =>
        {
            MoveDown();
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.J || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.N }, false);

        yield return new KeyBinding("pgup/ctrl+u", "page up", () =>
        {
            PageUp(10);
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.PageUp || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.U }, false);

        yield return new KeyBinding("pgdn/ctrl+d", "page down", () =>
        {
            PageDown(10);
            return Task.CompletedTask;
        }, k => k.Key == ConsoleKey.PageDown || k is { Modifiers: ConsoleModifiers.Control, Key: ConsoleKey.D }, false);

        yield return new KeyBinding("a", "add", AddReferenceAsync, k => k.Key == ConsoleKey.A);

        if (_refsList.SelectedItem != null)
        {
            yield return new KeyBinding("enter", "select in explorer", () =>
            {
                RequestSelectProject?.Invoke(_refsList.SelectedItem);
                return Task.CompletedTask;
            }, k => k.Key == ConsoleKey.Enter);
            yield return new KeyBinding("e", "edit", OpenInEditorAsync, k => k.Key == ConsoleKey.E);
            yield return new KeyBinding("d", "delete", RemoveReferenceAsync, k => k.Key == ConsoleKey.D);
        }
    }

    private void MoveUp() => _refsList.MoveUp();

    private void MoveDown() => _refsList.MoveDown();

    private void PageUp(int pageSize) => _refsList.PageUp(pageSize);

    private void PageDown(int pageSize) => _refsList.PageDown(pageSize);

    public void ClearData()
    {
        _refsList.Clear();
        _currentProjectPath = null;
        _isLoading = false;
        _lastFrameIndex = -1;
    }

    public bool IsLoaded(string projectPath) => _currentProjectPath == projectPath && !_isLoading;

    public bool OnTick()
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

    public async Task LoadAsync(string projectPath, string projectName, bool force = false)
    {
        if (!force && _currentProjectPath == projectPath && !_isLoading) return;

        _currentProjectPath = projectPath;
        _isLoading = true;
        _refsList.Clear();

        if (Directory.Exists(projectPath) && !projectPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            _isLoading = false;
            RequestRefresh?.Invoke();
            return;
        }

        try
        {
            var refs = await ProjectService.GetProjectReferencesAsync(projectPath);
            if (_currentProjectPath == projectPath)
            {
                _refsList.SetItems(refs);
            }
        }
        catch (Exception)
        {
            // ignored
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

    private Task AddReferenceAsync()
    {
        if (_currentProjectPath == null || solutionService.CurrentSolution == null)
            return Task.CompletedTask;

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
                    await ProjectService.AddProjectReferenceAsync(_currentProjectPath, selected.Path);
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
        return Task.CompletedTask;
    }

    private Task RemoveReferenceAsync()
    {
        if (_currentProjectPath == null || _refsList.SelectedItem == null) return Task.CompletedTask;

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
                    await ProjectService.RemoveProjectReferenceAsync(_currentProjectPath, targetPath);
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
        return Task.CompletedTask;
    }

    public IRenderable GetContent(int height, int width, bool isActive)
    {
        var grid = new Grid();
        grid.AddColumn();

        if (_currentProjectPath == null)
        {
             return grid;
        }

        if (_isLoading)
        {
             grid.AddRow(new Markup($"[yellow]{SpinnerHelper.GetFrame()} Loading references...[/]"));
             return grid;
        }

        if (_refsList.Count == 0)
        {
            grid.AddRow(new Markup("[dim]No project references found.[/]"));
            return grid;
        }

        RenderReferences(grid, height, width, isActive);

        return grid;
    }

    private void RenderReferences(Grid grid, int height, int width, bool isActive)
    {
        var visibleRows = Math.Max(1, height);
        var (start, end) = _refsList.GetVisibleRange(visibleRows);

        for (var i = start; i < end; i++)
        {
            var refPath = _refsList.Items[i];
            var refName = Path.GetFileNameWithoutExtension(refPath);
            var isSelected = i == _refsList.SelectedIndex;

            var pathMarkup = GetRelativePathMarkup(refPath, width - 6, refName.Length);

            if (isSelected)
            {
                grid.AddRow(isActive
                    ? new Markup(
                        $"  [black on blue]→ {Markup.Escape(refName)} [dim]{Markup.Escape(pathMarkup)}[/][/]")
                    : new Markup($"  [green]→[/] {Markup.Escape(refName)} [dim]{Markup.Escape(pathMarkup)}[/]"));
            }
            else
            {
                grid.AddRow(new Markup($"  [green]→[/] {Markup.Escape(refName)} [dim]{Markup.Escape(pathMarkup)}[/]"));
            }
        }
    }

    private string GetRelativePathMarkup(string refPath, int availableTextWidth, int refNameLen)
    {
        var displayPath = refPath;
        if (solutionService.CurrentSolution != null)
        {
            var slnDir = Path.GetDirectoryName(solutionService.CurrentSolution.Path);
            if (slnDir != null)
            {
                displayPath = Path.GetRelativePath(slnDir, refPath);
            }
        }

        var pathMarkup = $"({displayPath})";
        if (refNameLen + pathMarkup.Length + 4 > availableTextWidth)
        {
            var maxPathLen = availableTextWidth - refNameLen - 8;
            if (maxPathLen > 15)
            {
                pathMarkup = $"({displayPath[..(maxPathLen / 2)]}...{displayPath[^(maxPathLen / 2)..]})";
            }
        }

        return pathMarkup;
    }
}
