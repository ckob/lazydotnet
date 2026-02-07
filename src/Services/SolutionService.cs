using System.Collections.Concurrent;
using Microsoft.Build.Construction;

namespace lazydotnet.Services;

public record ProjectInfo
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Id { get; init; }
    public bool IsRunnable { get; set; }
    public bool IsRunning { get; set; }
}

public record SolutionInfo(string Name, string Path, List<ProjectInfo> Projects, bool IsSlnx = false, bool IsSlnf = false);

public class SolutionService
{
    private const string SlnFileExtension = ".sln";
    private const string SlnxFileExtension = ".slnx";
    private const string SlnfFileExtension = ".slnf";
    private const string CsprojFileExtension = ".csproj";

    public SolutionInfo? CurrentSolution { get; private set; }

    public async Task<SolutionInfo?> FindAndParseSolutionAsync(string path)
    {
        string? solutionFile = null;
        string? rootDir = null;

        if (Directory.Exists(path))
        {
            solutionFile = Directory.GetFiles(path, $"*{SlnFileExtension}").FirstOrDefault()
                           ?? Directory.GetFiles(path, $"*{SlnxFileExtension}").FirstOrDefault()
                           ?? Directory.GetFiles(path, $"*{SlnfFileExtension}").FirstOrDefault();
            rootDir = Path.GetFullPath(path);

            if (solutionFile == null)
            {
                var csproj = Directory.GetFiles(path, $"*{CsprojFileExtension}").FirstOrDefault();
                if (csproj != null)
                {
                    return await ParseProjectAsSolutionAsync(csproj);
                }
            }
        }
        else if (File.Exists(path))
        {
            if (path.EndsWith(SlnFileExtension, StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(SlnxFileExtension, StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(SlnfFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                solutionFile = Path.GetFullPath(path);
                rootDir = Path.GetDirectoryName(solutionFile);
            }
            else if (path.EndsWith(CsprojFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                return await ParseProjectAsSolutionAsync(path);
            }
        }

        if (solutionFile == null || rootDir == null) return null;

        var solution = SolutionFile.Parse(solutionFile);
        var projects = solution.ProjectsInOrder
            .Where(p => p.ProjectType != SolutionProjectType.SolutionFolder)
            .Select(proj => {
                var info = new ProjectInfo
                {
                    Name = proj.ProjectName,
                    Path = proj.AbsolutePath,
                    Id = proj.AbsolutePath,
                    IsRunnable = IsProjectRunnable(proj.AbsolutePath)
                };
                return info;
            }).ToList();

        CurrentSolution = new SolutionInfo(
            Path.GetFileNameWithoutExtension(solutionFile),
            solutionFile,
            projects,
            IsSlnx: solutionFile.EndsWith(SlnxFileExtension, StringComparison.OrdinalIgnoreCase),
            IsSlnf: solutionFile.EndsWith(SlnfFileExtension, StringComparison.OrdinalIgnoreCase));

        return CurrentSolution;
    }

    public static async Task<List<SolutionInfo>> DiscoverWorkspacesAsync(string rootPath)
    {
        return await Task.Run(() =>
        {
            var results = new ConcurrentBag<SolutionInfo>();
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5 };

            var ignoredDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bin", "obj", ".git", ".vs", ".vscode", "node_modules", "TestResults"
            };

            ScanDirectory(rootPath, 0);

            return results
                .OrderByDescending(w => IsSolution(w.Path))
                .ThenBy(w => GetDepth(rootPath, w.Path))
                .ThenBy(w => w.Name)
                .ToList();

            void ScanDirectory(string currentPath, int depth)
            {
                if (depth > options.MaxRecursionDepth) return;

                try
                {
                    ProcessFiles(currentPath, results);
                    ProcessSubDirectories(currentPath, depth, ignoredDirs, ScanDirectory);
                }
                catch (UnauthorizedAccessException)
                {
                    // Ignore inaccessible directories
                }
                catch (DirectoryNotFoundException)
                {
                    // Ignore missing directories
                }
            }
        });

        static void ProcessFiles(string currentPath, ConcurrentBag<SolutionInfo> results)
        {
            foreach (var file in Directory.GetFiles(currentPath, "*.*"))
            {
                var ext = Path.GetExtension(file).ToLower();
                switch (ext)
                {
                    case SlnFileExtension or SlnxFileExtension or SlnfFileExtension:
                        results.Add(new SolutionInfo(
                            Path.GetFileName(file),
                            file,
                            [],
                            IsSlnx: ext == SlnxFileExtension,
                            IsSlnf: ext == SlnfFileExtension));
                        break;
                    case CsprojFileExtension:
                        results.Add(new SolutionInfo(Path.GetFileName(file), file, []));
                        break;
                }
            }
        }

        static void ProcessSubDirectories(string currentPath, int depth, HashSet<string> ignoredDirs, Action<string, int> scanAction)
        {
            var subDirs = Directory.GetDirectories(currentPath);
            Parallel.ForEach(subDirs, subDir =>
            {
                var dirName = Path.GetFileName(subDir);
                if (!ignoredDirs.Contains(dirName))
                {
                    scanAction(subDir, depth + 1);
                }
            });
        }

        static bool IsSolution(string path) =>
            path.EndsWith(SlnFileExtension, StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(SlnxFileExtension, StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(SlnfFileExtension, StringComparison.OrdinalIgnoreCase);

        static int GetDepth(string root, string path)
        {
            var relative = Path.GetRelativePath(root, path);
            if (relative == "." || string.IsNullOrEmpty(relative)) return 0;
            return relative.Split(Path.DirectorySeparatorChar).Length;
        }
    }

    private Task<SolutionInfo?> ParseProjectAsSolutionAsync(string csprojPath)
    {
        var fullPath = Path.GetFullPath(csprojPath);
        var project = new ProjectInfo
        {
            Name = Path.GetFileNameWithoutExtension(fullPath),
            Path = fullPath,
            Id = fullPath,
            IsRunnable = IsProjectRunnable(fullPath)
        };

        CurrentSolution = new SolutionInfo(project.Name, fullPath, [project]);
        return Task.FromResult<SolutionInfo?>(CurrentSolution);
    }

    private static bool IsProjectRunnable(string projectPath)
    {
        try
        {
            // Fast heuristic: check file for OutputType or Web SDK without full MSBuild evaluation
            using var reader = new StreamReader(projectPath);
            var lineCount = 0;
            while (reader.ReadLine() is { } line && lineCount < 100)
            {
                lineCount++;
                if (line.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("<OutputType>WinExe</OutputType>", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Sdk=\"Microsoft.NET.Sdk.Web\"", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Sdk=\"Microsoft.NET.Sdk.Worker\"", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
