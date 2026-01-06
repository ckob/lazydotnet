using Spectre.Console;
using Spectre.Console.Rendering;

namespace lazydotnet.Core;

public sealed class Overlay : IRenderable
{
    private readonly IRenderable _background;
    private readonly IRenderable _foreground;

    public Overlay(IRenderable background, IRenderable foreground)
    {
        _background = background;
        _foreground = foreground;
    }

    public Measurement Measure(RenderOptions options, int maxWidth)
    {
        return _background.Measure(options, maxWidth);
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var backgroundSegments = _background.Render(options, maxWidth).ToList();
        var backgroundLines = Segment.SplitLines(backgroundSegments).ToList();

        // Measure foreground to find its natural size
        var measurement = _foreground.Measure(options, maxWidth);
        int fgWidth = Math.Min(measurement.Max, maxWidth);
        
        var foregroundSegments = _foreground.Render(options, fgWidth).ToList();
        var foregroundLines = Segment.SplitLines(foregroundSegments).ToList();

        int bgHeight = backgroundLines.Count;
        int fgHeight = foregroundLines.Count;

        if (fgHeight == 0) return backgroundSegments;

        int startY = Math.Max(0, (bgHeight - fgHeight) / 2);
        int endY = Math.Min(bgHeight, startY + fgHeight);

        var result = new List<Segment>();

        for (int y = 0; y < bgHeight; y++)
        {
            if (y >= startY && y < endY)
            {
                var fgLine = foregroundLines[y - startY];
                var bgLine = backgroundLines[y];

                // Calculate horizontal centering
                int bgWidth = bgLine.Sum(s => s.CellCount());
                int startX = Math.Max(0, (bgWidth - fgWidth) / 2);

                result.AddRange(OverlayLine(bgLine, fgLine, startX, fgWidth));
            }
            else
            {
                result.AddRange(backgroundLines[y]);
            }
            result.Add(Segment.LineBreak);
        }

        return result;
    }

    private static IEnumerable<Segment> OverlayLine(List<Segment> bgLine, List<Segment> fgLine, int startX, int fgWidth)
    {
        var prefix = TakeCells(bgLine, startX);
        var suffix = SkipCells(bgLine, startX + fgWidth);

        foreach (var s in prefix) yield return s;
        foreach (var s in fgLine) yield return s;
        foreach (var s in suffix) yield return s;
    }

    private static List<Segment> TakeCells(List<Segment> line, int count)
    {
        var result = new List<Segment>();
        int current = 0;
        foreach (var segment in line)
        {
            int remaining = count - current;
            if (remaining <= 0) break;

            if (segment.CellCount() <= remaining)
            {
                result.Add(segment);
                current += segment.CellCount();
            }
            else
            {
                // Split segment (approximate)
                int take = Math.Min(remaining, segment.Text.Length);
                result.Add(new Segment(segment.Text.Substring(0, take), segment.Style));
                current += take;
            }
        }
        // Pad with spaces if bg line was shorter than startX
        if (current < count)
        {
            result.Add(new Segment(new string(' ', count - current)));
        }
        return result;
    }

    private static List<Segment> SkipCells(List<Segment> line, int count)
    {
        var result = new List<Segment>();
        int current = 0;
        foreach (var segment in line)
        {
            int cellCount = segment.CellCount();
            if (current + cellCount <= count)
            {
                current += cellCount;
                continue;
            }

            int toSkip = count - current;
            if (toSkip > 0)
            {
                int skip = Math.Min(toSkip, segment.Text.Length);
                result.Add(new Segment(segment.Text.Substring(skip), segment.Style));
                current += cellCount;
            }
            else
            {
                result.Add(segment);
            }
        }
        return result;
    }
}
