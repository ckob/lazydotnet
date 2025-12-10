using Microsoft.Build.Construction;

namespace lazydotnet.Services;

public record ProjectInfo
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string Id { get; init; } = "";
    public string TypeGuid { get; init; } = "";
    public string? ParentId { get; set; }
    

    public bool IsSolutionFolder => TypeGuid.Equals("{2150E333-8FDC-42A3-9474-1A3956D46DE8}", StringComparison.OrdinalIgnoreCase) 
                                 || TypeGuid.Equals("SolutionFolder", StringComparison.OrdinalIgnoreCase);
}

public record SolutionInfo(string Name, string Path, List<ProjectInfo> Projects);

public class SolutionService
{
    public Task<SolutionInfo?> FindAndParseSolutionAsync(string directory)
    {
        var slnFile = Directory.GetFiles(directory, "*.sln").FirstOrDefault();
        if (slnFile == null) return Task.FromResult<SolutionInfo?>(null);


        var solution = SolutionFile.Parse(slnFile);
        
        var projects = new List<ProjectInfo>();

        foreach (var proj in solution.ProjectsInOrder)
        {

            bool isSolutionFolder = proj.ProjectType == SolutionProjectType.SolutionFolder;
            string extension = System.IO.Path.GetExtension(proj.AbsolutePath);
            bool isProjectFile = extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase); // csproj, fsproj, etc.

            if (!isSolutionFolder && !isProjectFile)
            {
                continue; 
            }


            projects.Add(new ProjectInfo
            {
                Name = proj.ProjectName,
                Path = proj.AbsolutePath,
                Id = proj.ProjectGuid,
                TypeGuid = proj.ProjectType.ToString(), 
                ParentId = proj.ParentProjectGuid
            });
        }

        return Task.FromResult<SolutionInfo?>(new SolutionInfo(Path.GetFileNameWithoutExtension(slnFile), slnFile, projects));
    }

    public Task<List<string>> GetProjectReferencesAsync(string projectPath)
    {
        var references = new List<string>();
        
        try
        {
            if (!File.Exists(projectPath))
                return Task.FromResult(references);

            var doc = System.Xml.Linq.XDocument.Load(projectPath);
            var projectRefs = doc.Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => e.Attribute("Include")?.Value)
                .Where(v => v != null);

            foreach (var refPath in projectRefs)
            {
                var projectName = Path.GetFileNameWithoutExtension(refPath);
                references.Add(projectName!);
            }
        }
        catch
        {
        }
        
        return Task.FromResult(references);
    }
}
