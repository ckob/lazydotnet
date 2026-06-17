using System.ComponentModel;

namespace lazydotnet.Core.Configuration;

public interface ITabSettings
{
    bool Enabled { get; }
    int Position { get; }
}

public static class SettingDescriptions
{
    public const string TabEnabled = "Whether the tab is enabled and visible.";
    public const string TabPosition = "The 0-based position of the tab. Tabs are sorted by this value in ascending order.";
}

public record DetailsPaneSettings
{
    public ReferencesTabSettings ReferencesTab { get; init; } = new();
    public NuGetsTabSettings NuGetsTab { get; init; } = new();
    public TestsTabSettings TestsTab { get; init; } = new();
    public ExecutionTabSettings ExecutionTab { get; init; } = new();
}

public record ReferencesTabSettings : ITabSettings
{
    [Description(SettingDescriptions.TabEnabled)]
    [DefaultValue(true)]
    public bool Enabled { get; init; } = true;

    [Description(SettingDescriptions.TabPosition)]
    [DefaultValue(0)]
    public int Position { get; init; } = 0;
}

public record NuGetsTabSettings : ITabSettings
{
    [Description(SettingDescriptions.TabEnabled)]
    [DefaultValue(true)]
    public bool Enabled { get; init; } = true;

    [Description(SettingDescriptions.TabPosition)]
    [DefaultValue(1)]
    public int Position { get; init; } = 1;
}

public record TestsTabSettings : ITabSettings
{
    [Description(SettingDescriptions.TabEnabled)]
    [DefaultValue(true)]
    public bool Enabled { get; init; } = true;

    [Description(SettingDescriptions.TabPosition)]
    [DefaultValue(2)]
    public int Position { get; init; } = 2;
}

public record ExecutionTabSettings : ITabSettings
{
    [Description(SettingDescriptions.TabEnabled)]
    [DefaultValue(true)]
    public bool Enabled { get; init; } = true;

    [Description(SettingDescriptions.TabPosition)]
    [DefaultValue(3)]
    public int Position { get; init; } = 3;
}

public record LazydotnetSettings
{
    [Description("Settings related to the details pane.")]
    public DetailsPaneSettings DetailsPane { get; init; } = new();
}
