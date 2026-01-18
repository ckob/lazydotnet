namespace lazydotnet.Core;

public static class PathHelper
{
    private static readonly string CurrentDirectory = Directory.GetCurrentDirectory();

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
                return Path.GetRelativePath(CurrentDirectory, absolutePath);
            }
        }
        catch
        {
            // Fallback to absolute if relativity fails
        }

        return absolutePath;
    }
}
