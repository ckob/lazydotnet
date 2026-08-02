using lazydotnet.Services;

namespace lazydotnet.UiTests;

public static class TestData
{
    public static ProjectInfo Project(string name, string path, bool runnable = false) => new()
    {
        Name = name,
        Path = path,
        Id = name,
        IsRunnable = runnable
    };

    public static SolutionInfo Solution() => new(
        Name: "SampleSolution",
        Path: "/repo/SampleSolution.sln",
        Projects:
        [
            Project("SampleApp", "/repo/src/SampleApp/SampleApp.csproj", runnable: true),
            Project("SampleLibrary", "/repo/src/SampleLibrary/SampleLibrary.csproj"),
            Project("SampleApp.Tests", "/repo/tests/SampleApp.Tests/SampleApp.Tests.csproj")
        ]);

    public static TestNode FailedTest()
    {
        var node = new TestNode
        {
            Name = "Add_TwoNumbers_ReturnsSum",
            FullName = "SampleApp.Tests.CalculatorTests.Add_TwoNumbers_ReturnsSum",
            IsTest = true,
            Status = TestStatus.Failed,
            Duration = 12,
            FilePath = "tests/SampleApp.Tests/CalculatorTests.cs",
            LineNumber = 21
        };
        node.Output.Add(new TestOutputLine("Expected: 5", Section: TestOutputSection.Error));
        node.Output.Add(new TestOutputLine("Actual:   4", Section: TestOutputSection.Error));
        node.Output.Add(new TestOutputLine("   at CalculatorTests.Add() in CalculatorTests.cs:line 21",
            "dim", TestOutputSection.Stack));
        node.Output.Add(new TestOutputLine("computing sum...", Section: TestOutputSection.Stdout));
        return node;
    }
}
