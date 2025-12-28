using System.Diagnostics;
using CliWrap;

namespace lazydotnet.Services;

public interface IEditorService
{
    Task OpenFileAsync(string filePath, int? lineNumber = null);
}

public class EditorService : IEditorService
{
    private enum EditorType
    {
        VsCodeStyle,
        ZedStyle
    }

    public async Task OpenFileAsync(string filePath, int? lineNumber = null)
    {
        var (command, type) = GetEditorInfo();
        var args = new List<string>();

        switch (type)
        {
            case EditorType.VsCodeStyle:
                args.AddRange(GetVsCodeStyleArgs(filePath, lineNumber));
                break;

            case EditorType.ZedStyle:
                var zedArgs = GetZedStyleArgs(filePath, lineNumber);
                args.AddRange(zedArgs);
                break;
            default:
                args.Add(lineNumber.HasValue ? $"{filePath}:{lineNumber}" : filePath);
                break;
        }

        try
        {
            await Cli.Wrap(command)
                .WithArguments(args)
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open editor '{command}': {ex.Message}");

            if (command != "open" && OperatingSystem.IsMacOS())
            {
                await Cli.Wrap("open").WithArguments(filePath).ExecuteAsync();
            }
        }
    }

    private static IEnumerable<string> GetVsCodeStyleArgs(string filePath, int? lineNumber)
    {
        if (lineNumber.HasValue)
        {
            yield return "--goto";
            yield return $"{filePath}:{lineNumber}";
        }
        else
        {
            yield return filePath;
        }
    }

    private static IEnumerable<string> GetZedStyleArgs(string filePath, int? lineNumber)
    {
        yield return lineNumber.HasValue ? $"{filePath}:{lineNumber}" : filePath;
    }

    private static (string Command, EditorType? Type) GetEditorInfo()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CURSOR_CLI")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CURSOR_AGENT")))
        {
            return ("cursor", EditorType.VsCodeStyle);
        }

        if (string.Equals(Environment.GetEnvironmentVariable("TERM_PROGRAM"), "zed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("ZED_TERM"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return ("zed", EditorType.ZedStyle);
        }

        if (string.Equals(Environment.GetEnvironmentVariable("TERM_PROGRAM"), "vscode", StringComparison.OrdinalIgnoreCase))
        {
            return ("code", EditorType.VsCodeStyle);
        }

        var editor = Environment.GetEnvironmentVariable("EDITOR");
        if (!string.IsNullOrEmpty(editor))
        {
            if (editor.Contains("cursor", StringComparison.OrdinalIgnoreCase) ||
                editor.Contains("code", StringComparison.OrdinalIgnoreCase))
            {
                return (editor, EditorType.VsCodeStyle);
            }
            if (editor.Contains("zed", StringComparison.OrdinalIgnoreCase))
            {
                return (editor, EditorType.ZedStyle);
            }
            return (editor, null);
        }

        return ("code", EditorType.VsCodeStyle);
    }
}
