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

    public async Task<SolutionInfo?> FindAndParseSolutionAsync(string path)
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

        if (slnFile == null || rootDir == null) return null;

        easyDotnetService.InitializeContext(rootDir, slnFile);

        var projectsResponse = await easyDotnetService.ListProjectsAsync(slnFile);

        var projects = projectsResponse
            .DistinctBy(p => p.AbsolutePath)
            .Select(proj => new ProjectInfo
        {
            Name = proj.ProjectName,
            Path = proj.AbsolutePath,
            Id = proj.AbsolutePath
        }).ToList();

        CurrentSolution = new SolutionInfo(Path.GetFileNameWithoutExtension(slnFile), slnFile, projects);
        return CurrentSolution;
    }

    public async Task<List<string>> GetProjectReferencesAsync(string projectPath)
    {
        try
        {
            if (!File.Exists(projectPath))
                return [];

            return await easyDotnetService.ListProjectReferencesAsync(projectPath);
        }
        catch
        {
            return [];
        }
    }

    public async Task<bool> AddProjectReferenceAsync(string projectPath, string targetPath)
    {
        return await easyDotnetService.AddProjectReferenceAsync(projectPath, targetPath);
    }

    public async Task<bool> RemoveProjectReferenceAsync(string projectPath, string targetPath)
    {
        return await easyDotnetService.RemoveProjectReferenceAsync(projectPath, targetPath);
    }
}
