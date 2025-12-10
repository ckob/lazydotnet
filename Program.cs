using Spectre.Console;
using lazydotnet.Services;
using lazydotnet.UI;



var solutionService = new SolutionService();
var commandService = new CommandService();


var solution = await solutionService.FindAndParseSolutionAsync(Directory.GetCurrentDirectory());

if (solution == null)
{
    AnsiConsole.MarkupLine("[red]No .sln file found in the current directory.[/]");
    return;
}


var explorer = new SolutionExplorer(solution);
var layout = new AppLayout();
bool isRunning = true;
CancellationTokenSource? buildCts = null;

// Graceful exit on Ctrl+C
Console.CancelKeyPress += (sender, e) => 
{
    e.Cancel = true;
    isRunning = false;
};


int initialH = Math.Max(5, Console.WindowHeight - 15);
int initialW = Console.WindowWidth / 3;
layout.UpdateLeft(explorer.GetContent(initialH, initialW));
layout.UpdateRight(new Text("Select a project to see details..."));
layout.UpdateBottom();


AnsiConsole.AlternateScreen(() =>
{

    AnsiConsole.Live(layout.GetRoot())
        .StartAsync(async ctx =>
        {
             layout.OnLog += () => 
             {
                 layout.UpdateBottom();
                 ctx.Refresh();
             };

            int lastWidth = Console.WindowWidth;
            int lastHeight = Console.WindowHeight;

            while (isRunning)
            {
                try
                {

                    if (Console.WindowWidth != lastWidth || Console.WindowHeight != lastHeight)
                    {
                        lastWidth = Console.WindowWidth;
                        lastHeight = Console.WindowHeight;
                        
                        int h = Math.Max(5, lastHeight - 15);
                        int w = lastWidth / 3;
                        layout.UpdateLeft(explorer.GetContent(h, w));
                        ctx.Refresh();
                    }


                    bool dirty = false;
                    while (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        dirty = true;
                        
                        int h = Math.Max(5, lastHeight - 15);
                        int w = lastWidth / 3;

                        switch (key.Key)
                        {
                            case ConsoleKey.Q:
                                isRunning = false;
                                break;
                            case ConsoleKey.RightArrow:
                                explorer.Expand();

                                layout.UpdateLeft(explorer.GetContent(h, w));
                                break;
                            case ConsoleKey.Enter:
                            case ConsoleKey.Spacebar:
                                explorer.ToggleExpand();
                                layout.UpdateLeft(explorer.GetContent(h, w));
                                break;
                            case ConsoleKey.LeftArrow:
                                explorer.Collapse();
                                layout.UpdateLeft(explorer.GetContent(h, w));
                                break;
                            case ConsoleKey.UpArrow:
                            case ConsoleKey.K:
                                explorer.MoveUp();
                                layout.UpdateLeft(explorer.GetContent(h, w));
                                break;
                            case ConsoleKey.DownArrow:
                            case ConsoleKey.J:
                                explorer.MoveDown();
                                layout.UpdateLeft(explorer.GetContent(h, w));
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

                    if (dirty)
                    {
                        ctx.Refresh();
                    }


                    await Task.Delay(20);
                    layout.UpdateBottom();
                    ctx.Refresh();
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