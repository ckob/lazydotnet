using Spectre.Console;
using lazydotnet.Core;
using lazydotnet.UI;
using lazydotnet.UI.Components;
using lazydotnet.Services;

namespace lazydotnet.Screens;

public class DashboardScreen : IScreen
{
    private readonly SolutionExplorer _explorer;
    private readonly ProjectDetailsPane _detailsPane;
    private string? _lastSelectedProjectPath;
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _buildCts;
    private bool _needsRefresh;

    private readonly AppLayout _layout;
    private readonly SolutionService _solutionService;
    private readonly string _rootDir;
    private readonly string? _solutionFile;

    public DashboardScreen(
        SolutionExplorer explorer,
        ProjectDetailsPane detailsPane,
        AppLayout layout,
        SolutionService solutionService,
        string rootDir,
        string? solutionFile)
    {
        _explorer = explorer;
        _detailsPane = detailsPane;
        _layout = layout;
        _solutionService = solutionService;
        _rootDir = rootDir;
        _solutionFile = solutionFile;

        _detailsPane.LogAction = msg => _layout.AddLog(msg);
        _detailsPane.RequestRefresh = () => _needsRefresh = true;
        _detailsPane.RequestModal = m =>
        {
            _activeModal = m;
            _needsRefresh = true;
        };
        _detailsPane.RequestSelectProject = p =>
        {
            _explorer.SelectProjectByPath(p);
            _layout.SetActivePanel(0);
            _needsRefresh = true;
        };
    }

    public void OnEnter()
    {
        _needsRefresh = true;

        _ = Task.Run(async () =>
        {
            try
            {
                var solution = await _solutionService.FindAndParseSolutionAsync(_solutionFile ?? _rootDir);
                if (solution != null)
                {
                    _explorer.SetSolution(solution);
                    _needsRefresh = true;
                }
                else
                {
                    _layout.AddLog($"[red]No solution found at {_solutionFile ?? _rootDir}[/]");
                }
            }
            catch (Exception ex)
            {
                _layout.AddLog($"[red]Error loading solution: {ex.Message}[/]");
            }
        });
    }

    public bool OnTick()
    {
        if (_activeModal != null && _activeModal.OnTick())
        {
            _needsRefresh = true;
        }

        var currentProject = _explorer.GetSelectedProject();
        var currentPath = currentProject?.Path;

        if (currentPath != _lastSelectedProjectPath)
        {
            HandleProjectChange(currentProject, currentPath);
        }

        if (_detailsPane.OnTick())
        {
            _needsRefresh = true;
        }

        var result = _needsRefresh;
        _needsRefresh = false;
        return result;
    }

    private void HandleProjectChange(ProjectInfo? currentProject, string? currentPath)
    {
        _lastSelectedProjectPath = currentPath;
        _needsRefresh = true;

        if (currentPath != null)
        {
            _detailsPane.ClearData();

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, token);
                    if (token.IsCancellationRequested) return;

                    await _detailsPane.LoadProjectDataAsync(currentPath, currentProject!.Name);
                    _needsRefresh = true;
                }
                catch (OperationCanceledException)
                {
                    // Ignore
                }
            }, token);
        }
        else
        {
            _detailsPane.ClearForNonProject();
        }
    }

    private Modal? _activeModal;

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        if (_activeModal != null)
        {
            return _activeModal.GetKeyBindings();
        }

        return GetGlobalBindings().Concat(GetPanelSpecificBindings());
    }

    private IEnumerable<KeyBinding> GetGlobalBindings()
    {
        yield return new KeyBinding("q", "quit", () => Task.FromResult<IScreen?>(null),
            k => k.Key == ConsoleKey.Q);
        yield return new KeyBinding("tab", "next panel", () => Task.CompletedTask,
            k => k.Key == ConsoleKey.Tab && (k.Modifiers & ConsoleModifiers.Shift) == 0, false);
        yield return new KeyBinding("shift+tab", "prev panel", () => Task.CompletedTask,
            k => k.Key == ConsoleKey.Tab && (k.Modifiers & ConsoleModifiers.Shift) != 0, false);
        yield return new KeyBinding("1-3", "switch panel", () => Task.CompletedTask,
            k => k.Key is ConsoleKey.D1 or ConsoleKey.D2 or ConsoleKey.D3, false);
        yield return new KeyBinding("b", "build project", () => HandleBuildAsync(_layout),
            k => k.KeyChar == 'b');
        yield return new KeyBinding("B", "build solution", () => HandleBuildAsync(_layout, true),
            k => k.KeyChar == 'B');
        yield return new KeyBinding("ctrl+r", "reload", HandleReloadAsync,
            k => k is { Key: ConsoleKey.R, Modifiers: ConsoleModifiers.Control });
        yield return new KeyBinding("?", "keybindings", () =>
        {
            ShowHelpModal();
            return Task.CompletedTask;
        }, k => k.KeyChar == '?');
    }

    private IEnumerable<KeyBinding> GetPanelSpecificBindings()
    {
        switch (_layout.ActivePanel)
        {
            case 0:
                foreach (var b in _explorer.GetKeyBindings()) yield return b;
                break;
            case 1:
                foreach (var b in _detailsPane.GetKeyBindings()) yield return b;
                break;
            case 2:
                foreach (var b in GetBottomPanelBindings()) yield return b;
                break;
        }
    }

    private IEnumerable<KeyBinding> GetBottomPanelBindings()
    {
        return _layout.LogViewer.GetKeyBindings();
    }

    private void ShowHelpModal()
    {
        var panelName = _layout.ActivePanel switch
        {
            0 => "Explorer",
            1 => "Details",
            2 => _layout.BottomActiveTab switch
            {
                0 => "Log",
                _ => "Bottom"
            },
            _ => "Local"
        };

        var localBindings = (_layout.ActivePanel switch
        {
            0 => _explorer.GetKeyBindings(),
            1 => _detailsPane.GetKeyBindings(),
            2 => [.. _layout.LogViewer.GetKeyBindings()],
            _ => []
        }).ToList();

        var globalBindings = new List<KeyBinding>
        {
            new("q", "quit", () => Task.CompletedTask, _ => false),
            new("tab", "next panel", () => Task.CompletedTask, _ => false),
            new("shift+tab", "prev panel", () => Task.CompletedTask, _ => false),
            new("1-3", "switch panel", () => Task.CompletedTask, _ => false),
            new("b", "build project", () => Task.CompletedTask, _ => false),
            new("B", "build solution", () => Task.CompletedTask, _ => false),
            new("ctrl+r", "reload", () => Task.CompletedTask, _ => false),
            new("?", "keybindings", () => Task.CompletedTask, _ => false)
        };

        var allBindings = localBindings.Concat(globalBindings).ToList();
        var maxLabelWidth = allBindings.Select(b => b.Label.Length).DefaultIfEmpty(0).Max();
        var maxDescWidth = allBindings.Select(b => b.Description.Length).DefaultIfEmpty(0).Max();

        var maxHeaderWidth = Math.Max(panelName.Length, "Global".Length) + 8;
        maxDescWidth = Math.Max(maxDescWidth, maxHeaderWidth);
        maxDescWidth = Math.Max(maxDescWidth, 40);

        var grid = new Grid
        {
            Expand = false
        };
        grid.AddColumn(new GridColumn().Width(maxLabelWidth).Padding(0, 0, 4, 0).NoWrap().RightAligned());
        grid.AddColumn(new GridColumn().Width(maxDescWidth).NoWrap());

        AddSection(panelName, localBindings);
        AddSection("Global", globalBindings);

        _activeModal = new Modal("Keybindings", grid, () => _activeModal = null);
        _needsRefresh = true;
        return;

        void AddSection(string title, IEnumerable<KeyBinding> bindings)
        {
            grid.AddRow(Text.Empty, new Markup($"[bold yellow]--- {Markup.Escape(title)} ---[/]"));
            grid.AddRow(Text.Empty, Text.Empty);

            foreach (var b in bindings)
            {
                grid.AddRow(new Markup($"[blue]{Markup.Escape(b.Label)}[/]"), new Markup(Markup.Escape(b.Description)));
            }

            grid.AddRow(Text.Empty, Text.Empty);
        }
    }

    public async Task<IScreen?> HandleInputAsync(ConsoleKeyInfo key, AppLayout layout)
    {
        _needsRefresh = true;

        if (_activeModal != null && await _activeModal.HandleInputAsync(key))
        {
            _needsRefresh = true;
            return this;
        }

        if (key.Key == ConsoleKey.Q)
        {
            return null;
        }

        var binding = GetKeyBindings().FirstOrDefault(b => b.Match(key));
        if (binding == null)
        {
            return this;
        }

        switch (binding.Label)
        {
            case "1-3":
            {
                if (key.Key == ConsoleKey.D1) layout.SetActivePanel(0);
                else if (key.Key == ConsoleKey.D2) layout.SetActivePanel(1);
                else if (key.Key == ConsoleKey.D3) layout.SetActivePanel(2);
                return this;
            }
            case "tab":
            {
                var next = (layout.ActivePanel + 1) % 3;
                layout.SetActivePanel(next);
                return this;
            }
            case "shift+tab":
            {
                var next = (layout.ActivePanel - 1 + 3) % 3;
                layout.SetActivePanel(next);
                return this;
            }
            case "q":
                return null;
            default:
                await binding.Action();

                return this;
        }
    }

    private async Task HandleReloadAsync()
    {
        var project = _explorer.GetSelectedProject();
        if (project == null) return;

        var isSolution = project.Path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase);

        if (isSolution)
        {
            _layout.AddLog("[blue]Reloading solution...[/]");
            try
            {
                var solution = await _solutionService.FindAndParseSolutionAsync(project.Path);
                if (solution != null)
                {
                    _explorer.SetSolution(solution);
                    _needsRefresh = true;
                    _layout.AddLog("[green]Solution reloaded.[/]");
                }
            }
            catch (Exception ex)
            {
                _layout.AddLog($"[red]Error reloading solution: {ex.Message}[/]");
            }
        }
        else
        {
            _layout.AddLog($"[blue]Reloading project {Markup.Escape(project.Name)}...[/]");
            await _detailsPane.ReloadCurrentTabDataAsync();
            _layout.AddLog($"[green]Project {Markup.Escape(project.Name)} reloaded.[/]");
        }
    }

    private Task HandleBuildAsync(AppLayout layout, bool buildSolution = false)
    {
        string? targetPath;
        string? targetName;

        if (buildSolution)
        {
            targetPath = _solutionService.CurrentSolution?.Path;
            targetName = _solutionService.CurrentSolution?.Name;
        }
        else
        {
            var project = _explorer.GetSelectedProject();
            targetPath = project?.Path;
            targetName = project?.Name;
        }

        if (targetPath == null)
        {
            layout.AddLog("[yellow]Cannot build: No target (project or solution) selected.[/]");
            return Task.CompletedTask;
        }

        layout.AddLog($"[blue]Starting build for {Markup.Escape(targetName ?? "Unknown")}...[/]");
        _ = Task.Run(async () =>
        {
            try
            {
                if (_buildCts != null)
                {
                    await _buildCts.CancelAsync();
                    _buildCts.Dispose();
                }

                _buildCts = new CancellationTokenSource();
                var result = await CommandService.BuildProjectAsync(targetPath,
                    msg => { layout.AddLog(Markup.Escape(msg)); }, _buildCts.Token);

                layout.AddLog(result.ExitCode == 0
                    ? $"[green]Build Succeeded: {Markup.Escape(targetName ?? "Unknown")}[/]"
                    : $"[red]Build Failed: {Markup.Escape(targetName ?? "Unknown")}[/]");

                _needsRefresh = true;
            }
            catch (Exception ex)
            {
                layout.AddLog($"[red]Build Error: {ex.Message}[/]");
                _needsRefresh = true;
            }
        });
        return Task.CompletedTask;
    }

    public void Render(AppLayout layout, int width, int height)
    {
        layout.SetDetailsActiveTab(_detailsPane.ActiveTab);

        if (_activeModal != null)
        {
            layout.UpdateModal(_activeModal.GetRenderable(width, height));
        }
        else
        {
            layout.UpdateModal(null);

            var bottomH = AppLayout.GetBottomHeight(height);
            var mainHeight = height - 1;
            var topH = mainHeight - bottomH;

            var contentTopH = Math.Max(1, topH - 2);

            var w = width / 3;
            var dw = width * 6 / 10;

            layout.UpdateLeft(_explorer.GetContent(contentTopH, w - 2, layout.ActivePanel == 0));
            layout.UpdateRight(_detailsPane.GetContent(contentTopH, dw - 2, layout.ActivePanel == 1));
        }
    }
}