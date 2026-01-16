using System.Diagnostics;

namespace lazydotnet.Services;

public static class VsTestConsoleLocator
{
    public static string? GetVsTestConsolePath()
    {
        try
        {
            var sdkPath = GetLatestSdkPath();
            if (sdkPath == null) return null;

            var vstestPath = Path.Combine(sdkPath, "vstest.console.dll");
            return File.Exists(vstestPath) ? vstestPath : null;
        }
        catch
        {
            return null;
        }
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
                var version = parts[0];
                var path = line[(line.IndexOf('[') + 1)..].TrimEnd(']');
                return new { Version = version, Path = Path.Combine(path, version) };
            })
            .Where(x => x != null)
            .OrderByDescending(x => x!.Version)
            .FirstOrDefault()
            ?.Path;
    }
}
