using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace lazydotnet.UiTests;

/// <summary>
/// Renders a Spectre <see cref="IRenderable"/> to deterministic plain text so the TUI can be
/// snapshot-tested. <see cref="TestConsole"/> emits no ANSI/colour by default, and pinning the
/// profile width/height removes the only other source of non-determinism (terminal size), so the
/// captured string is stable across machines and OSes — unlike a pixel screenshot.
/// </summary>
public static class TuiSnapshot
{
    public const int DefaultWidth = 100;
    public const int DefaultHeight = 30;

    /// <summary>Render <paramref name="renderable"/> to plain text at a fixed terminal size.</summary>
    public static string Render(IRenderable renderable, int width = DefaultWidth, int height = DefaultHeight)
    {
        var console = new TestConsole();
        console.Profile.Width = width;
        console.Profile.Height = height;
        console.Write(renderable);
        return console.Output;
    }

    /// <summary>
    /// Render with ANSI colour/style sequences captured. Use only when colour is the thing under
    /// test (e.g. an error panel must be red) — the escape codes make the diff harder to read, so
    /// plain <see cref="Render"/> is preferred for layout assertions.
    /// </summary>
    public static string RenderWithColor(IRenderable renderable, int width = DefaultWidth, int height = DefaultHeight)
    {
        var console = new TestConsole().EmitAnsiSequences();
        console.Profile.Width = width;
        console.Profile.Height = height;
        console.Write(renderable);
        return console.Output;
    }
}
