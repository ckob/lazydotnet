namespace lazydotnet.Core;

public static class PathHelper
{
    public static string GetRelativePath(string? absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
        {
            return string.Empty;
        }

        try
        {
            if (Path.IsPathRooted(absolutePath))
            {
                var currentDirectory = Directory.GetCurrentDirectory();
                var relativePath = Path.GetRelativePath(currentDirectory, absolutePath);
                return relativePath == "."
                    ? Path.GetFileName(absolutePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                    : relativePath;
            }
        }
        catch
        {
            // Fallback to absolute if relativity fails
        }

        return absolutePath;
    }
}
