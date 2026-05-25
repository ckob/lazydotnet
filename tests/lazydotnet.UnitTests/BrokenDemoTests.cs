using FluentAssertions;

namespace lazydotnet.UnitTests;

// Demo: deliberately failing tests used to inspect the new modal UI.
// Run from lazydotnet, open the test, press Enter to view the modal.
public class BrokenDemoTests
{
    [Fact]
    public void Demo_SimpleAssertion_Fails()
    {
        Console.WriteLine("computing answer...");
        Console.WriteLine("step 1 done");
        Console.WriteLine("step 2 done");

        var expected = 42;
        var actual = 7;

        actual.Should().Be(expected, "the answer to life, universe, everything");
    }

    [Fact]
    public void Demo_StringDiff_Fails()
    {
        const string expected = "lazydotnet rocks";
        const string actual = "lazydotnet rocks!";

        actual.Should().Be(expected);
    }

    [Fact]
    public void Demo_DeepStack_Fails()
    {
        Level1();
    }

    [Fact]
    public void Demo_LongError_Fails()
    {
        var expected = new[] { 1, 2, 3, 4, 5 };
        var actual = new[] { 1, 2, 99, 4, 5 };

        actual.Should().Equal(expected);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(10, 20)]
    [InlineData(100, 200)]
    public void Demo_TheoryCase_Fails(int a, int b)
    {
        a.Should().Be(b);
    }

    private static void Level1() => Level2();
    private static void Level2() => Level3();
    private static void Level3() => throw new InvalidOperationException(
        "something went very wrong in a deeply nested call chain");
}
