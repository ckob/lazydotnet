using Spectre.Console;
using Spectre.Console.Cli;
using NJsonSchema;
using lazydotnet.Core.Configuration;

namespace lazydotnet.Commands;

public class ConfigCommand : AsyncCommand<ConfigCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--generate-schema")]
        public bool GenerateSchema { get; init; }

        [CommandOption("--init-local")]
        public bool InitLocal { get; init; }

        [CommandOption("--init-global")]
        public bool InitGlobal { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.GenerateSchema)
        {
            var schema = JsonSchema.FromType<LazydotnetSettings>();
            schema.Properties.Add("$schema", new JsonSchemaProperty
            {
                Type = JsonObjectType.String,
                Description = "The JSON schema reference."
            });

            var json = schema.ToJson();
            Console.WriteLine(json);
            return 0;
        }

        if (settings.InitLocal)
        {
            await InitConfigAsync(Path.Combine(Directory.GetCurrentDirectory(), ".lazydotnet"));
            return 0;
        }

        if (settings.InitGlobal)
        {
            await InitConfigAsync(Core.EnvironmentPaths.GetConfigDirectory());
            return 0;
        }

        AnsiConsole.MarkupLine("Please specify an action. Options: [yellow]--init-local[/], [yellow]--init-global[/], [yellow]--generate-schema[/].");
        return 1;
    }

    private static async Task InitConfigAsync(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var settingsPath = Path.Combine(directory, "settings.json");

        if (!File.Exists(settingsPath))
        {
            const string defaultSettings = """
{
  "$schema": "https://raw.githubusercontent.com/ckob/lazydotnet/main/docs/settings.schema.json"
}
""";
            await File.WriteAllTextAsync(settingsPath, defaultSettings);
            AnsiConsole.MarkupLine($"[green]Created[/] new configuration at {settingsPath}");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]Configuration already exists[/] at {settingsPath}");
        }
    }
}
