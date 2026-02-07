using lazydotnet.Core;
using lazydotnet.Services;
using lazydotnet.UI.Components;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.UI;

public class WorkspacePane(SolutionService solutionService, string rootDir) : IKeyBindable
{
    public Action<string>? OnWorkspaceSelected { get; init; }
    public Action<Modal>? RequestModal { get; init; }
    public Action? RequestRefresh { get; init; }

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        yield return new KeyBinding("enter", "select workspace", OpenPickerAsync, k => k.Key == ConsoleKey.Enter);
    }

    private async Task OpenPickerAsync()
    {
        RequestRefresh?.Invoke();

        var picker = new WorkspacePickerModal(
            rootDir,
            path => OnWorkspaceSelected?.Invoke(path),
            () => RequestModal?.Invoke(null!),
            () => RequestRefresh?.Invoke()
        );

        RequestModal?.Invoke(picker);
        await Task.CompletedTask;
    }

    public IRenderable GetContent(bool isActive)
    {
        var current = solutionService.CurrentSolution;
        if (current == null) return new Markup("[dim]No workspace selected[/]");

        string icon;
        if (current.IsSlnx) icon = "[dodgerblue1]SLNX[/]";
        else if (current.IsSlnf) icon = "[cyan]SLNF[/]";
        else if (current.Path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)) icon = "[purple]SLN[/]";
        else icon = "[green]C#[/]";

        var markup = $" {icon} [bold]{Markup.Escape(current.Name)}[/]";
        if (isActive) return new Markup($"[black on blue]{Markup.Remove(markup)}[/]");
        return new Markup(markup);
    }
}
