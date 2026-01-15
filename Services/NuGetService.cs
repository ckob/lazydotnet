using System.Text.RegularExpressions;
using System.Text.Json;
using CliWrap;
using Spectre.Console;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Microsoft.Build.Evaluation;

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

public class NuGetService
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<SearchResult>> SearchPackagesAsync(string query, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {
            var provider = Repository.Provider.GetCoreV3();
            var sourceProvider = new PackageSourceProvider(Settings.LoadDefaultSettings(null));
            var allSources = sourceProvider.LoadPackageSources().Where(s => s.IsEnabled);

            var tasks = allSources.Select(async source =>
            {
                try
                {
                    var repo = new SourceRepository(source, provider);
                    var search = await repo.GetResourceAsync<PackageSearchResource>(ct);

                    return await search.SearchAsync(
                        query,
                        new SearchFilter(includePrerelease: false),
                        skip: 0,
                        take: 20,
                        log: NullLogger.Instance,
                        cancellationToken: ct);
                }
                catch
                {
                    return Enumerable.Empty<IPackageSearchMetadata>();
                }
            });

            var results = await Task.WhenAll(tasks);
            return results.SelectMany(r => r)
                .GroupBy(p => p.Identity.Id)
                .Select(g =>
                {
                    var p = g.First();
                    return new SearchResult
                    {
                        Id = p.Identity.Id,
                        LatestVersion = p.Identity.Version.ToNormalizedString(),
                        Description = p.Description,
                        Versions = [new SearchVersion { Version = p.Identity.Version.ToNormalizedString() }]
                    };
                })
                .ToList();
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
            var cache = new SourceCacheContext();
            var sourceProvider = new PackageSourceProvider(Settings.LoadDefaultSettings(null));
            var allSources = sourceProvider.LoadPackageSources().Where(s => s.IsEnabled);

            var tasks = allSources.Select(async source =>
            {
                try
                {
                    var repo = Repository.Factory.GetCoreV3(source.Source);
                    var resource = await repo.GetResourceAsync<FindPackageByIdResource>(ct);
                    var versions = await resource.GetAllVersionsAsync(packageId, cache, NullLogger.Instance, ct);
                    return versions.ToList();
                }
                catch
                {
                    return [];
                }
            });

            var results = await Task.WhenAll(tasks);
            return results.SelectMany(v => v)
                .Distinct()
                .OrderByDescending(v => v)
                .Select(v => v.ToNormalizedString())
                .ToList();
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger?.Invoke($"[red]Version search error: {Markup.Escape(ex.Message)}[/]");
            return [];
        }
    }

    public Task<List<NuGetPackageInfo>> GetPackagesAsync(string projectPath, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {
            var projectCollection = new ProjectCollection();
            var project = projectCollection.LoadProject(projectPath);
            var packages = project.GetItems("PackageReference")
                .Select(i => new NuGetPackageInfo(
                    i.EvaluatedInclude,
                    i.GetMetadataValue("Version"),
                    null))
                .OrderBy(p => p.Id)
                .ToList();

            projectCollection.UnloadAllProjects();
            return Task.FromResult(packages);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger?.Invoke($"[red]Error loading packages: {Markup.Escape(ex.Message)}[/]");
            return Task.FromResult(new List<NuGetPackageInfo>());
        }
    }

    public async Task<Dictionary<string, string>> GetLatestVersionsAsync(string projectPath, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {
            var packages = await GetPackagesAsync(projectPath, logger, ct);
            var outdated = new Dictionary<string, string>();

            var tasks = packages.Select(async pkg =>
            {
                var versions = await GetPackageVersionsAsync(pkg.Id, null, ct);
                var latest = versions.FirstOrDefault();
                if (latest != null && latest != pkg.ResolvedVersion)
                {
                    lock (outdated)
                    {
                        outdated[pkg.Id] = latest;
                    }
                }
            });

            await Task.WhenAll(tasks);
            return outdated;
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger?.Invoke($"[yellow]Warning: Could not fetch latest versions: {ex.Message}[/]");
            return [];
        }
    }

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
