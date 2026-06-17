using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

using lazydotnet.UI;

namespace lazydotnet.Services;

public static class UpdateCheckerService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

#pragma warning disable S1075 // Hardcoded URI is the official public NuGet API endpoint
    private const string NuGetUrl = "https://api.nuget.org/v3-flatcontainer/lazydotnet/index.json";
#pragma warning restore S1075
    private const string CacheFileName = "last_update_check.txt";
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromHours(24);
    private static readonly string CacheFilePath = Path.Combine(
        Core.EnvironmentPaths.GetDataDirectory(),
        CacheFileName
    );

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static string GetCurrentVersion()
    {
        var rawVersion = ThisAssembly.Info.InformationalVersion;

        var noBuildMeta = rawVersion.Split('+')[0];

        var cleanVersion = noBuildMeta.Split('-')[0];

        return cleanVersion;
    }

    private static void TryNotifyUpdate(string availableVersion)
    {
        if (!Version.TryParse(GetCurrentVersion(), out var current) ||
            !Version.TryParse(availableVersion, out var available) ||
            available <= current)
        {
            return;
        }

        AppLayout.UpdateAvailableVersion ??= availableVersion;
        AppCli.Log($"[yellow]Update available: v{current} -> v{available}. " +
                   $"Run: dotnet tool update -g lazydotnet[/]");
    }

    public static async Task StartFireAndForgetCheckAsync()
    {
        try
        {
            var cachedVersion = await ReadCacheAsync();

            if (cachedVersion != null)
            {
                TryNotifyUpdate(cachedVersion);
                return;
            }

            await PerformUpdateCheckAsync();
        }
        catch
        {
            // Exception swallowed by design
        }
    }

    private static async Task<string?> ReadCacheAsync()
    {
        try
        {
            if (!File.Exists(CacheFilePath))
            {
                return null;
            }

            var content = (await File.ReadAllTextAsync(CacheFilePath)).Trim();
            var parts = content.Split('|');
            if (parts.Length != 2)
            {
                return null;
            }

            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            {
                return null;
            }

            if (DateTime.UtcNow - timestamp >= CacheMaxAge)
            {
                return null;
            }

            return parts[1];
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteCacheAsync(string? version)
    {
        try
        {
            var dir = Path.GetDirectoryName(CacheFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(CacheFilePath, $"{DateTime.UtcNow:o}|{version ?? string.Empty}");
        }
        catch
        {
            // Exception swallowed by design - cache write failure should not crash TUI
        }
    }

    private static async Task PerformUpdateCheckAsync()
    {
        try
        {
            var response = await HttpClient.GetAsync(NuGetUrl);
            response.EnsureSuccessStatusCode();

            var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(JsonOptions);
            var root = doc?.RootElement;

            if (root is null || !root.Value.TryGetProperty("versions", out var versions))
            {
                return;
            }

            string? latestStableVersion = null;
            foreach (var version in versions.EnumerateArray())
            {
                var versionStr = version.GetString() ?? string.Empty;
                if (!versionStr.Contains('-'))
                {
                    latestStableVersion = versionStr;
                }
            }

            if (string.IsNullOrEmpty(latestStableVersion))
            {
                return;
            }

            TryNotifyUpdate(latestStableVersion);
            await WriteCacheAsync(latestStableVersion);
        }
        catch
        {
            // Exception swallowed by design - HTTP failure or parse error must not crash TUI
        }
    }
}
