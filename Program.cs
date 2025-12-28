using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using lazydotnet.Core;
using lazydotnet.UI;
using lazydotnet.Services;
using lazydotnet.Screens;

var services = new ServiceCollection();

// Register services
services.AddSingleton<EasyDotnetService>();
services.AddSingleton<SolutionService>();
services.AddSingleton<CommandService>();
services.AddSingleton<TestService>();
services.AddSingleton<NuGetService>();
services.AddSingleton<ProjectDetailsPane>();
services.AddSingleton<AppLayout>();

var serviceProvider = services.BuildServiceProvider();

var easyDotnetService = serviceProvider.GetRequiredService<EasyDotnetService>();
var solutionService = serviceProvider.GetRequiredService<SolutionService>();

var targetPath = Directory.GetCurrentDirectory();

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "-s" || args[i] == "--solution")
    {
        if (i + 1 < args.Length)
        {
            targetPath = args[i + 1];
            i++;
        }
    }
}

var solution = await solutionService.FindAndParseSolutionAsync(targetPath);

if (solution == null)
{
    AnsiConsole.MarkupLine($"[red]No .sln file found at: {targetPath}[/]");
    return;
}

var explorer = new SolutionExplorer(solution);
var detailsPane = serviceProvider.GetRequiredService<ProjectDetailsPane>();
var layout = serviceProvider.GetRequiredService<AppLayout>();
var commandService = serviceProvider.GetRequiredService<CommandService>();

// Wire up services
AppCli.OnLog += layout.AddLog;

var dashboard = new DashboardScreen(explorer, detailsPane, layout, commandService);
var host = new AppHost(layout, dashboard);

try
{
    await host.RunAsync();
}
finally
{
    if (serviceProvider is IAsyncDisposable asyncDisposable)
    {
        await asyncDisposable.DisposeAsync();
    }
    else
    {
        serviceProvider.Dispose();
    }
}
