using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Text.RegularExpressions;
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

public record NuGetPackageInfo(string Id, string ResolvedVersion, string? LatestVersion)
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

        var match = Regex.Match(version, @"^(\d+)\.(\d+)(?:\.(\d+))?(?:[\.\-].*)?$");
        if (!match.Success)
            return null;

        int major = int.Parse(match.Groups[1].Value);
        int minor = int.Parse(match.Groups[2].Value);
        int patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        bool isPreRelease = version.Contains('-');

        return (major, minor, patch, isPreRelease);
    }
}

public class NuGetReport
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("projects")]
    public List<NuGetProject> Projects { get; set; } = new();
}

public class NuGetProject
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("frameworks")]
    public List<NuGetFramework> Frameworks { get; set; } = new();
}

public class NuGetFramework
{
    [JsonPropertyName("framework")]
    public string Framework { get; set; } = "";

    [JsonPropertyName("topLevelPackages")]
    public List<NuGetPackageJson> TopLevelPackages { get; set; } = new();
}

public class NuGetPackageJson
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("requestedVersion")]
    public string RequestedVersion { get; set; } = "";

    [JsonPropertyName("resolvedVersion")]
    public string ResolvedVersion { get; set; } = "";

    [JsonPropertyName("latestVersion")]
    public string? LatestVersion { get; set; }
}

public class NuGetService
{
    public async Task<List<SearchResult>> SearchPackagesAsync(string query, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {
            var cmd = $"package search \"{Markup.Escape(query)}\" --take 20 --format json";

            var command = Cli.Wrap("dotnet")
                .WithArguments($"package search \"{query}\" --take 20 --format json")
                .WithValidation(CommandResultValidation.None);

            var result = await AppCli.RunBufferedAsync(command, ct);

            if (result.ExitCode != 0)
            {
                logger?.Invoke($"[red]Search failed (ExitCode {result.ExitCode}): {Markup.Escape(result.StandardError)}[/]");
            }

            return ParseSearchResults(result.StandardOutput);
        }
        catch (Exception ex)
        {
            logger?.Invoke($"[red]Search error: {Markup.Escape(ex.Message)}[/]");
            return new List<SearchResult>();
        }
    }

    public async Task<List<string>> GetPackageVersionsAsync(string packageId, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {
            var cmd = $"package search \"{Markup.Escape(packageId)}\" --exact-match --take 100 --format json";

            var command = Cli.Wrap("dotnet")
                .WithArguments($"package search \"{packageId}\" --exact-match --take 100 --format json")
                .WithValidation(CommandResultValidation.None);

            var result = await AppCli.RunBufferedAsync(command, ct);

             if (result.ExitCode != 0)
            {
                logger?.Invoke($"[red]Version search failed: {Markup.Escape(result.StandardError)}[/]");
            }

            var searchResults = ParseSearchResults(result.StandardOutput);
            var package = searchResults.FirstOrDefault(p => p.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
            
            return package?.Versions.Select(v => v.Version).ToList() ?? new List<string>();
        }
        catch (Exception ex)
        {
            logger?.Invoke($"[red]Version search error: {Markup.Escape(ex.Message)}[/]");
            return new List<string>();
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


        var displayArgs = new StringBuilder($"add \"{projectPath}\" package \"{packageId}\"");
        if (!string.IsNullOrEmpty(version)) displayArgs.Append($" -v {version}");
        if (noRestore) displayArgs.Append(" --no-restore");



        var command = Cli.Wrap("dotnet")
            .WithArguments(args)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(s => logger?.Invoke(Markup.Escape(s))))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(s => logger?.Invoke($"[red]{Markup.Escape(s)}[/]")));

        await AppCli.RunAsync(command);
    }

    public async Task RemovePackageAsync(string projectPath, string packageId, Action<string>? logger = null)
    {
        var cmd = $"remove \"{projectPath}\" package \"{packageId}\"";

        
        var command = Cli.Wrap("dotnet")
            .WithArguments($"remove \"{projectPath}\" package \"{packageId}\"")
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(s => logger?.Invoke(Markup.Escape(s))))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(s => logger?.Invoke($"[red]{Markup.Escape(s)}[/]")));

        await AppCli.RunAsync(command);
    }


    private List<SearchResult> ParseSearchResults(string output)
    {
        int jsonStart = output.IndexOf('{');
        if (jsonStart < 0) return new List<SearchResult>();

        var jsonContent = output.Substring(jsonStart);
        try 
        {
            var report = JsonSerializer.Deserialize<SearchReport>(jsonContent);
            if (report?.SearchResult == null) return new List<SearchResult>();

            // Flatten all packages from all sources
            var allPackages = report.SearchResult
                .SelectMany(s => s.Packages)
                .ToList();

            var grouped = allPackages
                .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => new SearchResult
                {
                    Id = g.Key,
                    LatestVersion = g.MaxBy(p => p.LatestVersion ?? p.Version, new VersionComparer())?.LatestVersion ?? g.First().LatestVersion ?? g.First().Version ?? "",
                    Versions = g.Select(p => new SearchVersion { Version = p.Version ?? "" }).Where(v => !string.IsNullOrEmpty(v.Version)).ToList()
                })
                .ToList();

            return grouped;
        }
        catch
        {
            return new List<SearchResult>();
        }
    }

    public async Task<List<NuGetPackageInfo>> GetPackagesAsync(string projectPath, Action<string>? logger = null, CancellationToken ct = default)
    {
        try
        {

            var listCmdDisplay = $"list \"{Markup.Escape(projectPath)}\" package --format json";
            var outdatedCmdDisplay = $"list \"{Markup.Escape(projectPath)}\" package --format json --outdated";
            

            
            var allPackagesCmd = Cli.Wrap("dotnet")
                .WithArguments($"list \"{projectPath}\" package --format json")
                .WithValidation(CommandResultValidation.None);

            var outdatedCmd = Cli.Wrap("dotnet")
                .WithArguments($"list \"{projectPath}\" package --format json --outdated")
                .WithValidation(CommandResultValidation.None);

            var allPackagesTask = AppCli.RunBufferedAsync(allPackagesCmd, ct);
            var outdatedTask = AppCli.RunBufferedAsync(outdatedCmd, ct);


            await Task.WhenAll(allPackagesTask, outdatedTask);


            var allPackages = ParseReport(allPackagesTask.Result.StandardOutput);
            var outdatedPackages = ParseReport(outdatedTask.Result.StandardOutput);


            var outdatedDict = outdatedPackages.ToDictionary(p => p.Id, p => p.LatestVersion);

            return allPackages.Select(p => new NuGetPackageInfo(
                p.Id,
                p.ResolvedVersion,
                outdatedDict.TryGetValue(p.Id, out var latest) ? latest : null
            )).ToList();
        }
        catch
        {
            return new List<NuGetPackageInfo>();
        }
    }

    private List<NuGetPackageInfo> ParseReport(string output)
    {
        int jsonStart = output.IndexOf('{');
        if (jsonStart < 0)
            return new List<NuGetPackageInfo>();

        var jsonContent = output.Substring(jsonStart);

        var report = JsonSerializer.Deserialize<NuGetReport>(jsonContent);
        if (report == null)
            return new List<NuGetPackageInfo>();

        var packages = new List<NuGetPackageInfo>();
        foreach (var project in report.Projects)
        {
            foreach (var framework in project.Frameworks)
            {
                foreach (var pkg in framework.TopLevelPackages)
                {
                    if (!packages.Any(p => p.Id == pkg.Id))
                    {
                        packages.Add(new NuGetPackageInfo(pkg.Id, pkg.ResolvedVersion, pkg.LatestVersion));
                    }
                }
            }
        }
        return packages;
    }
}

public class SearchResult
{
    public string Id { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public string? Description { get; set; }
    public List<SearchVersion> Versions { get; set; } = new();
}

public class SearchReport
{
    [JsonPropertyName("searchResult")]
    public List<SearchSourceResult> SearchResult { get; set; } = new();
}

public class SearchSourceResult
{
    [JsonPropertyName("sourceName")]
    public string SourceName { get; set; } = "";

    [JsonPropertyName("packages")]
    public List<JsonPackageResult> Packages { get; set; } = new();
}

public class JsonPackageResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("latestVersion")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class SearchVersion
{
    public string Version { get; set; } = "";
    public long Downloads { get; set; }
}

public class VersionComparer : IComparer<string?>
{
    public int Compare(string? x, string? y)
    {

        if (System.Version.TryParse(x, out var v1) && System.Version.TryParse(y, out var v2))
            return v1.CompareTo(v2);
        return string.Compare(x, y, StringComparison.Ordinal);
    }
}

