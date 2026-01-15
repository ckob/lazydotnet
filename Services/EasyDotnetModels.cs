namespace lazydotnet.Services;

public record SolutionFileProjectResponse(
    string ProjectName,
    string AbsolutePath
);

public record ClientInfo(string Name, string? Version);
public record ProjectInitializeInfo(string RootDir, string? SolutionFile);
public record DebuggerOptions(string? BinaryPath = null, bool ApplyValueConverters = false);
public record ClientOptions(DebuggerOptions? DebuggerOptions = null, bool UseVisualStudio = false);
public record InitializeRequest(ClientInfo ClientInfo, ProjectInitializeInfo ProjectInfo, ClientOptions? Options);

public record PackageReference(
    string Id,
    string RequestedVersion,
    string ResolvedVersion
);

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
