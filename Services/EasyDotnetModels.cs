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
