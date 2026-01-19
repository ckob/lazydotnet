using Microsoft.Extensions.DependencyInjection;
using Microsoft.Build.Locator;
using lazydotnet.Core;
using lazydotnet.UI;
using lazydotnet.Services;
using lazydotnet.Screens;

MSBuildLocator.RegisterDefaults();

var services = new ServiceCollection();

services.AddSingleton<SolutionService>();
services.AddSingleton<TestService>();
services.AddSingleton<IEditorService, EditorService>();
services.AddSingleton<ProjectDetailsPane>();
services.AddSingleton<AppLayout>();

var serviceProvider = services.BuildServiceProvider();

var solutionService = serviceProvider.GetRequiredService<SolutionService>();

var rootDir = Directory.GetCurrentDirectory();
string? solutionFile = null;

var i = 0;
while (i < args.Length)
{
    if ((args[i] == "-s" || args[i] == "--solution" || args[i] == "-p" || args[i] == "--project") && i + 1 < args.Length)
    {
        var path = args[i + 1];
        if (File.Exists(path))
        {
            solutionFile = Path.GetFullPath(path);
            rootDir = Path.GetDirectoryName(solutionFile) ?? rootDir;
        }
        else if (Directory.Exists(path))
        {
            rootDir = Path.GetFullPath(path);
        }
        i += 2;
        continue;
    }
    i++;
}

var explorer = new SolutionExplorer(serviceProvider.GetRequiredService<IEditorService>());
var detailsPane = serviceProvider.GetRequiredService<ProjectDetailsPane>();
var layout = serviceProvider.GetRequiredService<AppLayout>();

AppCli.OnLog += layout.AddLog;

var dashboard = new DashboardScreen(explorer, detailsPane, layout, solutionService, rootDir, solutionFile);
var host = new AppHost(layout, dashboard);

try
{
    await host.RunAsync();
}
finally
{
    await serviceProvider.DisposeAsync();
}
