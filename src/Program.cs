using Microsoft.Build.Locator;
using lazydotnet.Commands;
using Spectre.Console.Cli;

Console.OutputEncoding = System.Text.Encoding.UTF8;
MSBuildLocator.RegisterDefaults();

var app = new CommandApp<DefaultCommand>();
return await app.RunAsync(args);
