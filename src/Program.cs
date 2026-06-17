using Microsoft.Build.Locator;
using lazydotnet.Commands;
using lazydotnet.Services;
using lazydotnet.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (!MSBuildLocator.IsRegistered)
{
    try
    {
        MSBuildLocator.RegisterDefaults();
    }
    catch
    {
        var manualPath = DotnetSdkResolver.GetLatestSdkPath();

        if (!string.IsNullOrEmpty(manualPath))
        {
            MSBuildLocator.RegisterMSBuildPath(manualPath);
        }
    }

    if (!MSBuildLocator.IsRegistered)
    {
        AnsiConsole.MarkupLine("[red]Fatal Error:[/] Could not locate a .NET SDK.");
        AnsiConsole.MarkupLine("Please ensure [yellow]dotnet[/] is installed and available in your PATH.");
        return 1;
    }
}

// Configuration
var globalConfigDir = lazydotnet.Core.EnvironmentPaths.GetConfigDirectory();
var localConfigDir = Path.Combine(Directory.GetCurrentDirectory(), ".lazydotnet");

var config = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(globalConfigDir, "settings.json"), optional: true, reloadOnChange: false)
    .AddJsonFile(Path.Combine(localConfigDir, "settings.json"), optional: true, reloadOnChange: false)
    .AddLazydotnetEnvironmentVariables()
    .Build();

// Services
var services = new ServiceCollection();
services.Configure<LazydotnetSettings>(config);

var registrar = new lazydotnet.Core.DependencyInjection.TypeRegistrar(services);
var app = new CommandApp<DefaultCommand>(registrar);

app.Configure(configurator =>
{
    configurator.AddCommand<ConfigCommand>("config")
        .WithDescription("Manage lazydotnet configuration");
});

return await app.RunAsync(args);
