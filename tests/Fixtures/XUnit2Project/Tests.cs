using Xunit;

namespace XUnit2Project;

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
        Assert.True(false, "This test is designed to fail.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TheoryTest(int value)
    {
        Assert.True(value > 0);
    }
}
