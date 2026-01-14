using System.Text.RegularExpressions;
using System.Text.Json;
using CliWrap;
using Spectre.Console;

namespace lazydotnet.Services;

public enum VersionUpdateType
{
    None,
    Patch,
    Minor,
    Major
}

public enum VersionLock
{
    None,
    Major,
    Minor
}

public partial record NuGetPackageInfo(string Id, string ResolvedVersion, string? LatestVersion)
{
    public bool IsOutdated => LatestVersion != null && LatestVersion != ResolvedVersion;

    public VersionUpdateType GetUpdateType()
    {
        if (!IsOutdated || LatestVersion == null)
            return VersionUpdateType.None;

        var current = ParseVersion(ResolvedVersion);
        var latest = ParseVersion(LatestVersion);

        if (current == null || latest == null)
            return VersionUpdateType.Major;

        if (latest.Value.IsPreRelease)
            return VersionUpdateType.Major;

        if (latest.Value.Major > current.Value.Major)
            return VersionUpdateType.Major;
        if (latest.Value.Minor > current.Value.Minor)
            return VersionUpdateType.Minor;
        if (latest.Value.Patch > current.Value.Patch)
            return VersionUpdateType.Patch;

        return VersionUpdateType.None;
    }

    private static (int Major, int Minor, int Patch, bool IsPreRelease)? ParseVersion(string version)
    {
        var match = VersionRegex().Match(version);
        if (!match.Success)
            return null;

        var major = int.Parse(match.Groups[1].Value);
        var minor = int.Parse(match.Groups[2].Value);
        var patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        var isPreRelease = version.Contains('-');

        return (major, minor, patch, isPreRelease);
    }

    [GeneratedRegex(@"^(\d+)\.(\d+)(?:\.(\d+))?(?:[\.\-].*)?$")]
    private static partial Regex VersionRegex();
}

public class NuGetService(EasyDotnetService easyDotnetService)
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<SearchResult>> SearchPackagesAsync(string query, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {
            var results = await easyDotnetService.SearchPackagesAsync(query, ct: ct);
            var list = new List<SearchResult>();
            await foreach (var item in results.WithCancellation(ct))
            {
                list.Add(new SearchResult
                {
                    Id = item.Id,
                    LatestVersion = item.Version,
                    Description = item.Description,
                    Versions = [new SearchVersion { Version = item.Version }]
                });
            }
            return list;
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger?.Invoke($"[red]Search error: {Markup.Escape(ex.Message)}[/]");
            return [];
        }
    }

    public async Task<List<string>> GetPackageVersionsAsync(string packageId, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {
            var command = Cli.Wrap("dotnet")
                .WithArguments(["package", "search", packageId, "--exact-match", "--format", "json", "--prerelease"])
                .WithValidation(CommandResultValidation.None);

            var result = await AppCli.RunBufferedAsync(command, ct);
            if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                logger?.Invoke($"[red]Error searching for versions: {Markup.Escape(result.StandardError)}[/]");
                return [];
            }

            var output = JsonSerializer.Deserialize<DotnetPackageSearchOutput>(result.StandardOutput, _jsonSerializerOptions);

            if (output?.SearchResult == null) return [];

            var versions = output.SearchResult
                .SelectMany(s => s.Packages ?? [])
                .Where(p => p.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Version)
                .Distinct()
                .OrderByDescending(v => v, new VersionComparer())
                .ToList();

            return versions;
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger?.Invoke($"[red]Version search error: {Markup.Escape(ex.Message)}[/]");
            return [];
        }
    }

    private sealed record DotnetPackageSearchOutput(List<DotnetPackageSearchSource> SearchResult);
    private sealed record DotnetPackageSearchSource(string SourceName, List<DotnetPackageSearchPackage>? Packages);
    private sealed record DotnetPackageSearchPackage(string Id, string Version);

    public async Task<List<NuGetPackageInfo>> GetPackagesAsync(string projectPath, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {
            var command = Cli.Wrap("dotnet")
                .WithArguments($"list \"{projectPath}\" package --format json")
                .WithValidation(CommandResultValidation.None);

            var result = await AppCli.RunBufferedAsync(command, ct);

            if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                logger?.Invoke($"[red]Error listing packages: {Markup.Escape(result.StandardError)}[/]");
                return [];
            }

            var output = JsonSerializer.Deserialize<DotnetListPackageOutput>(result.StandardOutput, _jsonSerializerOptions);

            if (output?.Projects == null) return [];

            return output.Projects
                .Where(p => p is { Frameworks: not null })
                .SelectMany(p => (p.Frameworks ?? []).SelectMany(f => f.TopLevelPackages ?? []))
                .GroupBy(p => new { p.Id, p.ResolvedVersion })
                .Select(g => new NuGetPackageInfo(g.Key.Id ?? "", g.Key.ResolvedVersion ?? "", null))
                .OrderBy(p => p.Id)
                .ToList();
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger?.Invoke($"[red]Error loading packages: {Markup.Escape(ex.Message)}[/]");
            return [];
        }
    }

    public async Task<Dictionary<string, string>> GetLatestVersionsAsync(string projectPath, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {
            var command = Cli.Wrap("dotnet")
                .WithArguments($"list \"{projectPath}\" package --outdated --format json")
                .WithValidation(CommandResultValidation.None);

            var result = await AppCli.RunBufferedAsync(command, ct);

            if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                logger?.Invoke($"[red]Error fetching latest versions: {Markup.Escape(result.StandardError)}[/]");
                return [];
            }

            var output = JsonSerializer.Deserialize<DotnetListOutdatedOutput>(result.StandardOutput, _jsonSerializerOptions);

            if (output?.Projects == null) return [];

            var outdated = new Dictionary<string, string>();
            foreach (var p in output.Projects.Where(p => p is { Frameworks: not null }))
            {
                foreach (var f in p.Frameworks ?? [])
                {
                    if (f.TopLevelPackages == null) continue;
                    foreach (var pkg in f.TopLevelPackages)
                    {
                        if (pkg is { Id: not null, LatestVersion: not null })
                        {
                            outdated[pkg.Id] = pkg.LatestVersion;
                        }
                    }
                }
            }
            return outdated;
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger?.Invoke($"[yellow]Warning: Could not fetch latest versions: {ex.Message}[/]");
            return [];
        }
    }

    private sealed record DotnetListPackageOutput(List<DotnetListPackageProject>? Projects);
    private sealed record DotnetListPackageProject(string? Path, List<DotnetListPackageFramework>? Frameworks);
    private sealed record DotnetListPackageFramework(string? Framework, List<PackageReference>? TopLevelPackages);

    private sealed record DotnetListOutdatedOutput(List<DotnetListOutdatedProject>? Projects);
    private sealed record DotnetListOutdatedProject(string? Path, List<DotnetListOutdatedFramework>? Frameworks);
    private sealed record DotnetListOutdatedFramework(string? Framework, List<DotnetListOutdatedPackage>? TopLevelPackages);
    private sealed record DotnetListOutdatedPackage(string? Id, string? ResolvedVersion, string? LatestVersion);

    public static async Task InstallPackageAsync(string projectPath, string packageId, string? version = null, bool noRestore = false, Action<string>? logger = null)
    {
        var args = new List<string> { "add", projectPath, "package", packageId };

        if (!string.IsNullOrEmpty(version))
        {
            args.Add("-v");
            args.Add(version);
        }

        if (noRestore)
        {
            args.Add("--no-restore");
        }

        var command = Cli.Wrap("dotnet")
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(s => logger?.Invoke(Markup.Escape(s))))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(s => logger?.Invoke($"[red]{Markup.Escape(s)}[/]")));

        await AppCli.RunAsync(command);
    }

    public static async Task UpdatePackageAsync(string projectPath, string packageId, string version, bool noRestore = false, Action<string>? logger = null)
    {
        var args = new List<string> { "outdated", projectPath, "-u:Auto", "--include", packageId };

        if (!string.IsNullOrEmpty(version))
        {
            args.Add("--maximum-version");
            args.Add(version);
        }

        if (!string.IsNullOrEmpty(version) && version.Contains('-'))
        {
            args.Add("--pre-release");
            args.Add("Always");
        }
        else
        {
            args.Add("--pre-release");
            args.Add("Auto");
        }

        if (noRestore)
        {
            args.Add("--no-restore");
        }

        var command = Cli.Wrap("dotnet")
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(s => logger?.Invoke(Markup.Escape(s))))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(s => logger?.Invoke($"[red]{Markup.Escape(s)}[/]")));

        await AppCli.RunAsync(command);
    }

    public static async Task UpdateAllPackagesAsync(string projectPath, VersionLock versionLock, bool noRestore = false, Action<string>? logger = null)
    {
        var args = new List<string> { "outdated", projectPath, "-u:Auto" };

        if (noRestore)
        {
            args.Add("--no-restore");
        }

        args.Add("--pre-release");
        args.Add("Auto");

        if (versionLock != VersionLock.None)
        {
            args.Add("--version-lock");
            args.Add(versionLock.ToString());
        }

        var command = Cli.Wrap("dotnet")
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(s => logger?.Invoke(Markup.Escape(s))))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(s => logger?.Invoke($"[red]{Markup.Escape(s)}[/]")));

        await AppCli.RunAsync(command);
    }

    public static async Task RemovePackageAsync (string projectPath, string packageId, Action<string>? logger = null)
    {
        var command = Cli.Wrap("dotnet")
            .WithArguments($"remove \"{projectPath}\" package \"{packageId}\"")
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(s => logger?.Invoke(Markup.Escape(s))))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(s => logger?.Invoke($"[red]{Markup.Escape(s)}[/]")));

        await AppCli.RunAsync(command);
    }
}

public class SearchResult
{
    public string Id { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public string? Description { get; set; }
    public List<SearchVersion> Versions { get; set; } = [];
}

public class SearchVersion
{
    public string Version { get; set; } = "";
}

public class VersionComparer : IComparer<string?>
{
    public int Compare(string? x, string? y)
    {
        if (Version.TryParse(x, out var v1) && Version.TryParse(y, out var v2))
            return v1.CompareTo(v2);
        return string.Compare(x, y, StringComparison.Ordinal);
    }
}
