using Microsoft.Build.Construction;

namespace lazydotnet.Services;

public record ProjectInfo
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Id { get; init; }
}

public record SolutionInfo(string Name, string Path, List<ProjectInfo> Projects, bool IsSlnx = false, bool IsSlnf = false);

public class SolutionService
{
    public SolutionInfo? CurrentSolution { get; private set; }

    public async Task<SolutionInfo?> FindAndParseSolutionAsync(string path)
    {
        string? solutionFile = null;
        string? rootDir = null;

        if (Directory.Exists(path))
        {
            solutionFile = Directory.GetFiles(path, "*.sln").FirstOrDefault()
                           ?? Directory.GetFiles(path, "*.slnx").FirstOrDefault()
                           ?? Directory.GetFiles(path, "*.slnf").FirstOrDefault();
            rootDir = Path.GetFullPath(path);

            if (solutionFile == null)
            {
                var csproj = Directory.GetFiles(path, "*.csproj").FirstOrDefault();
                if (csproj != null)
                {
                    return await ParseProjectAsSolutionAsync(csproj);
                }
            }
        }
        else if (File.Exists(path))
        {
            if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase))
            {
                solutionFile = Path.GetFullPath(path);
                rootDir = Path.GetDirectoryName(solutionFile);
            }
            else if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return await ParseProjectAsSolutionAsync(path);
            }
        }

        if (solutionFile == null || rootDir == null) return null;

        var solution = SolutionFile.Parse(solutionFile);
        var projects = solution.ProjectsInOrder
            .Where(p => p.ProjectType != SolutionProjectType.SolutionFolder)
            .Select(proj => new ProjectInfo
            {
                Name = proj.ProjectName,
                Path = proj.AbsolutePath,
                Id = proj.AbsolutePath
            }).ToList();

        CurrentSolution = new SolutionInfo(
            Path.GetFileNameWithoutExtension(solutionFile),
            solutionFile,
            projects,
            IsSlnx: solutionFile.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase),
            IsSlnf: solutionFile.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase));

        return CurrentSolution;
    }

    private Task<SolutionInfo?> ParseProjectAsSolutionAsync(string csprojPath)
    {
        var fullPath = Path.GetFullPath(csprojPath);
        var project = new ProjectInfo
        {
            Name = Path.GetFileNameWithoutExtension(fullPath),
            Path = fullPath,
            Id = fullPath
        };

        CurrentSolution = new SolutionInfo(project.Name, fullPath, [project]);
        return Task.FromResult<SolutionInfo?>(CurrentSolution);
    }
}
