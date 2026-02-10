using Microsoft.Build.Locator;

namespace lazydotnet.IntegrationTests;

public sealed class MSBuildFixture : IDisposable
{
    public MSBuildFixture()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    public void Dispose()
    {
    }
}

[CollectionDefinition("MSBuild")]
public class MSBuildCollection : ICollectionFixture<MSBuildFixture>
{
}
