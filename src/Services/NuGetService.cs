using CliWrap;
using Spectre.Console;
using NuGet.Versioning;
using System.Text.Json;
using lazydotnet.Core;

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

public record PackageProjectInfo(string ProjectPath, string ResolvedVersion);

public record NuGetPackageInfo(string Id, List<PackageProjectInfo> Projects, string? LatestVersion)
{
    public string ResolvedVersion => Projects.Count > 0
        ? string.Join(", ", Projects.Select(p => p.ResolvedVersion).Distinct().OrderBy(v => v))
        : "";

    public string? PrimaryVersion => Projects.Count > 0 ? Projects[0].ResolvedVersion : null;

    public bool IsOutdated => LatestVersion != null && Projects.Any(p => p.ResolvedVersion != LatestVersion);

    public bool IsVersionOutdated(string version) => LatestVersion != null && version != LatestVersion;

    public VersionUpdateType GetUpdateType()
    {
        if (!IsOutdated || LatestVersion == null || string.IsNullOrEmpty(PrimaryVersion))
            return VersionUpdateType.None;

        if (!NuGetVersion.TryParse(PrimaryVersion, out var current) ||
            !NuGetVersion.TryParse(LatestVersion, out var latest))
        {
            return VersionUpdateType.Major;
        }

        if (latest.IsPrerelease || current.IsPrerelease)
            return VersionUpdateType.Major;

        if (latest.Major > current.Major)
            return VersionUpdateType.Major;
        if (latest.Minor > current.Minor)
            return VersionUpdateType.Minor;
        if (latest.Patch > current.Patch)
            return VersionUpdateType.Patch;

        return VersionUpdateType.None;
    }

    public List<string> GetProjectsToUpdate(string targetVersion)
    {
        if (!NuGetVersion.TryParse(targetVersion, out var target))
            return [];

        return Projects
            .Where(p => NuGetVersion.TryParse(p.ResolvedVersion, out var current) && current != target)
            .Select(p => p.ProjectPath)
            .ToList();
    }
}

public static class NuGetService
{
    private const string DotnetBaseCommand = "dotnet";

    public static async Task<List<SearchResult>> SearchPackagesAsync(string query, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {
            var command = Cli.Wrap(DotnetBaseCommand)
                .WithArguments(["package", "search", query, "--format", "json"])
                .WithValidation(CommandResultValidation.None);

            var result = await AppCli.RunBufferedAsync(command, ct);
            if (string.IsNullOrEmpty(result.StandardOutput))
            {
                return [];
            }

            var data = JsonSerializer.Deserialize<DotnetPackageSearchOutput>(result.StandardOutput, JsonOptions);
            if (data?.SearchResult == null) return [];

            return [.. data.SearchResult
                .SelectMany(sr => sr.Packages ?? [])
                .Where(p => !string.IsNullOrEmpty(p.Id))
                .GroupBy(p => p.Id!)
                .Select(g =>
                {
                    var p = g.First();
                    return new SearchResult
                    {
                        Id = p.Id!,
                        LatestVersion = p.LatestVersion ?? "",
                        Description = p.Owners,
                        Versions = [new SearchVersion { Version = p.LatestVersion ?? "" }]
                    };
                })];
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger?.Invoke($"[red]Search error: {Markup.Escape(ex.Message)}[/]");
            return [];
        }
    }

    public static async Task<List<string>> GetPackageVersionsAsync(string packageId, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {
            var command = Cli.Wrap(DotnetBaseCommand)
                .WithArguments(["package", "search", packageId, "--exact-match", "--format", "json"])
                .WithValidation(CommandResultValidation.None);

            var result = await AppCli.RunBufferedAsync(command, ct);
            if (string.IsNullOrEmpty(result.StandardOutput))
            {
                return [];
            }

            var data = JsonSerializer.Deserialize<DotnetPackageSearchOutput>(result.StandardOutput, JsonOptions);
            if (data?.SearchResult == null) return [];

            var versions = data.SearchResult
                .SelectMany(sr => sr.Packages ?? [])
                .Select(p => p.Version)
                .Where(v => !string.IsNullOrEmpty(v))
                .Cast<string>()
                .Distinct()
                .ToList();

            var parsedVersions = versions
                .Select(v => new { Original = v, SemVer = NuGetVersion.Parse(v) })
                .OrderByDescending(x => x.SemVer)
                .Select(x => x.Original)
                .ToList();

            return parsedVersions;
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger?.Invoke($"[red]Version search error: {Markup.Escape(ex.Message)}[/]");
            return [];
        }
    }

    public static async Task<List<NuGetPackageInfo>> GetPackagesAsync(string projectPath, Action<string>? logger = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(projectPath))
        {
            return [];
        }

        try
        {
            var relativePath = PathHelper.GetRelativePath(projectPath);
            var command = Cli.Wrap(DotnetBaseCommand)
                .WithArguments(["list", relativePath, "package", "--format", "json"])
                .WithValidation(CommandResultValidation.None);

            var result = await AppCli.RunBufferedAsync(command, ct);
            if (string.IsNullOrEmpty(result.StandardOutput))
            {
                return [];
            }

            var data = JsonSerializer.Deserialize<DotnetListOutput>(result.StandardOutput, JsonOptions);
            if (data?.Projects == null) return [];

            // Group packages by Id across all projects
            var packageGroups = data.Projects
                .SelectMany(p => (p.Frameworks ?? []).SelectMany(f => (f.TopLevelPackages ?? []).Select(pkg => new { Project = p.Path, Package = pkg })))
                .Where(x => !string.IsNullOrEmpty(x.Package.Id) && !string.IsNullOrEmpty(x.Package.ResolvedVersion))
                .GroupBy(x => x.Package.Id!)
                .OrderBy(g => g.Key);

            var packages = new List<NuGetPackageInfo>();
            foreach (var group in packageGroups)
            {
                var projects = group
                    .Select(x => new PackageProjectInfo(x.Project, x.Package.ResolvedVersion!))
                    .ToList();

                packages.Add(new NuGetPackageInfo(group.Key, projects, null));
            }

            return packages;
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger?.Invoke($"[red]Error loading packages: {Markup.Escape(ex.Message)}[/]");
            return [];
        }
    }

    public static async Task<Dictionary<string, string>> GetLatestVersionsAsync(string projectPath, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {
            var relativePath = PathHelper.GetRelativePath(projectPath);
            var command = Cli.Wrap(DotnetBaseCommand)
                .WithArguments(["list", relativePath, "package", "--outdated", "--format", "json"])
                .WithValidation(CommandResultValidation.None);

            var result = await AppCli.RunBufferedAsync(command, ct);
            if (string.IsNullOrEmpty(result.StandardOutput))
            {
                return [];
            }

            var data = JsonSerializer.Deserialize<DotnetListOutput>(result.StandardOutput, JsonOptions);
            if (data?.Projects == null) return [];

            // Get the latest version for each package across all projects
            return data.Projects
                .SelectMany(p => p.Frameworks ?? [])
                .SelectMany(f => f.TopLevelPackages ?? [])
                .Where(pkg => !string.IsNullOrEmpty(pkg.Id) && !string.IsNullOrEmpty(pkg.LatestVersion))
                .GroupBy(pkg => pkg.Id!)
                .ToDictionary(g => g.Key, g => g.First().LatestVersion!);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger?.Invoke($"[yellow]Warning: Could not fetch latest versions: {Markup.Escape(ex.Message)}[/]");
            return [];
        }
    }

    public static async Task InstallPackageAsync(string projectPath, string packageId, string? version = null, bool noRestore = false, Action<string>? logger = null)
    {
        var relativePath = PathHelper.GetRelativePath(projectPath);
        var args = new List<string> { "add", relativePath, "package", packageId };

        if (!string.IsNullOrEmpty(version))
        {
            args.Add("-v");
            args.Add(version);
        }

        if (noRestore)
        {
            args.Add("--no-restore");
        }

        var command = Cli.Wrap(DotnetBaseCommand)
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(s => logger?.Invoke(Markup.Escape(s))))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(s => logger?.Invoke($"[red]{Markup.Escape(s)}[/]")));

        await AppCli.RunAsync(command);
    }

    public static async Task UpdatePackageAsync(string projectPath, string packageId, string targetVersion, bool noRestore = false, Action<string>? logger = null)
    {
        await InstallPackageAsync(projectPath, packageId, targetVersion, noRestore, logger);
    }

    public static async Task UpdateAllPackagesAsync(string projectPath, VersionLock versionLock, bool noRestore = false, Action<string>? logger = null)
    {
        var relativePath = PathHelper.GetRelativePath(projectPath);
        var args = new List<string> { "outdated", relativePath, "-u:Auto" };

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

        var command = Cli.Wrap(DotnetBaseCommand)
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(s => logger?.Invoke(Markup.Escape(s))))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(s => logger?.Invoke($"[red]{Markup.Escape(s)}[/]")));

        await AppCli.RunAsync(command);
    }

    public static async Task RemovePackageAsync (string projectPath, string packageId, Action<string>? logger = null)
    {
        var relativePath = PathHelper.GetRelativePath(projectPath);
        var command = Cli.Wrap(DotnetBaseCommand)
            .WithArguments($"remove \"{relativePath}\" package \"{packageId}\"")
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(s => logger?.Invoke(Markup.Escape(s))))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(s => logger?.Invoke($"[red]{Markup.Escape(s)}[/]")));

        await AppCli.RunAsync(command);
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
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

internal record DotnetListOutput(List<DotnetProject>? Projects);
internal record DotnetProject(string Path, List<DotnetFramework>? Frameworks);
internal record DotnetFramework(string Framework, List<DotnetPackage>? TopLevelPackages);
internal record DotnetPackage(string? Id, string? ResolvedVersion, string? LatestVersion);
internal record DotnetPackageSearchOutput(int Version, List<DotnetSearchResult>? SearchResult);
internal record DotnetSearchResult(string SourceName, List<DotnetSearchPackage>? Packages);
internal record DotnetSearchPackage(
    string? Id,
    string? Version,
    string? LatestVersion,
    long? TotalDownloads,
    string? Owners);
