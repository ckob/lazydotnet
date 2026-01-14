using Spectre.Console;

namespace lazydotnet.UI.Components;

public static class SpinnerHelper
{
    private static readonly Spinner DefaultSpinner = Spinner.Known.Dots4;

    public static int GetCurrentFrameIndex(Spinner? spinner = null)
    {
        spinner ??= DefaultSpinner;
        var totalMs = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
        var intervalMs = spinner.Interval.TotalMilliseconds;
        if (intervalMs <= 0) intervalMs = 100;

        return (int)(totalMs / intervalMs % spinner.Frames.Count);
    }

    public static string GetFrame(Spinner? spinner = null)
    {
        spinner ??= DefaultSpinner;
        return spinner.Frames[GetCurrentFrameIndex(spinner)];
    }
}
