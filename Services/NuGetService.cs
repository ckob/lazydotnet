using System.Text.RegularExpressions;
using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;
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
    None,   // Allow Major updates
    Major,  // Lock Major (allow Minor/Patch)
    Minor   // Lock Minor (allow Patch)
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

        int major = int.Parse(match.Groups[1].Value);
        int minor = int.Parse(match.Groups[2].Value);
        int patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        bool isPreRelease = version.Contains('-');

        return (major, minor, patch, isPreRelease);
    }

    [GeneratedRegex(@"^(\d+)\.(\d+)(?:\.(\d+))?(?:[\.\-].*)?$")]
    private static partial Regex VersionRegex();
}

public class NuGetService(EasyDotnetService easyDotnetService)
{
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
            if (result.ExitCode != 0)
            {
                if (string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    logger?.Invoke($"[red]Error searching for versions: {Markup.Escape(result.StandardError)}[/]");
                    return [];
                }
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var output = JsonSerializer.Deserialize<DotnetPackageSearchOutput>(result.StandardOutput, options);

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

    private record DotnetPackageSearchOutput(List<DotnetPackageSearchSource> SearchResult);
    private record DotnetPackageSearchSource(string SourceName, List<DotnetPackageSearchPackage>? Packages);
    private record DotnetPackageSearchPackage(string Id, string Version);

        public async Task<List<NuGetPackageInfo>> GetPackagesAsync(string projectPath, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {
            var command = Cli.Wrap("dotnet")
                .WithArguments($"list \"{projectPath}\" package --format json")
                .WithValidation(CommandResultValidation.None);

            var result = await AppCli.RunBufferedAsync(command, ct);
            
            if (result.ExitCode != 0)
            {
                // Some projects might fail to list but we might still get some results or just an error
                // If it's a total failure, result.StandardOutput might be empty or invalid JSON
                if (string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    logger?.Invoke($"[red]Error listing packages: {Markup.Escape(result.StandardError)}[/]");
                    return [];
                }
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var output = JsonSerializer.Deserialize<DotnetListPackageOutput>(result.StandardOutput, options);

            if (output?.Projects == null) return [];

            return output.Projects
                .SelectMany(p => (p.Frameworks ?? []).SelectMany(f => f.TopLevelPackages ?? []))
                .GroupBy(p => new { p.Id, p.ResolvedVersion })
                .Select(g => new NuGetPackageInfo(g.Key.Id, g.Key.ResolvedVersion, null))
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

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var output = JsonSerializer.Deserialize<DotnetListOutdatedOutput>(result.StandardOutput, options);

            if (output?.Projects == null) return [];

            var outdated = new Dictionary<string, string>();
            foreach (var p in output.Projects)
            {
                if (p.Frameworks == null) continue;
                foreach (var f in p.Frameworks)
                {
                    if (f.TopLevelPackages == null) continue;
                    foreach (var pkg in f.TopLevelPackages)
                    {
                        outdated[pkg.Id] = pkg.LatestVersion;
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

    private record DotnetListPackageOutput(List<DotnetListPackageProject> Projects);
    private record DotnetListPackageProject(string Path, List<DotnetListPackageFramework> Frameworks);
    private record DotnetListPackageFramework(string Framework, List<PackageReference>? TopLevelPackages);

    private record DotnetListOutdatedOutput(List<DotnetListOutdatedProject> Projects);
    private record DotnetListOutdatedProject(string Path, List<DotnetListOutdatedFramework> Frameworks);
    private record DotnetListOutdatedFramework(string Framework, List<DotnetListOutdatedPackage>? TopLevelPackages);
    private record DotnetListOutdatedPackage(string Id, string ResolvedVersion, string LatestVersion);

        public async Task InstallPackageAsync(string projectPath, string packageId, string? version = null, bool noRestore = false, Action<string>? logger = null)
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

    public async Task UpdatePackageAsync(string projectPath, string packageId, string version, bool noRestore = false, Action<string>? logger = null)
    {
        // Use dotnet outdated for safe updates (handling CPM)
        // Try to update using dotnet outdated first
        var args = new List<string> { "outdated", projectPath, "-u:Auto", "--include", packageId };

        if (!string.IsNullOrEmpty(version))
        {
            // If version is specified, we try to use --maximum-version. 
            // Note: This only works if 'version' is higher than current. 
            // If downgrading, this won't work, but dotnet outdated is the requested tool.
            args.Add("--maximum-version");
            args.Add(version);
        }

        // If version is a pre-release (contains '-'), we must allow pre-release
        if (!string.IsNullOrEmpty(version) && version.Contains('-'))
        {
            args.Add("--pre-release");
            args.Add("Always");
        }
        else
        {
            // Default to Auto to respect project settings, or Always if users generally want latest pre-release?
            // "Auto" is safer.
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

    public async Task UpdateAllPackagesAsync(string projectPath, VersionLock versionLock, bool noRestore = false, Action<string>? logger = null)
    {
        var args = new List<string> { "outdated", projectPath, "-u:Auto" };

        if (noRestore)
        {
            args.Add("--no-restore");
        }
        
        // Use Auto for pre-release (default)
        args.Add("--pre-release");
        args.Add("Auto");

        // Apply version lock
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

    public async Task RemovePackageAsync
(string projectPath, string packageId, Action<string>? logger = null)
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
        if (Version.TryParse(x, out var v1) && System.Version.TryParse(y, out var v2))
            return v1.CompareTo(v2);
        return string.Compare(x, y, StringComparison.Ordinal);
    }
}
