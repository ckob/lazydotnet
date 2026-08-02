using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace lazydotnet.UiTests;

public static class TuiSnapshot
{
    public const int DefaultWidth = 100;
    public const int DefaultHeight = 30;

    public static string Render(IRenderable renderable, int width = DefaultWidth, int height = DefaultHeight)
    {
        var console = new TestConsole();
        console.Profile.Width = width;
        console.Profile.Height = height;
        console.Write(renderable);
        return Normalize(console.Output);
    }

    public static string RenderWithColor(IRenderable renderable, int width = DefaultWidth, int height = DefaultHeight)
    {
        var console = new TestConsole().EmitAnsiSequences();
        console.Profile.Width = width;
        console.Profile.Height = height;
        console.Write(renderable);
        return Normalize(console.Output);
    }

    private static string Normalize(string output) => output.Replace("\r\n", "\n").Replace('\\', '/');
}
