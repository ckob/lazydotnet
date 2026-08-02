using System.Runtime.CompilerServices;

namespace lazydotnet.UiTests;

internal static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        UseProjectRelativeDirectory("Snapshots");
    }
}
