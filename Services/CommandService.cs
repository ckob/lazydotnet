using CliWrap;
using Spectre.Console;

namespace lazydotnet.Services;

public class CommandService
{
    public static async Task<CommandResult> BuildProjectAsync(string projectPath, Action<string> onOutput, CancellationToken ct)
    {
        var cmd = Cli.Wrap("dotnet")
            .WithArguments($"build \"{projectPath}\" /v:n /nologo")
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(onOutput))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(err => onOutput($"[red]{Markup.Escape(err)}[/]")));

        var result = await AppCli.RunAsync(cmd, ct);

        return new CommandResult(result.ExitCode, result.StartTime, result.ExitTime);
    }
}
