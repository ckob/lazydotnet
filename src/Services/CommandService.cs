using CliWrap;
using Spectre.Console;
using lazydotnet.Core;

namespace lazydotnet.Services;

public static class CommandService
{
    private const string DotnetExecutable = "dotnet";

    public static async Task<CommandResult> BuildProjectAsync(string projectPath, Action<string> onOutput, CancellationToken ct)
    {
        var relativePath = PathHelper.GetRelativePath(projectPath);
        var cmd = Cli.Wrap(DotnetExecutable)
            .WithArguments($"build \"{relativePath}\" /v:n /nologo")
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(s => onOutput(Markup.Escape(s))))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(err => onOutput($"[red]{Markup.Escape(err)}[/]")));

        return await AppCli.RunAsync(cmd, ct);
    }
}
