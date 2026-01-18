using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Microsoft.Build.Evaluation;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CliWrap;

namespace lazydotnet.Services;

public record DiscoveredTest(
    string Id,
    string? Namespace,
    string Name,
    string DisplayName,
    string? FilePath,
    int? LineNumber
);

public record RunRequestNode(
    string Uid,
    string DisplayName
);

public record TestRunResult(
    string Id,
    string Outcome,
    long? Duration,
    IAsyncEnumerable<string> StackTrace,
    string[] ErrorMessage,
    IAsyncEnumerable<string> StdOut
);

public class TestNode
{
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Uid { get; set; }
    public List<TestNode> Children { get; } = [];
    public TestNode? Parent { get; set; }
    public bool IsContainer { get; set; }
    public bool IsTest { get; set; }
    public bool IsTheoryContainer { get; set; }
    public bool IsExpanded { get; set; } = true;
    public int Depth { get; set; }
    public int TestCount { get; set; }

    public TestStatus Status { get; set; } = TestStatus.None;
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public double Duration { get; set; }
    public string? FilePath { get; set; }
    public int? LineNumber { get; set; }
    public List<TestOutputLine> Output { get; } = [];
    public readonly Lock OutputLock = new();

    public List<TestOutputLine> GetOutputSnapshot()
    {
        lock (OutputLock)
        {
            return [.. Output];
        }
    }
}

public record TestOutputLine(string Text, string? Style = null);

public enum TestStatus
{
    None,
    Running,
    Passed,
    Failed
}

public class TestService
{
    private static readonly SemaphoreSlim _vstestLock = new(1, 1);

    public static async Task<List<DiscoveredTest>> DiscoverTestsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        await _vstestLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(async () =>
            {
                try
                {
                    if (!IsTestProject(projectPath)) return [];

                    var isMtp = IsMtpProject(projectPath);
                    var targetPath = GetTargetPath(projectPath);
                    if (targetPath == null || !File.Exists(targetPath)) return new List<DiscoveredTest>();

                    if (isMtp)
                    {
                        var mtpTests = await DiscoverMtpTestsAsync(targetPath, cancellationToken);
                        if (mtpTests.Count > 0) return mtpTests;
                    }

                    return await DiscoverVsTestsAsync(targetPath, cancellationToken);
                }
                catch (Exception ex)
                {
                    AppCli.Log($"[red]Discovery error: {ex.Message}[/]");
                    return [];
                }
            }, cancellationToken);
        }
        finally
        {
            _vstestLock.Release();
        }
    }

    private static async Task<List<DiscoveredTest>> DiscoverVsTestsAsync(string targetPath, CancellationToken ct)
    {
        var vstestPath = VsTestConsoleLocator.GetVsTestConsolePath();
        if (vstestPath == null)
        {
            AppCli.Log("[red]VSTest console not found.[/]");
            return [];
        }

        AppCli.Log($"[dim]Using VSTest: {vstestPath}[/]");
        var wrapper = new VsTestConsoleWrapper(vstestPath);
        var handler = new DiscoveryHandler();

        try
        {
            AppCli.Log($"[dim]Discovering tests for {Path.GetFileName(targetPath)}...[/]");
            
            return await Task.Run(() =>
            {
                var options = new TestPlatformOptions
                {
                    CollectMetrics = false,
                    SkipDefaultAdapters = false
                };

                // VSTest translation layer DiscoverTests is blocking when using this overload
                wrapper.DiscoverTests([targetPath], null, options, handler);
                
                handler.CompletionTask.Wait(ct);
                
                return handler.Tests.Select(tc => new DiscoveredTest(
                    tc.Id.ToString(),
                    null,
                    tc.FullyQualifiedName,
                    tc.DisplayName,
                    tc.CodeFilePath,
                    tc.LineNumber > 0 ? tc.LineNumber : null
                )).ToList();
            }, ct);
        }
        catch (Exception ex)
        {
            AppCli.Log($"[yellow]VSTest discovery failed: {ex.Message}. Falling back to CLI.[/]");
            return await DiscoverVsTestsCliAsync(targetPath, ct);
        }
        finally
        {
            wrapper.EndSession();
        }
    }

    private static async Task<List<DiscoveredTest>> DiscoverVsTestsCliAsync(string targetPath, CancellationToken ct)
    {
        try
        {
            var command = targetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? Cli.Wrap("dotnet").WithArguments(["test", targetPath, "--list-tests"])
                : Cli.Wrap(targetPath).WithArguments("--list-tests");

             command = command.WithValidation(CommandResultValidation.None);

            var result = await AppCli.RunBufferedAsync(command, ct);
             if (result.ExitCode != 0)
            {
                if (!string.IsNullOrEmpty(result.StandardError))
                    AppCli.Log($"[dim]VSTest CLI discovery stderr: {result.StandardError.Trim()}[/]");
                return [];
            }

            var tests = new List<DiscoveredTest>();
            var lines = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            bool testSectionStarted = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                if (trimmed.Contains("The following Tests are available:", StringComparison.OrdinalIgnoreCase))
                {
                    testSectionStarted = true;
                    continue;
                }

                if (trimmed.Contains(' ') && !testSectionStarted) continue;

                tests.Add(new DiscoveredTest(
                    Guid.NewGuid().ToString(),
                    null,
                    trimmed,
                    trimmed,
                    null,
                    null
                ));
            }

            return tests;
        }
        catch (Exception ex)
        {
            AppCli.Log($"[red]VSTest CLI discovery failed: {ex.Message}[/]");
            return [];
        }
    }

    private static bool IsTestProject(string projectPath)
    {
        if (string.IsNullOrEmpty(projectPath) || projectPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var project = ProjectCollection.GlobalProjectCollection.LoadedProjects.FirstOrDefault(p => p.FullPath == projectPath);
            if (project == null)
            {
                project = ProjectCollection.GlobalProjectCollection.LoadProject(projectPath);
            }

            var isTestProject = project.GetPropertyValue("IsTestProject");
            var isMtp = project.GetPropertyValue("IsTestingPlatformApplication");

            return string.Equals(isTestProject, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(isMtp, "true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsMtpProject(string projectPath)
    {
        try
        {
            var project = ProjectCollection.GlobalProjectCollection.LoadedProjects.FirstOrDefault(p => p.FullPath == projectPath);
            if (project == null)
            {
                project = ProjectCollection.GlobalProjectCollection.LoadProject(projectPath);
            }
            var isMtpVal = project.GetPropertyValue("IsTestingPlatformApplication");
            var outputType = project.GetPropertyValue("OutputType");

            return string.Equals(isMtpVal, "true", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            AppCli.Log($"[red]Error checking IsMtpProject: {ex.Message}[/]");
            return false;
        }
    }

    private static async Task<List<DiscoveredTest>> DiscoverMtpTestsAsync(string targetPath, CancellationToken ct)
    {
        MtpClient? mtpClient = null;
        try
        {
            mtpClient = await MtpClient.CreateAsync(targetPath, ct);
            var tests = await mtpClient.DiscoverTestsAsync(ct);

            if (tests.Count == 0)
            {
                return await DiscoverMtpTestsCliAsync(targetPath, ct);
            }
            return tests;
        }
        catch (Exception ex)
        {
            AppCli.Log($"[yellow]MTP RPC discovery failed: {ex.Message}. Falling back to CLI.[/]");
            return await DiscoverMtpTestsCliAsync(targetPath, ct);
        }
        finally
        {
            if (mtpClient != null) await mtpClient.DisposeAsync();
        }
    }

    private static async Task<List<DiscoveredTest>> DiscoverMtpTestsCliAsync(string targetPath, CancellationToken ct)
    {
        try
        {
            var command = targetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? Cli.Wrap("dotnet").WithArguments(["test", targetPath, "--list-tests"])
                : Cli.Wrap(targetPath).WithArguments("--list-tests");

            command = command.WithValidation(CommandResultValidation.None);

            var result = await AppCli.RunBufferedAsync(command, ct);
            if (result.ExitCode != 0)
            {
                if (!string.IsNullOrEmpty(result.StandardError))
                    AppCli.Log($"[dim]MTP CLI discovery stderr: {result.StandardError.Trim()}[/]");
                return [];
            }

            var tests = new List<DiscoveredTest>();
            var lines = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            bool testSectionStarted = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                if (trimmed.Contains("The following tests are available:", StringComparison.OrdinalIgnoreCase))
                {
                    testSectionStarted = true;
                    continue;
                }

                if (trimmed.Contains(' ') && !testSectionStarted) continue;

                tests.Add(new DiscoveredTest(trimmed, null, trimmed, trimmed, null, null));
            }

            return tests;
        }
        catch
        {
            return [];
        }
    }

    private static string? GetTargetPath(string projectPath)
    {
        try
        {
            var project = ProjectCollection.GlobalProjectCollection.LoadedProjects.FirstOrDefault(p => p.FullPath == projectPath);
            if (project == null)
            {
                project = ProjectCollection.GlobalProjectCollection.LoadProject(projectPath);
            }
            var targetPath = project.GetPropertyValue("TargetPath");
            return targetPath;
        }
        catch
        {
            return null;
        }
    }

    private class DiscoveryHandler : ITestDiscoveryEventsHandler, ITestDiscoveryEventsHandler2
    {
        public List<TestCase> Tests { get; } = [];
        private readonly TaskCompletionSource _tcs = new();
        public Task CompletionTask => _tcs.Task;

        public void HandleDiscoveredTests(IEnumerable<TestCase>? discoveredTestCases)
        {
            if (discoveredTestCases != null) Tests.AddRange(discoveredTestCases);
        }

        public void HandleDiscoveryComplete(long totalTests, IEnumerable<TestCase>? lastChunk, bool isAborted)
        {
            if (lastChunk != null) Tests.AddRange(lastChunk);
            _tcs.TrySetResult();
        }

        public void HandleDiscoveryComplete(DiscoveryCompleteEventArgs args, IEnumerable<TestCase>? lastChunk)
        {
            if (lastChunk != null) Tests.AddRange(lastChunk);
            _tcs.TrySetResult();
        }

        public void HandleLogMessage(TestMessageLevel level, string? message) 
        {
            if (level == TestMessageLevel.Error) AppCli.Log($"[red]VSTest: {message}[/]");
        }
        public void HandleRawMessage(string rawMessage) { }
    }

    public static TestNode BuildTestTree(List<DiscoveredTest> tests)
    {
        var root = new TestNode { Name = "Tests", IsContainer = true, Depth = 0 };

        foreach (var test in tests)
        {
            var fqn = test.Name;
            var parts = fqn.Split('.');
            var current = root;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                var part = parts[i];
                var existing = current.Children.FirstOrDefault(c => c.Name == part && c.IsContainer);
                if (existing == null)
                {
                    existing = new TestNode
                    {
                        Name = part,
                        Parent = current,
                        Depth = current.Depth + 1,
                        IsContainer = true,
                        FullName = string.Join(".", parts.Take(i + 1))
                    };
                    current.Children.Add(existing);
                }

                if (string.IsNullOrEmpty(existing.FilePath))
                {
                    existing.FilePath = test.FilePath;
                    existing.LineNumber = test.LineNumber;
                }

                current = existing;
            }

            var methodName = parts[^1];
            var shortDisplayName = GetShortDisplayName(test.DisplayName, fqn);

            if (shortDisplayName == methodName)
            {
                var testNode = new TestNode
                {
                    Name = methodName,
                    Parent = current,
                    Depth = current.Depth + 1,
                    IsTest = true,
                    IsContainer = false,
                    FullName = fqn,
                    Uid = test.Id,
                    FilePath = test.FilePath,
                    LineNumber = test.LineNumber
                };
                current.Children.Add(testNode);
            }
            else
            {
                var methodContainer = current.Children.FirstOrDefault(c => c.Name == methodName && c.IsContainer);
                if (methodContainer == null)
                {
                    methodContainer = new TestNode
                    {
                        Name = methodName,
                        Parent = current,
                        Depth = current.Depth + 1,
                        IsContainer = true,
                        IsTheoryContainer = true,
                        IsExpanded = false,
                        FullName = fqn,
                        FilePath = test.FilePath,
                        LineNumber = test.LineNumber
                    };
                    current.Children.Add(methodContainer);
                }

                var caseNode = new TestNode
                {
                    Name = shortDisplayName,
                    Parent = methodContainer,
                    Depth = methodContainer.Depth + 1,
                    IsTest = true,
                    IsContainer = false,
                    FullName = fqn,
                    Uid = test.Id,
                    FilePath = test.FilePath,
                    LineNumber = test.LineNumber
                };
                methodContainer.Children.Add(caseNode);
            }
        }

        CompactTree(root);
        SortTree(root);
        RecalculateMetadata(root, 0);

        return root;
    }

    private static int RecalculateMetadata(TestNode node, int depth)
    {
        node.Depth = depth;
        if (node.IsTest)
        {
            node.TestCount = 1;
        }
        else
        {
            node.TestCount = 0;
            foreach (var child in node.Children)
            {
                node.TestCount += RecalculateMetadata(child, depth + 1);
            }
        }
        return node.TestCount;
    }

    private static string GetShortDisplayName(string displayName, string fqn)
    {
        if (displayName.StartsWith(fqn) && displayName.Length > fqn.Length)
        {
            var suffix = displayName[fqn.Length..];
            if (suffix.StartsWith('(') || suffix.StartsWith('['))
                return suffix;
        }

        var methodName = fqn.Split('.').Last();
        if (displayName.StartsWith(methodName) && displayName.Length > methodName.Length)
        {
             var suffix = displayName[methodName.Length..];
             if (suffix.StartsWith('(') || suffix.StartsWith('['))
                return suffix;
        }

        if (string.IsNullOrEmpty(displayName))
            return fqn;

        if (displayName == fqn || displayName == methodName)
        {
            return methodName;
        }

        return displayName;
    }

    private static void CompactTree(TestNode node)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                CompactTree(node.Children[i]);
            }

            if (node.Children.Count != 1)
            {
                continue;
            }

            var child = node.Children[0];
            if (child is { IsContainer: true, IsTest: false, IsTheoryContainer: false } && node is { IsTheoryContainer: false, Depth: > 0 })
            {
                node.Name = $"{node.Name}.{child.Name}";
                node.FullName = child.FullName;
                node.Children.Clear();
                node.Children.AddRange(child.Children);

                foreach (var c in node.Children)
                {
                    c.Parent = node;
                }

                changed = true;
            }
        }
    }

    private static void SortTree(TestNode node)
    {
        node.Children.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        foreach(var child in node.Children) SortTree(child);
    }

    public static async Task<IAsyncEnumerable<TestRunResult>> RunTestsAsync(string projectPath, RunRequestNode[] filter)
    {
        if (!IsTestProject(projectPath)) return EmptyRun();

        var isMtp = IsMtpProject(projectPath);
        var targetPath = GetTargetPath(projectPath);
        if (targetPath == null || !File.Exists(targetPath)) return EmptyRun();

        if (isMtp)
        {
            try
            {
                var mtpClient = await MtpClient.CreateAsync(targetPath, CancellationToken.None);
                return mtpClient.RunTestsAsync(filter, CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppCli.Log($"[yellow]MTP RPC run failed: {ex.Message}. Falling back to CLI.[/]");
                return RunMtpTestsCliAsync(targetPath, filter);
            }
        }

        await _vstestLock.WaitAsync();
        try
        {
            var vstestPath = VsTestConsoleLocator.GetVsTestConsolePath();
            if (vstestPath == null) return EmptyRun();

            var wrapper = new VsTestConsoleWrapper(vstestPath);
            var handler = new RunHandler();

            _ = Task.Run(() =>
            {
                try
                {
                    var options = new TestPlatformOptions
                    {
                        CollectMetrics = false,
                        SkipDefaultAdapters = false
                    };

                    // To run tests reliably, we first discover them to get the TestCase objects
                    // because RunTests(IEnumerable<TestCase> tests, ...) is more reliable than filters
                    var discoveryHandler = new DiscoveryHandler();
                    wrapper.DiscoverTests([targetPath], null, options, discoveryHandler);
                    discoveryHandler.CompletionTask.Wait();

                    var testCases = discoveryHandler.Tests;
                    if (filter != null && filter.Length > 0)
                    {
                        var filterIds = filter.Select(f => f.Uid).ToHashSet();
                        testCases = testCases.Where(t => filterIds.Contains(t.Id.ToString())).ToList();
                    }

                    wrapper.RunTests(testCases, null, options, handler);
                }
                catch (Exception ex)
                {
                    handler.Fail(ex);
                }
                finally
                {
                    // Note: wrapper.EndSession() should probably be called when run is complete
                    // But handler is async. We'll let the handler complete first.
                }
            });

            return handler.GetResultsAsync();
        }
        finally
        {
            _vstestLock.Release();
        }
    }

    private static async IAsyncEnumerable<TestRunResult> RunMtpTestsCliAsync(string targetPath, RunRequestNode[] filter)
    {
        var channel = Channel.CreateUnbounded<TestRunResult>();
        var args = new List<string>();

        if (targetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            args.Add(targetPath);
        }

        if (filter is { Length: > 0 })
        {
            var filterStr = string.Join('|', filter.Select(f => $"Name~{f.DisplayName}"));
            args.Add("--filter");
            args.Add(filterStr);
        }

        var command = targetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? Cli.Wrap("dotnet").WithArguments(args)
            : Cli.Wrap(targetPath).WithArguments(args);

        command = command.WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(line =>
            {
                if (line.StartsWith("Passed: ", StringComparison.OrdinalIgnoreCase))
                {
                    var name = line[8..].Split('(')[0].Trim();
                    channel.Writer.TryWrite(new TestRunResult(name, "Passed", null, EmptyStrings(), [], EmptyStrings()));
                }
                else if (line.StartsWith("Failed: ", StringComparison.OrdinalIgnoreCase))
                {
                    var name = line[8..].Split('(')[0].Trim();
                    channel.Writer.TryWrite(new TestRunResult(name, "Failed", null, EmptyStrings(), ["Test failed"], EmptyStrings()));
                }
            }));

        _ = Task.Run(async () =>
        {
            try
            {
                await command.ExecuteAsync();
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        });

        while (await channel.Reader.WaitToReadAsync())
        {
            while (channel.Reader.TryRead(out var result))
            {
                yield return result;
            }
        }
    }

    private static async IAsyncEnumerable<string> EmptyStrings()
    {
        yield break;
    }

    private static async IAsyncEnumerable<TestRunResult> EmptyRun()
    {
        yield break;
    }

    private sealed class RunHandler : ITestRunEventsHandler
    {
        private readonly Channel<TestRunResult> _channel = Channel.CreateUnbounded<TestRunResult>();

        public async IAsyncEnumerable<TestRunResult> GetResultsAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            while (await _channel.Reader.WaitToReadAsync(ct))
            {
                while (_channel.Reader.TryRead(out var result))
                {
                    yield return result;
                }
            }
        }

        public void HandleTestRunStatsChange(TestRunChangedEventArgs? testRunChangedArgs)
        {
            if (testRunChangedArgs?.NewTestResults == null) return;

            foreach (var result in testRunChangedArgs.NewTestResults)
            {
                _channel.Writer.TryWrite(new TestRunResult(
                    result.TestCase.Id.ToString(),
                    result.Outcome.ToString(),
                    (long?)result.Duration.TotalMilliseconds,
                    ToAsyncEnumerable(result.ErrorStackTrace),
                    result.ErrorMessage != null ? [result.ErrorMessage] : [],
                    ToAsyncEnumerable(GetStdOut(result))
                ));
            }
        }

        private static string? GetStdOut(TestResult result)
        {
            return result.Messages.FirstOrDefault(m => m.Category == TestMessageLevel.Informational.ToString())?.Text;
        }

        private static async IAsyncEnumerable<string> ToAsyncEnumerable(string? text)
        {
            if (!string.IsNullOrEmpty(text)) yield return text;
        }

        public void HandleTestRunComplete(TestRunCompleteEventArgs testRunCompleteArgs, TestRunChangedEventArgs? lastChunkArgs, ICollection<AttachmentSet>? runContextAttachments, ICollection<string>? executorUris)
        {
            if (lastChunkArgs != null) HandleTestRunStatsChange(lastChunkArgs);
            _channel.Writer.TryComplete();
        }

        public void Fail(Exception ex) => _channel.Writer.TryComplete(ex);

        public void HandleLogMessage(TestMessageLevel level, string? message) { }
        public void HandleRawMessage(string rawMessage) { }
        public int LaunchProcessWithDebuggerAttached(TestProcessStartInfo testProcessStartInfo) => 0;
    }
}
