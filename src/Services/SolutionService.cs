using System.Collections.Concurrent;
using Microsoft.VisualStudio.SolutionPersistence;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace lazydotnet.Services;

public record ProjectInfo
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Id { get; init; }
    public bool IsRunnable { get; set; }
    public bool IsRunning { get; set; }
    public bool IsSolutionFolder { get; init; }
    public string? ParentId { get; init; }
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
        // Handle directories with multiple projects (no solution file)
        if (Directory.Exists(path))
        {
            var directoryResult = HandleDirectory(path);
            if (directoryResult != null)
                return directoryResult;
        }

        var (solutionFile, rootDir) = ResolveSolutionPath(path);
        if (solutionFile == null || rootDir == null) return null;

        // Handle single .csproj files directly
        if (solutionFile.EndsWith(CsprojFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return CreateSingleProjectSolution(solutionFile);
        }

        return await ParseSolutionFileAsync(solutionFile);
    }

    private static SolutionInfo? HandleDirectory(string path)
    {
        var solutionFile = Directory.GetFiles(path, $"*{SlnFileExtension}").FirstOrDefault()
                           ?? Directory.GetFiles(path, $"*{SlnxFileExtension}").FirstOrDefault()
                           ?? Directory.GetFiles(path, $"*{SlnfFileExtension}").FirstOrDefault();
        
        // If no solution file found, look for all .csproj files recursively
        if (solutionFile == null)
        {
            var projectFiles = Directory.GetFiles(path, $"*{CsprojFileExtension}", SearchOption.AllDirectories);
            return projectFiles.Length > 0 ? CreateMultiProjectSolution(path, projectFiles) : null;
        }
        
        return null;
    }

    private async Task<SolutionInfo?> ParseSolutionFileAsync(string solutionFile)
    {
        // For slnf files, we need to read the referenced solution file
        // but keep the original slnf path for the solution name
        string? originalSolutionFile = solutionFile;
        var slnfFilteredProjects = await ParseSlnfFilterAsync(solutionFile);
        
        if (slnfFilteredProjects != null && slnfFilteredProjects.Value.Path != null)
        {
            solutionFile = slnfFilteredProjects.Value.Path;
        }

        var serializer = SolutionSerializers.GetSerializerByMoniker(solutionFile);
        if (serializer == null)
            return null;

        var solution = await serializer.OpenAsync(solutionFile, CancellationToken.None);
        var solutionDirectory = Path.GetDirectoryName(solutionFile) ?? "";
        var projects = BuildProjectList(solution, solutionDirectory, slnfFilteredProjects?.FilteredPaths);

        var (isSlnx, isSlnf) = DetermineSolutionType(originalSolutionFile, solutionFile);
        
        CurrentSolution = new SolutionInfo(
            Path.GetFileNameWithoutExtension(originalSolutionFile ?? solutionFile),
            originalSolutionFile ?? solutionFile,
            projects,
            IsSlnx: isSlnx,
            IsSlnf: isSlnf);

        return CurrentSolution;
    }

    private static (bool IsSlnx, bool IsSlnf) DetermineSolutionType(string? originalSolutionFile, string solutionFile)
    {
        var isSlnf = originalSolutionFile?.EndsWith(SlnfFileExtension, StringComparison.OrdinalIgnoreCase) ?? 
                     solutionFile.EndsWith(SlnfFileExtension, StringComparison.OrdinalIgnoreCase);
        var isSlnx = !isSlnf && solutionFile.EndsWith(SlnxFileExtension, StringComparison.OrdinalIgnoreCase);
        return (isSlnx, isSlnf);
    }

    private static SolutionInfo CreateMultiProjectSolution(string directoryPath, string[] projectFiles)
    {
        var projects = projectFiles.Select(file => new ProjectInfo
        {
            Name = Path.GetFileNameWithoutExtension(file),
            Path = Path.GetFullPath(file),
            Id = Path.GetFullPath(file),
            IsRunnable = IsProjectRunnable(file)
        }).ToList();

        var directoryName = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) 
                            ?? "Projects";
        
        return new SolutionInfo(directoryName, directoryPath, projects);
    }

    private static SolutionInfo CreateSingleProjectSolution(string csprojPath)
    {
        var fullPath = Path.GetFullPath(csprojPath);
        var project = new ProjectInfo
        {
            Name = Path.GetFileNameWithoutExtension(fullPath),
            Path = fullPath,
            Id = fullPath,
            IsRunnable = IsProjectRunnable(fullPath)
        };

        return new SolutionInfo(project.Name, fullPath, [project]);
    }

    private static (string? SolutionFile, string? RootDir) ResolveSolutionPath(string path)
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
                    return (Path.GetFullPath(csproj), Path.GetDirectoryName(csproj));
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
                return (Path.GetFullPath(path), Path.GetDirectoryName(path));
            }
        }

        return (solutionFile, rootDir);
    }

    private static async Task<(string? Path, HashSet<string>? FilteredPaths)?> ParseSlnfFilterAsync(string solutionFile)
    {
        if (!solutionFile.EndsWith(SlnfFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var slnfContent = await File.ReadAllTextAsync(solutionFile);
            using var doc = System.Text.Json.JsonDocument.Parse(slnfContent);
            
            string? relativePath = null;
            HashSet<string>? filteredProjects = null;
            
            if (doc.RootElement.TryGetProperty("solution", out var solutionElement))
            {
                (relativePath, filteredProjects) = ExtractSlnfInfo(solutionElement);
            }
            
            if (!string.IsNullOrEmpty(relativePath))
            {
                var absolutePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(solutionFile) ?? "", relativePath));
                if (File.Exists(absolutePath))
                {
                    return (absolutePath, filteredProjects);
                }
            }
        }
        catch
        {
            // Ignore errors parsing slnf, continue with original file
        }

        return null;
    }

    private static (string? Path, HashSet<string>? FilteredProjects) ExtractSlnfInfo(System.Text.Json.JsonElement solutionElement)
    {
        string? relativePath = null;
        HashSet<string>? filteredProjects = null;
        
        if (solutionElement.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            relativePath = solutionElement.GetString();
        }
        else if (solutionElement.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (solutionElement.TryGetProperty("path", out var pathElement) ||
                solutionElement.TryGetProperty("relativePath", out pathElement))
            {
                relativePath = pathElement.GetString();
            }
            
            if (solutionElement.TryGetProperty("projects", out var projectsElement) &&
                projectsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                filteredProjects = projectsElement.EnumerateArray()
                    .Where(p => p.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(p => NormalizePath(p.GetString()!))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }
        
        return (relativePath, filteredProjects);
    }

    /// <summary>
    /// Normalizes a path for cross-platform comparison.
    /// Converts backslashes to forward slashes and removes any redundant separators.
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;
        
        // Replace backslashes with forward slashes for consistent comparison
        return path.Replace('\\', '/');
    }

    private static List<ProjectInfo> BuildProjectList(SolutionModel solution, string solutionDirectory, HashSet<string>? slnfFilteredProjects)
    {
        // First pass: identify which projects to include based on SLNF filter
        // Normalize paths for cross-platform comparison
        var filteredProjects = solution.SolutionProjects
            .Where(proj => slnfFilteredProjects == null || 
                          slnfFilteredProjects.Contains(NormalizePath(proj.FilePath)))
            .ToList();

        var requiredFolderIds = new HashSet<string>();
        foreach (var proj in filteredProjects)
        {
            MarkParentFolders(proj.Parent, requiredFolderIds);
        }

        var projects = new List<ProjectInfo>();

        // Add required folders
        projects.AddRange(
            solution.SolutionFolders
                .Where(folder => requiredFolderIds.Contains(folder.Id.ToString()))
                .Select(folder => new ProjectInfo
                {
                    Name = folder.Name ?? "Unnamed Folder",
                    Path = "",
                    Id = folder.Id.ToString(),
                    IsRunnable = false,
                    IsSolutionFolder = true,
                    ParentId = folder.Parent?.Id.ToString()
                }));

        // Add filtered projects - pass solution directory for path resolution
        projects.AddRange(filteredProjects.Select(proj => CreateProjectInfo(proj, solutionDirectory)));

        return projects;
    }

    private static void MarkParentFolders(SolutionItemModel? parent, HashSet<string> requiredFolderIds)
    {
        var currentParent = parent;
        while (currentParent != null)
        {
            requiredFolderIds.Add(currentParent.Id.ToString());
            currentParent = currentParent.Parent;
        }
    }

    private static ProjectInfo CreateProjectInfo(SolutionProjectModel proj, string solutionDirectory)
    {
        var parentId = proj.Parent?.Id.ToString();
        var filePath = ResolveProjectPath(proj.FilePath, solutionDirectory);
        var projectName = CalculateProjectName(proj, filePath);
        
        return new ProjectInfo
        {
            Name = projectName,
            Path = filePath,
            Id = string.IsNullOrEmpty(filePath) ? Guid.NewGuid().ToString() : filePath,
            IsRunnable = !string.IsNullOrEmpty(filePath) && IsProjectRunnable(filePath),
            IsSolutionFolder = false,
            ParentId = parentId
        };
    }

    /// <summary>
    /// Resolves a project path to an absolute path.
    /// If the path is relative, it's resolved against the solution directory.
    /// </summary>
    private static string ResolveProjectPath(string? projectPath, string solutionDirectory)
    {
        if (string.IsNullOrEmpty(projectPath))
            return "";
        
        // If already absolute, return as-is
        if (Path.IsPathRooted(projectPath))
            return Path.GetFullPath(projectPath);
        
        // Resolve relative path against solution directory
        var combinedPath = Path.Combine(solutionDirectory, projectPath);
        return Path.GetFullPath(combinedPath);
    }

    private static string CalculateProjectName(SolutionProjectModel proj, string filePath)
    {
        if (!string.IsNullOrEmpty(proj.DisplayName) && proj.DisplayName != "Unnamed Project")
        {
            return proj.DisplayName;
        }
        
        if (!string.IsNullOrEmpty(filePath))
        {
            return Path.GetFileNameWithoutExtension(filePath);
        }
        
        return "Unnamed Project";
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

    private static bool IsProjectRunnable(string projectPath)
    {
        try
        {
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
            // Ignore errors reading project file
            return false;
        }
    }
}
