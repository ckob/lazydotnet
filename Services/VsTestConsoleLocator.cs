using System.Diagnostics;
using NuGet.Versioning;

namespace lazydotnet.Services;

public static class VsTestConsoleLocator
{
    private static string? _cachedPath;
    private static bool _hasSearched;

    public static string? GetVsTestConsolePath()
    {
        if (_hasSearched) return _cachedPath;

        try
        {
            var sdkPath = GetLatestSdkPath();
            if (sdkPath != null)
            {
                var vstestPath = Path.Combine(sdkPath, "vstest.console.dll");
                if (File.Exists(vstestPath))
                {
                    _cachedPath = vstestPath;
                }
            }
        }
        catch
        {
            _cachedPath = null;
        }

        _hasSearched = true;
        return _cachedPath;
    }

    private static string? GetLatestSdkPath()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "--list-sdks",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? null : GetSdkPathWithHighestVersion(lines);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetSdkPathWithHighestVersion(string[] lines)
    {
        return lines
            .Select(line =>
            {
                var parts = line.Split(' ');
                if (parts.Length < 2) return null;
                var versionStr = parts[0];
                var path = line[(line.IndexOf('[') + 1)..].TrimEnd(']');

                return SemanticVersion.TryParse(versionStr, out var version)
                    ? new { Version = version, Path = Path.Combine(path, versionStr) }
                    : null;
            })
            .Where(x => x != null)
            .OrderByDescending(x => x!.Version)
            .FirstOrDefault()
            ?.Path;
    }
}
