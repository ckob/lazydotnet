using System.Runtime.CompilerServices;

namespace lazydotnet.UiTests;

internal static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        // Store approved snapshots under tests/lazydotnet.UiTests/Snapshots/ regardless of which
        // directory the test runner is launched from. Baselines are committed; a mismatch fails the
        // test (Verify writes a *.received.txt next to it for review) — same model as netclaw's
        // approved screenshots, but on deterministic text instead of pixels.
        UseProjectRelativeDirectory("Snapshots");
    }
}
