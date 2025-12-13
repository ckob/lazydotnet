using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;

namespace lazydotnet.Services;

public static class AppCli
{
    public static event Action<string>? OnLog;

    public static async Task<CommandResult> RunAsync(Command command, CancellationToken ct = default)
    {
        OnLog?.Invoke($"[blue]Running: {Markup.Escape(command.ToString() ?? "")}[/]");
        return await command.ExecuteAsync(ct);
    }
    
    public static async Task<BufferedCommandResult> RunBufferedAsync(Command command, CancellationToken ct = default)
    {
         OnLog?.Invoke($"[blue]Running: {Markup.Escape(command.ToString() ?? "")}[/]");
         return await command.ExecuteBufferedAsync(ct);
    }
}
