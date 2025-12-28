using Spectre.Console;
using lazydotnet.Core;
using lazydotnet.UI;
using lazydotnet.Services;
using lazydotnet.Screens;

var solution = await SolutionService.FindAndParseSolutionAsync(Directory.GetCurrentDirectory());

if (solution == null)
{
    AnsiConsole.MarkupLine("[red]No .sln file found in the current directory.[/]");
    return;
}

var explorer = new SolutionExplorer(solution);
var detailsPane = new ProjectDetailsPane();
var layout = new AppLayout();

// Wire up services
AppCli.OnLog += layout.AddLog;

var dashboard = new DashboardScreen(explorer, detailsPane, layout);
var host = new AppHost(layout, dashboard);

await host.RunAsync();
