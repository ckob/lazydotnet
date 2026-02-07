using FluentAssertions;
using lazydotnet.Services;

namespace lazydotnet.UnitTests;

public class TestServiceTests
{
    [Fact]
    public void BuildTestTree_WithSimpleTests_ShouldReturnCorrectTree()
    {
        // Arrange
        var tests = new List<DiscoveredTest>
        {
            new("1", "Namespace", "Namespace.Class1.Test1", "Test1", "file1.cs", 10, "source1", false),
            new("2", "Namespace", "Namespace.Class1.Test2", "Test2", "file1.cs", 20, "source1", false),
            new("3", "Namespace", "Namespace.Class2.Test3", "Test3", "file2.cs", 30, "source1", false)
        };

        // Act
        var tree = TestService.BuildTestTree(tests);

        // Assert
        tree.Name.Should().Be("Tests");
        tree.Children.Should().HaveCount(1);
        tree.Children[0].Name.Should().Be("Namespace");
        tree.Children[0].Children.Should().HaveCount(2);
        tree.Children[0].Children.Select(c => c.Name).Should().Contain(["Class1", "Class2"]);
    }

    [Fact]
    public void BuildTestTree_WithTheory_ShouldCreateTheoryContainer()
    {
        // Arrange
        var tests = new List<DiscoveredTest>
        {
            new("1", "N", "N.C.T", "T(1)", "f.cs", 1, "s", false),
            new("2", "N", "N.C.T", "T(2)", "f.cs", 1, "s", false)
        };

        // Act
        var tree = TestService.BuildTestTree(tests);

        // Assert
        // Tree: Tests -> N.C -> T (Container) -> (1), (2) (Tests)
        var nc = tree.Children[0];
        var t = nc.Children[0];

        t.Name.Should().Be("T");
        t.IsTheoryContainer.Should().BeTrue();
        t.Children.Should().HaveCount(2);
        t.Children.Select(x => x.Name).Should().Contain(["(1)", "(2)"]);
    }

    [Fact]
    public void BuildTestTree_WithDotsInArguments_ShouldNotSplitArguments()
    {
        // Arrange
        // NUnit style FQN often includes arguments with dots
        var fqn = "Namespace.Class.TestMethod(\"1.2.3\")";
        var tests = new List<DiscoveredTest>
        {
            new("1", "Namespace", fqn, "TestMethod(\"1.2.3\")", "file.cs", 1, "source", false)
        };

        // Act
        var tree = TestService.BuildTestTree(tests);

        // Assert
        // Expected: Tests -> Namespace.Class -> TestMethod (Container) -> ("1.2.3") (Test)
        // OR if it's not detected as a theory container, it should at least be Namespace.Class -> TestMethod("1.2.3")

        var nc = tree.Children[0];
        nc.Name.Should().Be("Namespace.Class");
        nc.Children.Should().HaveCount(1);

        var testNode = nc.Children[0];
        // If it's a theory container:
        if (testNode.IsTheoryContainer)
        {
            testNode.Name.Should().Be("TestMethod");
            testNode.Children[0].Name.Should().Be("(\"1.2.3\")");
        }
        else
        {
            testNode.Name.Should().Be("TestMethod(\"1.2.3\")");
        }
    }

    [Fact]
    public void BuildTestTree_WithMultipleTheoryCasesInFqn_ShouldGroupThem()
    {
        // Arrange
        var tests = new List<DiscoveredTest>
        {
            new("1", "N", "N.C.T(\"a.b\")", "T(\"a.b\")", "f.cs", 1, "s", false),
            new("2", "N", "N.C.T(\"c.d\")", "T(\"c.d\")", "f.cs", 1, "s", false)
        };

        // Act
        var tree = TestService.BuildTestTree(tests);

        // Assert
        var nc = tree.Children[0];
        nc.Name.Should().Be("N.C");
        nc.Children.Should().HaveCount(1);

        var t = nc.Children[0];
        t.Name.Should().Be("T");
        t.IsTheoryContainer.Should().BeTrue();
        t.Children.Should().HaveCount(2);
        t.Children.Select(x => x.Name).Should().Contain(["(\"a.b\")", "(\"c.d\")"]);
    }
}
