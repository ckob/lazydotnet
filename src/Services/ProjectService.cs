using Microsoft.Build.Evaluation;
using CliWrap;

namespace lazydotnet.Services;

public static class ProjectService
{
    public static async Task<List<string>> GetProjectReferencesAsync(string projectPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(projectPath))
                    return [];

                var projectCollection = new ProjectCollection();
                var project = projectCollection.LoadProject(projectPath);
                var references = project.GetItems("ProjectReference")
                    .Select(i => Path.GetFullPath(Path.Combine(project.DirectoryPath, i.EvaluatedInclude)))
                    .ToList();

                projectCollection.UnloadAllProjects();
                return references;
            }
            catch
            {
                return [];
            }
        });
    }

    public static async Task AddProjectReferenceAsync(string projectPath, string targetPath)
    {
        try
        {
            var command = Cli.Wrap("dotnet")
                .WithArguments(["add", projectPath, "reference", targetPath])
                .WithValidation(CommandResultValidation.None);

            await AppCli.RunAsync(command);
        }
        catch
        {
            // Ignore
        }
    }

    public static async Task RemoveProjectReferenceAsync(string projectPath, string targetPath)
    {
        try
        {
            var command = Cli.Wrap("dotnet")
                .WithArguments(["remove", projectPath, "reference", targetPath])
                .WithValidation(CommandResultValidation.None);

            await AppCli.RunAsync(command);
        }
        catch
        {
            // Ignore
        }
    }
}
