using CliWrap;
using CliWrap.EventStream;
using Spectre.Console;

namespace lazydotnet.Services;

public class CommandService
{
    public async Task<CommandResult> BuildProjectAsync(string projectPath, Action<string> onOutput, CancellationToken ct)
    {

        var result = await Cli.Wrap("dotnet")
            .WithArguments($"build \"{projectPath}\" /v:n /nologo")
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(onOutput))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(err => onOutput($"[red]{Markup.Escape(err)}[/]")))
            .ExecuteAsync(ct);

        return new CommandResult(result.ExitCode, result.StartTime, result.ExitTime);
    }
}
