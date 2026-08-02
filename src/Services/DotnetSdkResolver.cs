using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using NuGet.Versioning;

namespace lazydotnet.Services;

public static class DotnetSdkResolver
{
    private static string? _cachedSdkPath;

    public static string GetRequiredSdkVersionPath()
    {
        var path = GetLatestSdkPath();
        if (string.IsNullOrEmpty(path))
        {
            throw new InvalidOperationException("Could not find any installed .NET SDKs via 'dotnet --list-sdks'.");
        }
        return path;
    }

    [SuppressMessage("Security", "S4036:Use an absolute path for this command", Justification = "dotnet command is resolved via system PATH.")]
    public static string? GetLatestSdkPath()
    {
        if (_cachedSdkPath != null) return _cachedSdkPath;

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
            _cachedSdkPath = lines.Length == 0 ? null : ResolveHighestVersionPath(lines);
            return _cachedSdkPath;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveHighestVersionPath(string[] lines)
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