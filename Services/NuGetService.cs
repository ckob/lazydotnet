using System.Text.RegularExpressions;
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
            var versions = await easyDotnetService.GetPackageVersionsAsync(packageId, ct: ct);
            var list = new List<string>();
            await foreach (var v in versions.WithCancellation(ct))
            {
                list.Add(v);
            }
            return list;
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger?.Invoke($"[red]Version search error: {Markup.Escape(ex.Message)}[/]");
            return [];
        }
    }

    public async Task<List<NuGetPackageInfo>> GetPackagesAsync(string projectPath, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {
            var properties = await easyDotnetService.GetProjectPropertiesAsync(projectPath);
            var tfm = properties.TargetFramework ?? "";

            var packagesTask = await easyDotnetService.ListPackageReferencesAsync(projectPath, tfm, ct: ct);
            var packages = new List<PackageReference>();
            await foreach (var p in packagesTask.WithCancellation(ct))
            {
                packages.Add(p);
            }

            return packages.Select(p => new NuGetPackageInfo(
                p.Id,
                p.ResolvedVersion,
                null
            )).OrderBy(p => p.Id).ToList();
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
            var outdatedTask = await easyDotnetService.GetOutdatedPackagesAsync(projectPath, ct: ct);
            var outdated = new Dictionary<string, string>();
            await foreach (var o in outdatedTask.WithCancellation(ct))
            {
                outdated[o.Name] = o.LatestVersion;
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

    public async Task RemovePackageAsync(string projectPath, string packageId, Action<string>? logger = null)
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
