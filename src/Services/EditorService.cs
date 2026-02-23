using System.Diagnostics;
using CliWrap;

namespace lazydotnet.Services;

public interface IEditorService
{
    string? RootPath { get; set; }
    Task OpenFileAsync(string filePath, int? lineNumber = null);
    (string Command, List<string> Args) GetEditorLaunchCommand(string filePath, int? lineNumber = null);
}

public class EditorService : IEditorService
{
    public string? RootPath { get; set; }

    private enum EditorType
    {
        VsCodeStyle,
        ZedStyle
    }

    public async Task OpenFileAsync(string filePath, int? lineNumber = null)
    {
        var (command, args) = GetEditorLaunchCommand(filePath, lineNumber);

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

    public (string Command, List<string> Args) GetEditorLaunchCommand(string filePath, int? lineNumber = null)
    {
        var (command, type) = GetEditorInfo();
        var args = new List<string>();

        if (type is EditorType.VsCodeStyle or EditorType.ZedStyle)
        {
            args.Add(RootPath ?? Directory.GetCurrentDirectory());
        }

        switch (type)
        {
            case EditorType.VsCodeStyle:
                args.AddRange(GetVsCodeStyleArgs(filePath, lineNumber));
                break;

            case EditorType.ZedStyle:
                args.AddRange(GetZedStyleArgs(filePath, lineNumber));
                break;
            default:
                args.Add(lineNumber.HasValue ? $"{filePath}:{lineNumber}" : filePath);
                break;
        }

        return (command, args);
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

        var antigravityAlias = Environment.GetEnvironmentVariable("ANTIGRAVITY_CLI_ALIAS");
        if (!string.IsNullOrEmpty(antigravityAlias))
        {
            return (antigravityAlias, EditorType.VsCodeStyle);
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
        if (string.IsNullOrEmpty(editor))
        {
            return ("code", EditorType.VsCodeStyle);
        }

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
}
