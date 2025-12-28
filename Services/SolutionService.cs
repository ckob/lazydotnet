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

        return new SolutionInfo(Path.GetFileNameWithoutExtension(slnFile), slnFile, projects);
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
}
