using CliWrap;
using Spectre.Console;

namespace lazydotnet.Services;

public static class CommandService
{
    private const string DotnetExecutable = "dotnet";

    public static async Task<CommandResult> BuildProjectAsync(string projectPath, Action<string> onOutput, CancellationToken ct)
    {
        var cmd = Cli.Wrap(DotnetExecutable)
            .WithArguments($"build \"{projectPath}\" /v:n /nologo")
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(s => onOutput(Markup.Escape(s))))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(err => onOutput($"[red]{Markup.Escape(err)}[/]")));

        return await AppCli.RunAsync(cmd, ct);
    }
}
