using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CliWrap;
using Spectre.Console;
using lazydotnet.Core;

namespace lazydotnet.Services;

public enum ExecutionStatus
{
    Idle,
    Building,
    Running,
    Stopped,
    Crashed
}

public partial class ProjectExecutionState
{
    [GeneratedRegex(@"\x1B\[[^@-~]*[@-~]", RegexOptions.Compiled)]
    private static partial Regex GetAnsiRegex();

    private static readonly Regex AnsiRegex = GetAnsiRegex();

    public string ProjectPath { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Idle;
    public List<string> Logs { get; } = [];
    public Lock LogLock { get; } = new();
    public CancellationTokenSource? Cts { get; set; }
    public Task? ExecutionTask { get; set; }
    public int? ExitCode { get; set; }

    public void AddLog(string message)
    {
        var cleanMessage = AnsiRegex.Replace(message, string.Empty);
        lock (LogLock)
        {
            Logs.Add(cleanMessage);
            if (Logs.Count > 2000) Logs.RemoveAt(0);
        }
    }

    public void ClearLogs()
    {
        lock (LogLock)
        {
            Logs.Clear();
        }
    }
}

public class ExecutionService
{
    private static readonly Lazy<ExecutionService> LazyInstance = new(() => new ExecutionService());
    public static ExecutionService Instance => LazyInstance.Value;

    private readonly ConcurrentDictionary<string, ProjectExecutionState> _states = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string, ExecutionStatus>? OnStatusChanged;
    public event Action<string, string>? OnLogReceived;

    public ProjectExecutionState GetOrCreateState(string projectPath, string projectName)
    {
        return _states.GetOrAdd(projectPath, path => new ProjectExecutionState
        {
            ProjectPath = path,
            ProjectName = projectName
        });
    }

    public bool IsRunning(string projectPath)
    {
        return _states.TryGetValue(projectPath, out var state) &&
               state.Status is ExecutionStatus.Running or ExecutionStatus.Building;
    }

    public async Task StartProjectAsync(string projectPath, string projectName)
    {
        var state = GetOrCreateState(projectPath, projectName);

        if (state.Status is ExecutionStatus.Running or ExecutionStatus.Building)
        {
            await StopProjectAsync(projectPath);
        }

        state.Cts = new CancellationTokenSource();
        state.Status = ExecutionStatus.Building;
        state.ExitCode = null;
        OnStatusChanged?.Invoke(projectPath, state.Status);

        state.AddLog($"[blue]Building project {Markup.Escape(projectName)}...[/]");

        var relativePath = PathHelper.GetRelativePath(projectPath);
        state.ExecutionTask = Task.Run(async () =>
        {
            try
            {
                // Build phase
                var buildCmd = Cli.Wrap("dotnet")
                    .WithArguments($"build \"{relativePath}\"")
                    .WithValidation(CommandResultValidation.None)
                    .WithStandardOutputPipe(PipeTarget.ToDelegate(line =>
                    {
                        state.AddLog($"[dim]{Markup.Escape(line)}[/]");
                    }))
                    .WithStandardErrorPipe(PipeTarget.ToDelegate(line =>
                    {
                        state.AddLog($"[red]{Markup.Escape(line)}[/]");
                    }));

                var buildResult = await buildCmd.ExecuteAsync(state.Cts.Token);
                if (buildResult.ExitCode != 0)
                {
                    state.Status = ExecutionStatus.Stopped;
                    state.ExitCode = buildResult.ExitCode;
                    state.AddLog($"[red]Build failed with exit code {buildResult.ExitCode}.[/]");
                    return;
                }

                // Run phase
                state.Status = ExecutionStatus.Running;
                OnStatusChanged?.Invoke(projectPath, state.Status);
                state.AddLog($"[blue]Starting project {Markup.Escape(projectName)}...[/]");

                var runCmd = Cli.Wrap("dotnet")
                    .WithArguments($"run --project \"{relativePath}\" --no-build")
                    .WithValidation(CommandResultValidation.None)
                    .WithStandardOutputPipe(PipeTarget.ToDelegate(line =>
                    {
                        state.AddLog(Markup.Escape(line));
                        OnLogReceived?.Invoke(projectPath, line);
                    }))
                    .WithStandardErrorPipe(PipeTarget.ToDelegate(line =>
                    {
                        var msg = $"[red]{Markup.Escape(line)}[/]";
                        state.AddLog(msg);
                        OnLogReceived?.Invoke(projectPath, msg);
                    }));

                var result = await runCmd.ExecuteAsync(state.Cts.Token);

                state.Status = ExecutionStatus.Stopped;
                state.ExitCode = result.ExitCode;
                state.AddLog(result.ExitCode == 0
                    ? "[green]Process finished successfully.[/]"
                    : $"[red]Process exited with code {result.ExitCode}.[/]");
            }
            catch (OperationCanceledException)
            {
                state.Status = ExecutionStatus.Stopped;
                state.AddLog("[yellow]Process stopped by user.[/]");
            }
            catch (Exception ex)
            {
                state.Status = ExecutionStatus.Crashed;
                var shortMsg = ex.Message.Split('\n')[0];
                state.AddLog($"[red]Process crashed: {Markup.Escape(shortMsg)}[/]");
            }
            finally
            {
                OnStatusChanged?.Invoke(projectPath, state.Status);
                state.Cts?.Dispose();
                state.Cts = null;
            }
        });

        await Task.CompletedTask;
    }

    public async Task StopProjectAsync(string projectPath)
    {
        if (_states.TryGetValue(projectPath, out var state))
        {
            if (state.Cts != null)
            {
                await state.Cts.CancelAsync();
            }

            if (state.ExecutionTask != null)
            {
                try
                {
                    await state.ExecutionTask;
                }
                catch
                {
                    // Ignore task cancellation exceptions
                }
                finally
                {
                    state.ExecutionTask = null;
                }
            }
        }
    }

    public async Task StopAllAsync()
    {
        var tasks = _states.Values
            .Where(s => s.Status == ExecutionStatus.Running)
            .Select(s => StopProjectAsync(s.ProjectPath));

        await Task.WhenAll(tasks);
    }
}
