using Spectre.Console;
using Spectre.Console.Rendering;
using lazydotnet.Core;
using lazydotnet.UI;
using lazydotnet.UI.Components;
using lazydotnet.Services;

namespace lazydotnet.Screens;

public class DashboardScreen : IScreen
{
    private readonly SolutionExplorer _explorer;
    private readonly ProjectDetailsPane _detailsPane;
    private readonly CommandService _commandService;
    private string? _lastSelectedProjectPath;
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _buildCts;
    private int _lastWidth;
    private int _lastHeight;
    private bool _needsRefresh;

    private readonly AppLayout _layout;
    private readonly SolutionService _solutionService;
    private readonly string _rootDir;
    private readonly string? _solutionFile;

    public DashboardScreen(
        SolutionExplorer explorer, 
        ProjectDetailsPane detailsPane, 
        AppLayout layout, 
        CommandService commandService,
        SolutionService solutionService,
        string rootDir,
        string? solutionFile)
    {
        _explorer = explorer;
        _detailsPane = detailsPane;
        _layout = layout;
        _commandService = commandService;
        _solutionService = solutionService;
        _rootDir = rootDir;
        _solutionFile = solutionFile;
        
        _detailsPane.LogAction = msg => _layout.AddLog(msg);
        _detailsPane.RequestRefresh = () => _needsRefresh = true;
    }

    public void OnEnter()
    {
        _needsRefresh = true;

        // Start loading solution in the background
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
        var currentProject = _explorer.GetSelectedProject();
        var currentPath = currentProject?.Path;

        if (currentPath != _lastSelectedProjectPath)
        {
            _lastSelectedProjectPath = currentPath;
            _needsRefresh = true;

            if (currentPath != null)
            {
                _detailsPane.ClearData();
                
                _debounceCts?.Cancel();
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
                    catch (OperationCanceledException) { }
                }, token);
            }
            else
            {
                _detailsPane.ClearForNonProject();
            }
        }

        // Synchronize test output if the test tab is active
        if (_layout.ActivePanel == 1 && _detailsPane.ActiveTab == 2)
        {
            var selectedTest = _detailsPane.GetSelectedTestNode();
            if (selectedTest != null)
            {
                _layout.TestOutputViewer.SetOutput(selectedTest.GetOutputSnapshot());
            }
        }

        bool result = _needsRefresh;
        _needsRefresh = false;
        return result;
    }

    private Modal? _activeModal;

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        if (_activeModal != null)
        {
            yield return new KeyBinding("q", "quit", () => Task.FromResult<IScreen?>(null), k => k.Key == ConsoleKey.Q);
            foreach (var b in _activeModal.GetKeyBindings())
            {
                yield return b;
            }
            yield break;
        }

        yield return new KeyBinding("q", "quit", () => Task.FromResult<IScreen?>(null), k => k.Key == ConsoleKey.Q);
        yield return new KeyBinding("1-3", "switch panel", () => Task.CompletedTask, k => k.Key == ConsoleKey.D1 || k.Key == ConsoleKey.D2 || k.Key == ConsoleKey.D3);
        yield return new KeyBinding("b", "build", () => HandleBuildAsync(_layout), k => k.Key == ConsoleKey.B);
        yield return new KeyBinding("?", "help", () => { ShowHelpModal(); return Task.CompletedTask; }, k => k.KeyChar == '?');

        switch (_layout.ActivePanel)
        {
            case 0:
                foreach (var b in _explorer.GetKeyBindings())
                {
                    yield return b;
                }
                break;
            case 1:
                foreach (var b in _detailsPane.GetKeyBindings())
                {
                    yield return b;
                }
                break;
            case 2:
                IEnumerable<KeyBinding>? subBindings = _layout.BottomActiveTab switch
                {
                    0 => _layout.LogViewer.GetKeyBindings(),
                    1 => _layout.TestOutputViewer.GetKeyBindings(),
                    2 => _layout.EasyDotnetOutputViewer.GetKeyBindings(),
                    _ => null
                };

                if (subBindings != null)
                {
                    yield return new KeyBinding("[", "prev tab", () =>
                    {
                        _layout.PreviousBottomTab();
                        return Task.CompletedTask;
                    }, k => k.KeyChar == '[');

                    yield return new KeyBinding("]", "next tab", () =>
                    {
                        _layout.NextBottomTab();
                        return Task.CompletedTask;
                    }, k => k.KeyChar == ']');

                    foreach (var b in subBindings)
                    {
                        yield return b;
                    }
                }
                break;
        }
    }

    private void ShowHelpModal()
    {
        string panelName = _layout.ActivePanel switch
        {
            0 => "Explorer",
            1 => "Details",
            2 => _layout.BottomActiveTab switch
            {
                0 => "Log",
                1 => "Test Output",
                2 => "EasyDotnet Output",
                _ => "Bottom"
            },
            _ => "Local"
        };

        var localBindings = (_layout.ActivePanel switch
        {
            0 => _explorer.GetKeyBindings(),
            1 => _detailsPane.GetKeyBindings(),
            2 => _layout.BottomActiveTab switch
            {
                0 => _layout.LogViewer.GetKeyBindings(),
                1 => _layout.TestOutputViewer.GetKeyBindings(),
                2 => _layout.EasyDotnetOutputViewer.GetKeyBindings(),
                _ => Enumerable.Empty<KeyBinding>()
            },
            _ => Enumerable.Empty<KeyBinding>()
        }).ToList();

        var globalBindings = new List<KeyBinding>
        {
            new KeyBinding("q", "quit", () => Task.CompletedTask, _ => false),
            new KeyBinding("1-3", "switch panel", () => Task.CompletedTask, _ => false),
            new KeyBinding("b", "build", () => Task.CompletedTask, _ => false),
            new KeyBinding("?", "help", () => Task.CompletedTask, _ => false)
        };

        var allBindings = localBindings.Concat(globalBindings).ToList();
        int maxLabelWidth = allBindings.Select(b => b.Label.Length).DefaultIfEmpty(0).Max();
        int maxDescWidth = allBindings.Select(b => b.Description.Length).DefaultIfEmpty(0).Max();
        
        // Ensure headers also fit in the description column if needed
        int maxHeaderWidth = Math.Max(panelName.Length, "Global".Length) + 8; // --- Title ---
        maxDescWidth = Math.Max(maxDescWidth, maxHeaderWidth);
        maxDescWidth = Math.Max(maxDescWidth, 40); // Give it some space even if descriptions are short
        
        var grid = new Grid();
        grid.Expand = false;
        grid.AddColumn(new GridColumn().Width(maxLabelWidth).Padding(0, 0, 4, 0).NoWrap().RightAligned());
        grid.AddColumn(new GridColumn().Width(maxDescWidth).NoWrap());

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

        AddSection(panelName, localBindings);
        AddSection("Global", globalBindings);

        _activeModal = new Modal("Keybindings", grid, () => _activeModal = null);
        _needsRefresh = true;
    }

    public async Task<IScreen?> HandleInputAsync(ConsoleKeyInfo key, AppLayout layout)
    {
        _needsRefresh = true;

        if (key.Key == ConsoleKey.Q)
        {
            return null;
        }

        if (_activeModal != null)
        {
            var modalBinding = _activeModal.GetKeyBindings().FirstOrDefault(b => b.Match(key));
            if (modalBinding != null)
            {
                await modalBinding.Action();
                _needsRefresh = true;
            }
            return this;
        }

        var binding = GetKeyBindings().FirstOrDefault(b => b.Match(key));
        if (binding != null)
        {
            if (binding.Label == "1-3")
            {
                if (key.Key == ConsoleKey.D1) layout.SetActivePanel(0);
                else if (key.Key == ConsoleKey.D2) layout.SetActivePanel(1);
                else if (key.Key == ConsoleKey.D3) layout.SetActivePanel(2);
                return this;
            }
            
            if (binding.Label == "q") return null;

            await binding.Action();
            return this;
        }

        return this;
    }

    private async Task HandleBuildAsync(AppLayout layout)
    {
        var project = _explorer.GetSelectedProject();
        if (project == null)
        {
            layout.AddLog("[yellow]Cannot build this item (not a project or solution).[/]");
            return;
        }

        layout.AddLog($"[blue]Starting build for {project.Name}...[/]");
        _ = Task.Run(async () =>
        {
            try
            {
                if (_buildCts != null)
                {
                    await _buildCts.CancelAsync();
                }
                _buildCts = new CancellationTokenSource();
                var result = await _commandService.BuildProjectAsync(project.Path, msg =>
                {
                    layout.AddLog(Markup.Escape(msg));
                }, _buildCts.Token);

                if (result.ExitCode == 0)
                    layout.AddLog($"[green]Build Succeeded: {Markup.Escape(project.Name)}[/]");
                else
                    layout.AddLog($"[red]Build Failed: {Markup.Escape(project.Name)}[/]");

                _needsRefresh = true;
            }
            catch (Exception ex)
            {
                layout.AddLog($"[red]Build Error: {ex.Message}[/]");
                _needsRefresh = true;
            }
        });
    }

    public void Render(AppLayout layout, int width, int height)
    {
        _lastWidth = width;
        _lastHeight = height;

        layout.SetDetailsActiveTab(_detailsPane.ActiveTab);

        if (_activeModal != null)
        {
            layout.UpdateModal(_activeModal.GetRenderable(width, height));
        }
        else
        {
            layout.UpdateModal(null);

            int bottomH = layout.GetBottomHeight(height);
            int topH = height - bottomH;

            // Subtract 2 for panel borders
            int contentTopH = Math.Max(1, topH - 2);

            int w = width / 3;
            int dw = width * 6 / 10;

            layout.UpdateLeft(_explorer.GetContent(contentTopH, w - 2));
            layout.UpdateRight(_detailsPane.GetContent(contentTopH, dw - 2));
        }
    }
}
