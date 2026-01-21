using Spectre.Console.Rendering;

namespace lazydotnet.Core;

public sealed class Overlay(IRenderable background, IRenderable foreground) : IRenderable
{
    public Measurement Measure(RenderOptions options, int maxWidth)
    {
        return background.Measure(options, maxWidth);
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var backgroundSegments = background.Render(options, maxWidth).ToList();
        var backgroundLines = Segment.SplitLines(backgroundSegments).ToList();

        var measurement = foreground.Measure(options, maxWidth);
        var fgWidth = Math.Min(measurement.Max, maxWidth);

        var foregroundSegments = foreground.Render(options, fgWidth).ToList();
        var foregroundLines = Segment.SplitLines(foregroundSegments).ToList();

        var bgHeight = backgroundLines.Count;
        var fgHeight = foregroundLines.Count;

        if (fgHeight == 0) return backgroundSegments;

        var startY = Math.Max(0, (bgHeight - fgHeight) / 2);
        var endY = Math.Min(bgHeight, startY + fgHeight);

        var result = new List<Segment>();

        for (var y = 0; y < bgHeight; y++)
        {
            if (y >= startY && y < endY)
            {
                var fgLine = foregroundLines[y - startY];
                var bgLine = backgroundLines[y];

                var bgWidth = bgLine.Sum(s => s.CellCount());
                var startX = Math.Max(0, (bgWidth - fgWidth) / 2);

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
        var current = 0;
        foreach (var segment in line)
        {
            var remaining = count - current;
            if (remaining <= 0) break;

            if (segment.CellCount() <= remaining)
            {
                result.Add(segment);
                current += segment.CellCount();
            }
            else
            {
                var take = Math.Min(remaining, segment.Text.Length);
                result.Add(new Segment(segment.Text[..take], segment.Style));
                current += take;
            }
        }
        if (current < count)
        {
            result.Add(new Segment(new string(' ', count - current)));
        }
        return result;
    }

    private static List<Segment> SkipCells(List<Segment> line, int count)
    {
        var result = new List<Segment>();
        var current = 0;
        foreach (var segment in line)
        {
            var cellCount = segment.CellCount();
            if (current + cellCount <= count)
            {
                current += cellCount;
                continue;
            }

            var toSkip = count - current;
            if (toSkip > 0)
            {
                var skip = Math.Min(toSkip, segment.Text.Length);
                result.Add(new Segment(segment.Text[skip..], segment.Style));
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
