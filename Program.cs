using Spectre.Console;
using lazydotnet.Services;
using lazydotnet.UI;



var solutionService = new SolutionService();
var commandService = new CommandService();
var nugetService = new NuGetService();
var testService = new TestService();


var solution = await solutionService.FindAndParseSolutionAsync(Directory.GetCurrentDirectory());

if (solution == null)
{
    AnsiConsole.MarkupLine("[red]No .sln file found in the current directory.[/]");
    return;
}


var explorer = new SolutionExplorer(solution);
var detailsPane = new ProjectDetailsPane(nugetService, solutionService, testService);
var layout = new AppLayout();
bool isRunning = true;
CancellationTokenSource? buildCts = null;
CancellationTokenSource? debounceCts = null;
string? lastSelectedProjectPath = null;


    // Lock for UI synchronization
    object uiLock = new();

    Console.CancelKeyPress += (sender, e) =>  
{
    e.Cancel = true;
    isRunning = false;
};


int initialH = Math.Max(5, Console.WindowHeight - 15);
int initialW = Console.WindowWidth / 3;
int detailsW = Console.WindowWidth * 6 / 10;
layout.UpdateLeft(explorer.GetContent(initialH, initialW));
layout.UpdateRight(detailsPane.GetContent(initialH, detailsW));
layout.UpdateBottom();

detailsPane.LogAction = msg => layout.AddLog(msg);
AppCli.OnLog += msg => layout.AddLog(msg);

AnsiConsole.AlternateScreen(() =>
{

    AnsiConsole.Live(layout.GetRoot())
        .StartAsync(async ctx =>
        {
             layout.OnLog += () => 
             {
                 lock (uiLock)
                 {
                     layout.UpdateBottom();
                     // We can refresh here, safely
                     ctx.Refresh();
                 }
             };

            int lastWidth = Console.WindowWidth;
            int lastHeight = Console.WindowHeight;

            detailsPane.RequestRefresh = () => 
            {
                lock (uiLock)
                {
                    int h = Math.Max(5, lastHeight - 15);
                    int dw = lastWidth * 6 / 10;
                    layout.UpdateRight(detailsPane.GetContent(h, dw));
                    ctx.Refresh();
                }
            };

            while (isRunning)
            {
                try
                {
                    int h = Math.Max(5, lastHeight - 15);
                    int w = lastWidth / 3;
                    int dw = lastWidth * 6 / 10;

                    if (Console.WindowWidth != lastWidth || Console.WindowHeight != lastHeight)
                    {
                        lock (uiLock)
                        {
                            lastWidth = Console.WindowWidth;
                            lastHeight = Console.WindowHeight;
                            
                            h = Math.Max(5, lastHeight - 15);
                            w = lastWidth / 3;
                            dw = lastWidth * 6 / 10;
                            layout.UpdateLeft(explorer.GetContent(h, w));
                            layout.UpdateRight(detailsPane.GetContent(h, dw));
                            ctx.Refresh();
                        }
                    }

                    var currentProject = explorer.GetSelectedProject();
                    var currentPath = currentProject?.Path;
                    
                    if (currentPath != lastSelectedProjectPath)
                    {
                        lastSelectedProjectPath = currentPath;
                        
                        if (currentPath != null)
                        {
                            detailsPane.ClearData();
                            layout.UpdateRight(detailsPane.GetContent(h, dw));
                            ctx.Refresh();
                            
                            debounceCts?.Cancel();
                            debounceCts = new CancellationTokenSource();
                            var token = debounceCts.Token;

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await Task.Delay(500, token);
                                    if (token.IsCancellationRequested) return;

                                    await detailsPane.LoadProjectDataAsync(currentPath, currentProject!.Name);
                                    lock (uiLock) 
                                    {
                                        if (!token.IsCancellationRequested)
                                        {
                                            layout.UpdateRight(detailsPane.GetContent(h, dw));
                                            ctx.Refresh();
                                        }
                                    }
                                }
                                catch (OperationCanceledException) { }
                            }, token);
                        }
                        else
                        {
                            detailsPane.ClearForNonProject();
                            layout.UpdateRight(detailsPane.GetContent(h, dw));
                            ctx.Refresh();
                        }
                    }


                    bool dirty = false;
                    while (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        dirty = true;
                        
                        h = Math.Max(5, lastHeight - 15);
                        w = lastWidth / 3;
                        dw = lastWidth * 6 / 10;

                        if (layout.ActivePanel == 1)
                        {
                            if (await detailsPane.HandleKey(key))
                            {
                                layout.UpdateRight(detailsPane.GetContent(h, dw));
                                continue;
                            }
                        }


                        if (key.KeyChar == '[' && layout.ActivePanel == 1)
                        {
                            detailsPane.PreviousTab();
                            layout.SetDetailsActiveTab(detailsPane.ActiveTab);
                            layout.UpdateRight(detailsPane.GetContent(h, dw));
                            continue;
                        }
                        if (key.KeyChar == ']' && layout.ActivePanel == 1)
                        {
                            detailsPane.NextTab();
                            layout.SetDetailsActiveTab(detailsPane.ActiveTab);
                            layout.UpdateRight(detailsPane.GetContent(h, dw));
                            continue;
                        }

                        switch (key.Key)
                        {
                            case ConsoleKey.Q:
                                isRunning = false;
                                break;
                            case ConsoleKey.D1:
                                layout.SetActivePanel(0);
                                layout.UpdateLeft(explorer.GetContent(h, w));
                                layout.UpdateRight(detailsPane.GetContent(h, dw));
                                break;
                            case ConsoleKey.D2:
                                layout.SetActivePanel(1);
                                layout.UpdateLeft(explorer.GetContent(h, w));
                                layout.UpdateRight(detailsPane.GetContent(h, dw));
                                break;
                            case ConsoleKey.D3:
                                layout.SetActivePanel(2);
                                layout.UpdateLeft(explorer.GetContent(h, w));
                                layout.UpdateRight(detailsPane.GetContent(h, dw));
                                break;
                            case ConsoleKey.RightArrow:
                                if (layout.ActivePanel == 0)
                                {
                                    explorer.Expand();
                                    layout.UpdateLeft(explorer.GetContent(h, w));
                                }
                                break;
                            case ConsoleKey.Enter:
                            case ConsoleKey.Spacebar:
                                if (layout.ActivePanel == 0)
                                {
                                    explorer.ToggleExpand();
                                    layout.UpdateLeft(explorer.GetContent(h, w));
                                }
                                break;
                            case ConsoleKey.LeftArrow:
                                if (layout.ActivePanel == 0)
                                {
                                    explorer.Collapse();
                                    layout.UpdateLeft(explorer.GetContent(h, w));
                                }
                                break;
                            case ConsoleKey.UpArrow:
                            case ConsoleKey.K:
                                if (layout.ActivePanel == 0)
                                {
                                    explorer.MoveUp();
                                    layout.UpdateLeft(explorer.GetContent(h, w));
                                }
                                else if (layout.ActivePanel == 1)
                                {
                                    detailsPane.MoveUp();
                                    layout.UpdateRight(detailsPane.GetContent(h, dw));
                                }
                                else if (layout.ActivePanel == 2)
                                {
                                    layout.LogViewer.MoveUp();
                                }
                                break;
                            case ConsoleKey.PageUp:
                                if (layout.ActivePanel == 2) layout.LogViewer.PageUp(10);
                                break;
                            case ConsoleKey.DownArrow:
                            case ConsoleKey.J:
                                if (layout.ActivePanel == 0)
                                {
                                    explorer.MoveDown();
                                    layout.UpdateLeft(explorer.GetContent(h, w));
                                }
                                else if (layout.ActivePanel == 1)
                                {
                                    detailsPane.MoveDown();
                                    layout.UpdateRight(detailsPane.GetContent(h, dw));
                                }
                                else if (layout.ActivePanel == 2)
                                {
                                    layout.LogViewer.MoveDown();
                                }
                                break;
                            case ConsoleKey.PageDown:
                                if (layout.ActivePanel == 2) layout.LogViewer.PageDown(10);
                                break;
                            case ConsoleKey.B:
                                var project = explorer.GetSelectedProject();
                                if (project == null) 
                                {
                                    layout.AddLog("[yellow]Cannot build this item (not a project or solution).[/]");
                                    layout.UpdateBottom();
                                    continue;
                                }

                                layout.AddLog($"[blue]Starting build for {project.Name}...[/]");
                                layout.UpdateBottom();
                                ctx.Refresh();

                                _ = Task.Run(async () => 
                                {
                                    try 
                                    {
                                        var cts = new CancellationTokenSource();
                                        buildCts = cts;
                                         
                                        var result = await commandService.BuildProjectAsync(project.Path, msg => 
                                        {
                                            layout.AddLog(Markup.Escape(msg));
                                        }, cts.Token);

                                        if (result.ExitCode == 0)
                                            layout.AddLog($"[green]Build Succeeded: {Markup.Escape(project.Name)}[/]");
                                        else
                                            layout.AddLog($"[red]Build Failed: {Markup.Escape(project.Name)}[/]");
                                    }
                                    catch (Exception ex)
                                    {
                                        layout.AddLog($"[red]Build Error: {ex.Message}[/]");
                                    }
                                });
                                break;
                        }
                    }

                    lock (uiLock)
                    {
                        if (dirty) ctx.Refresh();
                        layout.UpdateRight(detailsPane.GetContent(h, dw));
                        layout.UpdateBottom();
                        ctx.Refresh();
                    }
                }
                catch (Exception ex)
                {
                    layout.AddLog($"[red]CRITICAL ERROR: {ex.Message}[/]");
                    layout.UpdateBottom();
                    ctx.Refresh();
                    await Task.Delay(1000); 
                }
            }
        })
        .GetAwaiter().GetResult();
});