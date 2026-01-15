using Microsoft.Build.Construction;

namespace lazydotnet.Services;

public record ProjectInfo
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Id { get; init; }
}

public record SolutionInfo(string Name, string Path, List<ProjectInfo> Projects);

public class SolutionService(EasyDotnetService easyDotnetService)
{
    public SolutionInfo? CurrentSolution { get; private set; }

    public Task<SolutionInfo?> FindAndParseSolutionAsync(string path)
    {
        string? slnFile = null;
        string? rootDir = null;

        if (Directory.Exists(path))
        {
            slnFile = Directory.GetFiles(path, "*.sln").FirstOrDefault();
            rootDir = Path.GetFullPath(path);
        }
        else if (File.Exists(path) && path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            slnFile = Path.GetFullPath(path);
            rootDir = Path.GetDirectoryName(slnFile);
        }

        if (slnFile == null || rootDir == null) return Task.FromResult<SolutionInfo?>(null);

        easyDotnetService.InitializeContext(rootDir, slnFile);

        var solution = SolutionFile.Parse(slnFile);
        var projects = solution.ProjectsInOrder
            .Where(p => p.ProjectType != SolutionProjectType.SolutionFolder)
            .Select(proj => new ProjectInfo
            {
                Name = proj.ProjectName,
                Path = proj.AbsolutePath,
                Id = proj.AbsolutePath
            }).ToList();

        CurrentSolution = new SolutionInfo(Path.GetFileNameWithoutExtension(slnFile), slnFile, projects);
        return Task.FromResult<SolutionInfo?>(CurrentSolution);
    }
}
