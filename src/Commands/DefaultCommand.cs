using System.ComponentModel;
using lazydotnet.Core;
using lazydotnet.Screens;
using lazydotnet.Services;
using lazydotnet.UI;
using Spectre.Console;
using Spectre.Console.Cli;

namespace lazydotnet.Commands;

public sealed class DefaultSettings : CommandSettings
{
    [CommandArgument(0, "[PATH]")]
    [Description("The path to the solution, project or directory to open.")]
    public string? Path { get; init; }

    [CommandOption("-p|--project <FILE>")]
    [Description("The project file (.csproj) to open.")]
    public string? Project { get; init; }

    [CommandOption("-s|--solution <FILE>")]
    [Description("The solution file (.sln, .slnx, .slnf) to open.")]
    public string? Solution { get; init; }

    public override ValidationResult Validate()
    {
        if (!string.IsNullOrEmpty(Path))
        {
            var result = ValidateInput(Path, "path", allowDirectory: true);
            if (!result.Successful) return result;
        }

        if (!string.IsNullOrEmpty(Project))
        {
            var result = ValidateInput(Project, "project file", allowDirectory: false);
            if (!result.Successful) return result;
        }

        if (!string.IsNullOrEmpty(Solution))
        {
            var result = ValidateInput(Solution, "solution file", allowDirectory: false);
            if (!result.Successful) return result;
        }

        return ValidationResult.Success();
    }

    private static ValidationResult ValidateInput(string path, string label, bool allowDirectory)
    {
        if (allowDirectory && Directory.Exists(path)) return ValidationResult.Success();

        if (!File.Exists(path))
        {
            return ValidationResult.Error($"The {label} '{path}' does not exist.");
        }

        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".sln" or ".slnx" or ".slnf" or ".csproj"))
        {
            return ValidationResult.Error($"The file '{path}' is not a valid solution or project file.");
        }

        return ValidationResult.Success();
    }
}

public sealed class DefaultCommand : AsyncCommand<DefaultSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DefaultSettings settings, CancellationToken cancellationToken)
    {
        var rootDir = Directory.GetCurrentDirectory();
        string? solutionFile = null;

        var inputPath = settings.Solution ?? settings.Project;

        if (!string.IsNullOrEmpty(inputPath))
        {
            solutionFile = Path.GetFullPath(inputPath);
            rootDir = Path.GetDirectoryName(solutionFile) ?? rootDir;
        }
        else if (!string.IsNullOrEmpty(settings.Path))
        {
            if (File.Exists(settings.Path))
            {
                solutionFile = Path.GetFullPath(settings.Path);
                rootDir = Path.GetDirectoryName(solutionFile) ?? rootDir;
            }
            else if (Directory.Exists(settings.Path))
            {
                rootDir = Path.GetFullPath(settings.Path);
            }
        }

        var editorService = new EditorService { RootPath = rootDir };
        var solutionService = new SolutionService();
        var detailsPane = new ProjectDetailsPane(solutionService, editorService);
        var layout = new AppLayout();

        var explorer = new SolutionExplorer(editorService);
        AppCli.OnLog += layout.AddLog;

        var dashboard = new DashboardScreen(explorer, detailsPane, layout, solutionService, rootDir, solutionFile);
        explorer.OnSearchRequested += () => dashboard.StartSearch();
        detailsPane.OnSearchRequested += () => dashboard.StartSearch();
        layout.SetLogViewerSearchCallback(() => dashboard.StartSearch());
        var host = new AppHost(layout, dashboard);

        try
        {
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return 1;
        }

        return 0;
    }
}
