using Microsoft.Build.Locator;
using lazydotnet.Commands;
using Spectre.Console.Cli;

MSBuildLocator.RegisterDefaults();

var app = new CommandApp<DefaultCommand>();
return await app.RunAsync(args);
