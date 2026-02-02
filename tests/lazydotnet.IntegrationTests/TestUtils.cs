using System.IO;

namespace lazydotnet.Tests;

public static class TestUtils
{
    private static string GetFixturesPath()
    {
        // Check if copied to output directory (directly or in Fixtures folder)
        var localFixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        if (Directory.Exists(localFixtures)) return localFixtures;

        // In the current build, they seem to be copied directly to BaseDirectory
        // based on the <None Include="..\Fixtures\**\*" ... /> glob
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "SimpleSolution.sln")))
            return AppContext.BaseDirectory;

        // Fallback for development/IDE
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Fixtures"));
    }

    public static string CopyFixture(string fixtureName, string targetDirectory)
    {
        var sourcePath = Path.Combine(GetFixturesPath(), fixtureName);
        var targetPath = Path.Combine(targetDirectory, fixtureName);

        if (File.Exists(sourcePath))
        {
            File.Copy(sourcePath, targetPath, true);
            return targetPath;
        }

        if (Directory.Exists(sourcePath))
        {
            CopyDirectory(sourcePath, targetPath);
            return targetPath;
        }

        throw new DirectoryNotFoundException($"Fixture {fixtureName} not found at {sourcePath}");
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

        var dirs = dir.GetDirectories();
        Directory.CreateDirectory(destinationDir);

        foreach (var file in dir.GetFiles())
        {
            var targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        foreach (var subDir in dirs)
        {
            var newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }

    public static string CreateTestProject(string directory, string projectName, string outputType = "Library", string sdk = "Microsoft.NET.Sdk")
    {
        var projectPath = Path.Combine(directory, $"{projectName}.csproj");
        var content = $@"<Project Sdk=""{sdk}"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputType>{outputType}</OutputType>
    <IsTestProject>{(projectName.Contains("Test") ? "true" : "false")}</IsTestProject>
  </PropertyGroup>
</Project>";
        File.WriteAllText(projectPath, content);
        return projectPath;
    }

    public static string CreateTestSolution(string directory, string solutionName, params string[] projectPaths)
    {
        var solutionPath = Path.Combine(directory, $"{solutionName}.sln");
        var content = new System.Text.StringBuilder();
        content.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        content.AppendLine("# Visual Studio Version 17");
        
        foreach (var path in projectPaths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var guid = Guid.NewGuid().ToString("B").ToUpper();
            content.AppendLine($@"Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""{name}"", ""{Path.GetRelativePath(directory, path)}"", ""{guid}""");
            content.AppendLine("EndProject");
        }
        
        File.WriteAllText(solutionPath, content.ToString());
        return solutionPath;
    }
}
