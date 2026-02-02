using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MSTestProject;

[TestClass]
public class Tests
{
    [TestMethod]
    public void PassingTest()
    {
        Assert.IsTrue(true);
    }

    [TestMethod]
    public void FailingTest()
    {
        Assert.Fail("MSTest failing test.");
    }
}
