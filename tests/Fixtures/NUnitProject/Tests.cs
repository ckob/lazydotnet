using NUnit.Framework;

namespace NUnitProject;

[TestFixture]
public class Tests
{
    [Test]
    public void PassingTest()
    {
        Assert.Pass();
    }

    [Test]
    public void FailingTest()
    {
        Assert.Fail("NUnit failing test.");
    }
}
