using Xunit;

namespace XUnit3Project;

public class Tests
{
    [Fact]
    public void PassingTest()
    {
        Assert.True(true);
    }

    [Fact]
    public void FailingTest()
    {
        Assert.True(false, "MTP failing test.");
    }

    [Fact]
    public void ErrorTest()
    {
        throw new System.Exception("MTP error test.");
    }
}
