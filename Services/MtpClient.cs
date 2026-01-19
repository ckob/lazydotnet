using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using System.Text.Json;
using StreamJsonRpc;
using System.Threading.Channels;
using CliWrap;

namespace lazydotnet.Services;

public record MtpInitializeRequest(int ProcessId, MtpClientInfo ClientInfo, MtpClientCapabilities Capabilities);
public record MtpClientInfo(string Name, string Version = "1.0.0");
public record MtpClientCapabilities(MtpClientTestingCapabilities Testing);
public record MtpClientTestingCapabilities(bool DebuggerProvider);

public record MtpDiscoveryRequest(Guid RunId);
public record MtpRunRequest(MtpRunRequestNode[] Tests, Guid RunId);

public record MtpRunRequestNode(
    [property: JsonPropertyName("uid")]
    string Uid,
    [property: JsonPropertyName("display-name")]
    string DisplayName
);

public record MtpTestNodeUpdate(
    [property: JsonPropertyName("node")]
    MtpTestNode Node,
    [property: JsonPropertyName("parent")]
    string? Parent);

public record MtpTestNode(
    [property: JsonPropertyName("uid")]
    string Uid,
    [property: JsonPropertyName("display-name")]
    string DisplayName,
    [property: JsonPropertyName("execution-state")]
    string? ExecutionState = null,
    [property: JsonPropertyName("time.duration-ms")]
    float? Duration = null,
    [property: JsonPropertyName("error.message")]
    string? Message = null,
    [property: JsonPropertyName("error.stacktrace")]
    string? StackTrace = null,
    [property: JsonPropertyName("standardOutput")]
    string? StandardOutput = null,
    [property: JsonPropertyName("location.namespace")]
    string? TestNamespace = null,
    [property: JsonPropertyName("location.method")]
    string? TestMethod = null,
    [property: JsonPropertyName("location.type")]
    string? TestType = null,
    [property: JsonPropertyName("location.file")]
    string? FilePath = null,
    [property: JsonPropertyName("location.line-start")]
    int? LineStart = null,
    [property: JsonPropertyName("node-type")]
    string? NodeType = null
);

public class MtpClient : IAsyncDisposable
{
    private readonly JsonRpc _jsonRpc;
    private readonly TcpClient _tcpClient;
    private readonly TcpListener _listener;
    private readonly string _targetPath;

    private readonly Channel<MtpTestNodeUpdate> _updates = Channel.CreateUnbounded<MtpTestNodeUpdate>();
    private readonly ConcurrentDictionary<Guid, List<MtpTestNode>> _runResults = new();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _completionSources = new();

    private MtpClient(TcpListener listener, TcpClient tcpClient, JsonRpc jsonRpc, string targetPath)
    {
        _listener = listener;
        _tcpClient = tcpClient;
        _jsonRpc = jsonRpc;
        _targetPath = targetPath;

        _jsonRpc.AddLocalRpcTarget(this, new JsonRpcTargetOptions { MethodNameTransform = CommonMethodNameTransforms.CamelCase });
        _jsonRpc.StartListening();
    }

    public static async Task<MtpClient> CreateAsync(string targetPath, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var args = new List<string>();
        if (targetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            args.Add(targetPath);
        }

        args.AddRange(["--server", "--client-host", "localhost", "--client-port", port.ToString()]);

        var command = targetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? Cli.Wrap("dotnet").WithArguments(args)
            : Cli.Wrap(targetPath).WithArguments(args);

        _ = command.ExecuteAsync(ct);

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var tcpClient = await listener.AcceptTcpClientAsync(linkedCts.Token);

            var formatter = new SystemTextJsonFormatter();
            formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            formatter.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());

            var jsonRpc = new JsonRpc(new HeaderDelimitedMessageHandler(tcpClient.GetStream(), tcpClient.GetStream(), formatter));

            var client = new MtpClient(listener, tcpClient, jsonRpc, targetPath);

            await jsonRpc.InvokeWithParameterObjectAsync("initialize", new MtpInitializeRequest(
                Environment.ProcessId,
                new MtpClientInfo("lazydotnet", "1.0.0"),
                new MtpClientCapabilities(new MtpClientTestingCapabilities(false))
            ), linkedCts.Token);

            return client;
        }
        catch (Exception ex)
        {
            AppCli.Log($"[red]MTP connection failed: {ex.Message}[/]");
            throw;
        }
    }

    [JsonRpcMethod("testing/testUpdates/tests")]
    public void OnTestsUpdate(JsonElement runId, JsonElement changes)
    {
        Guid runIdGuid;
        try { runIdGuid = runId.GetGuid(); }
        catch { return; }

        if (changes.ValueKind == JsonValueKind.Null)
        {
            if (_completionSources.TryGetValue(runIdGuid, out var tcs))
            {
                tcs.TrySetResult();
            }
            return;
        }

        if (changes.ValueKind == JsonValueKind.Array)
        {
            var updates = changes.Deserialize<MtpTestNodeUpdate[]>(new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (updates != null)
            {
                var list = _runResults.GetOrAdd(runIdGuid, _ => []);
                lock (list)
                {
                    foreach (var update in updates)
                    {
                        list.Add(update.Node);
                        _updates.Writer.TryWrite(update);
                    }
                }
            }
        }
    }

    [JsonRpcMethod("client/log")]
    public void OnClientLog(object? level, string? message)
    {
        // Client log received from MTP server, currently ignored.
    }

    [JsonRpcMethod("telemetry/update")]
    public void OnTelemetryUpdate(object? payload)
    {
        // Telemetry update received from MTP server, currently ignored.
    }

    public async Task<List<DiscoveredTest>> DiscoverTestsAsync(CancellationToken ct)
    {
        var runId = Guid.NewGuid();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _completionSources[runId] = tcs;

        try
        {
            await _jsonRpc.InvokeWithParameterObjectAsync("testing/discoverTests", new MtpDiscoveryRequest(runId), ct);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            await tcs.Task.WaitAsync(linkedCts.Token);

            if (_runResults.TryGetValue(runId, out var nodes))
            {
                return nodes
                    .Where(n => n.NodeType is "test" or "action" or null)
                    .Select(MapToDiscoveredTest).ToList();
            }
            return [];
        }
        catch (Exception ex)
        {
            AppCli.Log($"[red]MTP discovery RPC failed: {ex.Message}[/]");
            throw;
        }
        finally
        {
            _completionSources.TryRemove(runId, out _);
            _runResults.TryRemove(runId, out _);
        }
    }

    private DiscoveredTest MapToDiscoveredTest(MtpTestNode n)
    {
        var name = GetDiscoveredTestName(n);

        var openParenIndex = name.IndexOf('(');
        if (openParenIndex > 0)
        {
            name = name[..openParenIndex];
        }

        return new DiscoveredTest(
            n.Uid,
            n.TestNamespace,
            name,
            n.DisplayName,
            n.FilePath,
            n.LineStart,
            _targetPath,
            true
        );
    }

    private static string GetDiscoveredTestName(MtpTestNode n)
    {
        if (!string.IsNullOrEmpty(n.TestType) && !string.IsNullOrEmpty(n.TestMethod))
        {
            return $"{n.TestType}.{n.TestMethod}";
        }

        var name = n.DisplayName;

        if (!string.IsNullOrEmpty(n.TestNamespace))
        {
            if (!name.StartsWith(n.TestNamespace + "."))
            {
                name = $"{n.TestNamespace}.{name}";
            }
        }
        else if (!string.IsNullOrEmpty(n.TestType) && !name.StartsWith(n.TestType + "."))
        {
            name = $"{n.TestType}.{name}";
        }

        return name;
    }

    public async IAsyncEnumerable<TestRunResult> RunTestsAsync(RunRequestNode[] filter, [EnumeratorCancellation] CancellationToken ct)
    {
        var runId = Guid.NewGuid();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _completionSources[runId] = tcs;

        // Map generic RunRequestNode to MtpRunRequestNode
        var mtpTests = filter.Select(t => new MtpRunRequestNode(t.Uid, t.DisplayName)).ToArray();

        // Start running tests
        var runTask = _jsonRpc.InvokeWithParameterObjectAsync("testing/runTests", new MtpRunRequest(mtpTests, runId), ct);

        while (!tcs.Task.IsCompleted || _updates.Reader.Count > 0)

        {
            MtpTestNodeUpdate? update = null;
            try
            {
                if (await _updates.Reader.WaitToReadAsync(ct))
                {
                    _updates.Reader.TryRead(out update);
                }
            }
            catch (OperationCanceledException) { break; }

            if (update is { Node.ExecutionState: "passed" or "failed" or "skipped" })
            {
                yield return new TestRunResult(
                    update.Node.Uid,
                    update.Node.ExecutionState,
                    (long?)(update.Node.Duration),
                    ToAsyncEnumerable(update.Node.StackTrace),
                    update.Node.Message != null ? [update.Node.Message] : [],
                    ToAsyncEnumerable(update.Node.StandardOutput)
                );
            }

            if (tcs.Task.IsCompleted && _updates.Reader.Count == 0) break;
        }

        await runTask;
        _completionSources.TryRemove(runId, out _);
        _runResults.TryRemove(runId, out _);
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(string? text)
    {
        if (!string.IsNullOrEmpty(text)) yield return text;
    }

    public async ValueTask DisposeAsync()
    {
        try { await _jsonRpc.NotifyAsync("exit"); } catch { /* Ignore exit notification failure */ }
        _jsonRpc.Dispose();
        _tcpClient.Dispose();
        _listener.Stop();
    }
}
