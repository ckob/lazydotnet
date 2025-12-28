using Spectre.Console;
using lazydotnet.Core;
using lazydotnet.UI;
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

    public DashboardScreen(SolutionExplorer explorer, ProjectDetailsPane detailsPane, AppLayout layout, CommandService commandService)
    {
        _explorer = explorer;
        _detailsPane = detailsPane;
        _layout = layout;
        _commandService = commandService;
        
        _detailsPane.LogAction = msg => _layout.AddLog(msg);
        _detailsPane.RequestRefresh = () => _needsRefresh = true;
    }

    public void OnEnter()
    {
        _needsRefresh = true;
    }

    public bool OnTick()
    {
        var currentProject = _explorer.GetSelectedProject();
        var currentPath = currentProject?.Path;

        if (currentPath != _lastSelectedProjectPath)
        {
            _lastSelectedProjectPath = currentPath;
            _needsRefresh = true;

            int h = Math.Max(5, _lastHeight - 15);
            int dw = _lastWidth * 6 / 10;

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

        bool result = _needsRefresh;
        _needsRefresh = false;
        return result;
    }

    public async Task<IScreen?> HandleInputAsync(ConsoleKeyInfo key, AppLayout layout)
    {
        _needsRefresh = true;

        // 1. Handle Global Keys first
        switch (key.Key)
        {
            case ConsoleKey.Q:
                return null;
            case ConsoleKey.D1:
                layout.SetActivePanel(0);
                return this;
            case ConsoleKey.D2:
                layout.SetActivePanel(1);
                return this;
            case ConsoleKey.D3:
                layout.SetActivePanel(2);
                return this;
            case ConsoleKey.B:
                await HandleBuildAsync(layout);
                return this;
        }

        // 2. Delegate to active panel
        switch (layout.ActivePanel)
        {
            case 0:
                _explorer.HandleInput(key);
                break;
            case 1:
                await _detailsPane.HandleInputAsync(key, layout);
                break;
            case 2:
                layout.LogViewer.HandleInput(key);
                break;
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

        int h = Math.Max(5, height - 15);
        int w = width / 3;
        int dw = width * 6 / 10;

        layout.UpdateLeft(_explorer.GetContent(h, w));
        layout.UpdateRight(_detailsPane.GetContent(h, dw));
    }
}
