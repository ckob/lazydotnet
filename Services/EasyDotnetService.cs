using System.Diagnostics;
using System.IO.Pipes;
using StreamJsonRpc;
using System.Text.Json;

namespace lazydotnet.Services;

public class EasyDotnetService : IAsyncDisposable
{
    private Process? _process;
    private JsonRpc? _jsonRpc;
    private NamedPipeClientStream? _pipeStream;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private string? RootDir { get; set; }
    private string? SolutionFile { get; set; }

    public event Action<string>? OnServerOutput;

    private static string GetServerInfoPath()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lazydotnet", "server.info");
        return path;
    }

    public void InitializeContext(string rootDir, string? solutionFile)
    {
        RootDir = rootDir;
        SolutionFile = solutionFile;
    }

    private async Task<bool> TryConnectExistingAsync(CancellationToken ct)
    {
        var infoPath = GetServerInfoPath();
        if (!File.Exists(infoPath)) return false;

        try
        {
            var pipeName = await File.ReadAllTextAsync(infoPath, ct);
            if (string.IsNullOrWhiteSpace(pipeName)) return false;

            _pipeStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipeStream.ConnectAsync(200, ct);

            await SetupRpcAsync(ct);
            return true;
        }
        catch
        {
            if (_pipeStream != null)
                await _pipeStream.DisposeAsync();
            _pipeStream = null;
            return false;
        }
    }

    private async Task SetupRpcAsync(CancellationToken ct)
    {
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;

        var messageHandler = new HeaderDelimitedMessageHandler(_pipeStream!, _pipeStream!, formatter);
        _jsonRpc = new JsonRpc(messageHandler);
        _jsonRpc.StartListening();

        var rootDir = RootDir ?? Directory.GetCurrentDirectory();
        var slnFile = SolutionFile ?? Directory.GetFiles(rootDir, "*.sln").FirstOrDefault();

        var initRequest = new InitializeRequest(
            new ClientInfo("lazydotnet", "2.0.0"),
            new ProjectInitializeInfo(rootDir, slnFile),
            new ClientOptions(new DebuggerOptions(null, true))
        );

        await _jsonRpc.InvokeWithParameterObjectAsync("initialize", new { request = initRequest }, ct);
    }

    private async Task EnsureInitializedAsync()
    {
        if (_jsonRpc != null && _pipeStream is { IsConnected: true })
            return;

        await _initLock.WaitAsync();
        try
        {
            if (_jsonRpc != null && _pipeStream is { IsConnected: true })
                return;

            await CleanupAsync();

            using var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            if (await TryConnectExistingAsync(startupCts.Token))
            {
                OnServerOutput?.Invoke("[Client]: Connected to existing easydotnet server.");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "easydotnet",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = RootDir ?? Directory.GetCurrentDirectory()
            };

            _process = new Process { StartInfo = startInfo };

            var pipeNameTcs = new TaskCompletionSource<string>();

            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) OnServerOutput?.Invoke($"[Server StdOut]: {e.Data}");
                if (e.Data?.StartsWith("Named pipe server started: ") == true)
                {
                    var name = e.Data["Named pipe server started: ".Length..].Trim();
                    pipeNameTcs.TrySetResult(name);
                }
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) OnServerOutput?.Invoke($"[Server StdErr]: {e.Data}");
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            var pipeName = await pipeNameTcs.Task.WaitAsync(startupCts.Token);

            _pipeStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipeStream.ConnectAsync(5000, startupCts.Token);

            try
            {
                var dir = Path.GetDirectoryName(GetServerInfoPath());
                if (dir != null) Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(GetServerInfoPath(), pipeName, startupCts.Token);
            }
            catch
            {
                // Ignore
            }

            await SetupRpcAsync(startupCts.Token);

            _process.EnableRaisingEvents = true;
            _process.Exited += (sender, args) =>
            {
                _ = CleanupAsync();
            };
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task CleanupAsync()
    {
        if (_jsonRpc != null)
        {
            _jsonRpc.Dispose();
            _jsonRpc = null;
        }

        if (_pipeStream != null)
        {
            await _pipeStream.DisposeAsync();
            _pipeStream = null;
        }

        if (_process != null)
        {
            _process.Dispose();
            _process = null;
        }
    }

    public async Task<List<SolutionFileProjectResponse>> ListProjectsAsync(string solutionFilePath)
    {
        await EnsureInitializedAsync();
        try
        {
            var result = await _jsonRpc!.InvokeWithParameterObjectAsync<List<SolutionFileProjectResponse>>(
                "solution/list-projects",
                new { solutionFilePath });

            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error calling list-projects: {ex.Message}");
            throw;
        }
    }

    public async Task<List<string>> ListProjectReferencesAsync(string projectPath)
    {
        await EnsureInitializedAsync();
        try
        {
            var result = await _jsonRpc!.InvokeWithParameterObjectAsync<List<string>>(
                "msbuild/list-project-reference",
                new { projectPath });
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error calling list-project-reference: {ex.Message}");
            throw;
        }
    }

    public async Task<bool> AddProjectReferenceAsync(string projectPath, string targetPath)
    {
        await EnsureInitializedAsync();
        return await _jsonRpc!.InvokeWithParameterObjectAsync<bool>(
            "msbuild/add-project-reference",
            new { projectPath, targetPath });
    }

    public async Task<bool> RemoveProjectReferenceAsync(string projectPath, string targetPath)
    {
        await EnsureInitializedAsync();
        return await _jsonRpc!.InvokeWithParameterObjectAsync<bool>(
            "msbuild/remove-project-reference",
            new { projectPath, targetPath });
    }

    public async Task<IAsyncEnumerable<NugetPackageMetadata>> SearchPackagesAsync(string searchTerm, List<string>? sources = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        return await _jsonRpc!.InvokeWithParameterObjectAsync<IAsyncEnumerable<NugetPackageMetadata>>(
            "nuget/search-packages",
            new { searchTerm, sources },
            ct);
    }

    public async Task<IAsyncEnumerable<string>> GetPackageVersionsAsync(string packageId, List<string>? sources = null, bool includePrerelease = false, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        return await _jsonRpc!.InvokeWithParameterObjectAsync<IAsyncEnumerable<string>>(
            "nuget/get-package-versions",
            new { packageId, sources, includePrerelease },
            ct);
    }

    public async Task<IAsyncEnumerable<OutdatedDependencyInfoResponse>> GetOutdatedPackagesAsync(string targetPath, bool? includeTransitive = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        return await _jsonRpc!.InvokeWithParameterObjectAsync<IAsyncEnumerable<OutdatedDependencyInfoResponse>>(
            "outdated/packages",
            new { targetPath, includeTransitive },
            ct);
    }

    public async Task<IAsyncEnumerable<PackageReference>> ListPackageReferencesAsync(string projectPath, string targetFramework = "", CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        return await _jsonRpc!.InvokeWithParameterObjectAsync<IAsyncEnumerable<PackageReference>>(
            "msbuild/list-package-reference",
            new { projectPath, targetFramework },
            ct);
    }

    public async Task<IAsyncEnumerable<DiscoveredTest>> DiscoverTestsAsync(string projectPath, string? targetFrameworkMoniker = null, string? configuration = null, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        return await _jsonRpc!.InvokeWithParameterObjectAsync<IAsyncEnumerable<DiscoveredTest>>(
            "test/discover",
            new { projectPath, targetFrameworkMoniker, configuration },
            ct);
    }

    public async Task<IAsyncEnumerable<TestRunResult>> RunTestsAsync(string projectPath, string configuration, RunRequestNode[] filter, string? targetFrameworkMoniker = null)
    {
        await EnsureInitializedAsync();
        return await _jsonRpc!.InvokeWithParameterObjectAsync<IAsyncEnumerable<TestRunResult>>(
            "test/run",
            new { projectPath, configuration, filter, targetFrameworkMoniker });
    }

    public async Task RestorePackagesAsync(string targetPath)
    {
        await EnsureInitializedAsync();
        await _jsonRpc!.InvokeWithParameterObjectAsync("nuget/restore", new { targetPath });
    }

    public async Task<DotnetProjectV1> GetProjectPropertiesAsync(string targetPath, string targetFramework = "", string configuration = "")
    {
        await EnsureInitializedAsync();
        return await _jsonRpc!.InvokeWithParameterObjectAsync<DotnetProjectV1>(
            "msbuild/project-properties",
            new { request = new { targetPath, targetFramework, configuration } });
    }

    public async ValueTask DisposeAsync()
    {
        await CleanupAsync();
        _initLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
