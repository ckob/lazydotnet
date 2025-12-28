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

    public void InitializeContext(string rootDir, string? solutionFile)
    {
        RootDir = rootDir;
        SolutionFile = solutionFile;
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
                if (e.Data?.StartsWith("Named pipe server started: ") == true)
                {
                    pipeNameTcs.TrySetResult(e.Data["Named pipe server started: ".Length..].Trim());
                }
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var pipeName = await pipeNameTcs.Task.WaitAsync(cts.Token);

            _pipeStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipeStream.ConnectAsync(5000, cts.Token);

            var formatter = new SystemTextJsonFormatter();
            formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;

            var messageHandler = new HeaderDelimitedMessageHandler(_pipeStream, _pipeStream, formatter);
            _jsonRpc = new JsonRpc(messageHandler);
            _jsonRpc.StartListening();

            // Initialize the server
            var rootDir = RootDir ?? Directory.GetCurrentDirectory();
            var slnFile = SolutionFile ?? Directory.GetFiles(rootDir, "*.sln").FirstOrDefault();

            var initRequest = new InitializeRequest(
                new ClientInfo("lazydotnet", "2.0.0"),
                new ProjectInitializeInfo(rootDir, slnFile),
                new ClientOptions(new DebuggerOptions(null, true))
            );

            await _jsonRpc.InvokeWithParameterObjectAsync("initialize", new { request = initRequest }, cts.Token);

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
            if (!_process.HasExited)
            {
                try { _process.Kill(); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error killing process: {ex.Message}");
                }
            }
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

            return result ?? [];
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
            return result ?? [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error calling list-project-reference: {ex.Message}");
            throw;
        }
    }

    public async Task<IAsyncEnumerable<NugetPackageMetadata>> SearchPackagesAsync(string searchTerm, List<string>? sources = null)
    {
        await EnsureInitializedAsync();
        return await _jsonRpc!.InvokeWithParameterObjectAsync<IAsyncEnumerable<NugetPackageMetadata>>(
            "nuget/search-packages",
            new { searchTerm, sources });
    }

    public async Task<IAsyncEnumerable<string>> GetPackageVersionsAsync(string packageId, List<string>? sources = null, bool includePrerelease = false)
    {
        await EnsureInitializedAsync();
        return await _jsonRpc!.InvokeWithParameterObjectAsync<IAsyncEnumerable<string>>(
            "nuget/get-package-versions",
            new { packageId, sources, includePrerelease });
    }

    public async Task<IAsyncEnumerable<OutdatedDependencyInfoResponse>> GetOutdatedPackagesAsync(string targetPath, bool? includeTransitive = null)
    {
        await EnsureInitializedAsync();
        return await _jsonRpc!.InvokeWithParameterObjectAsync<IAsyncEnumerable<OutdatedDependencyInfoResponse>>(
            "outdated/packages",
            new { targetPath, includeTransitive });
    }

    public async Task<IAsyncEnumerable<PackageReference>> ListPackageReferencesAsync(string projectPath, string targetFramework = "")
    {
        await EnsureInitializedAsync();
        return await _jsonRpc!.InvokeWithParameterObjectAsync<IAsyncEnumerable<PackageReference>>(
            "msbuild/list-package-reference",
            new { projectPath, targetFramework });
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
