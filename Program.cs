using Microsoft.Extensions.DependencyInjection;
using lazydotnet.Core;
using lazydotnet.UI;
using lazydotnet.Services;
using lazydotnet.Screens;

var services = new ServiceCollection();

services.AddSingleton<EasyDotnetService>();
services.AddSingleton<SolutionService>();
services.AddSingleton<TestService>();
services.AddSingleton<NuGetService>();
services.AddSingleton<IEditorService, EditorService>();
services.AddSingleton<ProjectDetailsPane>();
services.AddSingleton<AppLayout>();

var serviceProvider = services.BuildServiceProvider();

var easyDotnetService = serviceProvider.GetRequiredService<EasyDotnetService>();
var solutionService = serviceProvider.GetRequiredService<SolutionService>();

var rootDir = Directory.GetCurrentDirectory();
string? solutionFile = null;

var i = 0;
while (i < args.Length)
{
    if ((args[i] == "-s" || args[i] == "--solution") && i + 1 < args.Length)
    {
        var path = args[i + 1];
        if (File.Exists(path) && path.EndsWith(".sln"))
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

easyDotnetService.InitializeContext(rootDir, solutionFile);

var explorer = new SolutionExplorer(serviceProvider.GetRequiredService<IEditorService>());
var detailsPane = serviceProvider.GetRequiredService<ProjectDetailsPane>();
var layout = serviceProvider.GetRequiredService<AppLayout>();

AppCli.OnLog += layout.AddLog;
easyDotnetService.OnServerOutput += layout.AddEasyDotnetLog;

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
