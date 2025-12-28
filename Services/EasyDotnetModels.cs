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

// NuGet Models from easy-dotnet-server
public record NugetPackageMetadata(
    string Source,
    string Id,
    string Version,
    string? Authors,
    string? Description,
    long? DownloadCount,
    Uri? LicenseUrl,
    IReadOnlyList<string> Owners,
    Uri? ProjectUrl,
    Uri? ReadmeUrl,
    string? Summary,
    IReadOnlyList<string> Tags,
    string? Title,
    bool PrefixReserved,
    bool IsListed
);

public record OutdatedDependencyInfoResponse(
    string Name,
    string CurrentVersion,
    string LatestVersion,
    string TargetFramework,
    bool IsOutdated,
    bool IsTransitive,
    string UpgradeSeverity
);

public record PackageReference(
    string Id,
    string RequestedVersion,
    string ResolvedVersion
);

public record DotnetProjectV1(
    string ProjectName,
    string Language,
    string? OutputPath,
    string? OutputType,
    string? TargetExt,
    string? AssemblyName,
    string? TargetFramework,
    string[]? TargetFrameworks,
    bool IsTestProject,
    bool IsWebProject,
    bool IsWorkerProject,
    string? UserSecretsId,
    bool TestingPlatformDotnetTestSupport,
    bool IsTestingPlatformApplication,
    string? TargetPath,
    bool GeneratePackageOnBuild,
    bool IsPackable,
    string? LangVersion,
    string? RootNamespace,
    string? PackageId,
    string? NugetVersion,
    string? Version,
    string? PackageOutputPath,
    bool IsMultiTarget,
    bool IsNetFramework,
    bool UseIISExpress,
    string RunCommand,
    string BuildCommand,
    string TestCommand,
    bool IsAspireHost
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
