using FluentAssertions;
using lazydotnet.Services;
using lazydotnet.UI.Components;

namespace lazydotnet.UnitTests;

public class TestDetailsReportTests
{
    private static TestNode MakeFailedTest()
    {
        var node = new TestNode
        {
            Name = "MyTest",
            FullName = "MyNamespace.MyClass.MyTest",
            IsTest = true,
            Status = TestStatus.Failed,
            Duration = 42,
            FilePath = "/abs/path/MyTest.cs",
            LineNumber = 17
        };
        node.Output.Add(new TestOutputLine("Expected: 1", Section: TestOutputSection.Error));
        node.Output.Add(new TestOutputLine("Actual:   2", Section: TestOutputSection.Error));
        node.Output.Add(new TestOutputLine("   at MyClass.MyTest() in MyTest.cs:line 17", "dim", TestOutputSection.Stack));
        node.Output.Add(new TestOutputLine("stdout line", Section: TestOutputSection.Stdout));
        return node;
    }

    [Fact]
    public void BuildErrorAndStack_FailedTest_ReturnsErrorThenStack_NoStdout()
    {
        var node = MakeFailedTest();

        var text = TestDetailsReport.BuildErrorAndStack(node);

        text.Should().Contain("Expected: 1");
        text.Should().Contain("Actual:   2");
        text.Should().Contain("at MyClass.MyTest()");
        text.Should().NotContain("stdout line");

        var idxError = text.IndexOf("Expected: 1", StringComparison.Ordinal);
        var idxStack = text.IndexOf("at MyClass.MyTest()", StringComparison.Ordinal);
        idxError.Should().BeLessThan(idxStack);
    }

    [Fact]
    public void BuildErrorAndStack_PassedTestWithoutOutput_ReturnsEmpty()
    {
        var node = new TestNode
        {
            Name = "OkTest",
            FullName = "N.C.OkTest",
            IsTest = true,
            Status = TestStatus.Passed
        };

        var text = TestDetailsReport.BuildErrorAndStack(node);

        text.Should().BeEmpty();
    }

    [Fact]
    public void BuildErrorAndStack_IgnoresGenericAndStdoutSections()
    {
        var node = new TestNode { Name = "X", FullName = "X", IsTest = true, Status = TestStatus.Failed };
        node.Output.Add(new TestOutputLine("Run name: X", "dim"));
        node.Output.Add(new TestOutputLine("stdout", Section: TestOutputSection.Stdout));
        node.Output.Add(new TestOutputLine("boom", Section: TestOutputSection.Error));

        var text = TestDetailsReport.BuildErrorAndStack(node);

        text.Should().Be("boom");
    }

    [Fact]
    public void BuildMarkdownReport_FailedTest_IncludesAllSections()
    {
        var node = MakeFailedTest();

        var report = TestDetailsReport.BuildMarkdownReport(node);

        report.Should().Contain("# MyNamespace.MyClass.MyTest");
        report.Should().Contain("- **Status:** Failed");
        report.Should().Contain("- **Duration:** 42ms");
        report.Should().Contain(":17");
        report.Should().Contain("## Error");
        report.Should().Contain("Expected: 1");
        report.Should().Contain("## Stack Trace");
        report.Should().Contain("at MyClass.MyTest()");
        report.Should().Contain("## Output");
        report.Should().Contain("stdout line");
    }

    [Fact]
    public void BuildMarkdownReport_FailedTest_SectionOrderErrorStackStdout()
    {
        var node = MakeFailedTest();

        var report = TestDetailsReport.BuildMarkdownReport(node);

        var idxError = report.IndexOf("## Error", StringComparison.Ordinal);
        var idxStack = report.IndexOf("## Stack Trace", StringComparison.Ordinal);
        var idxStdout = report.IndexOf("## Output", StringComparison.Ordinal);

        idxError.Should().BeGreaterThan(0);
        idxError.Should().BeLessThan(idxStack);
        idxStack.Should().BeLessThan(idxStdout);
    }

    [Fact]
    public void BuildMarkdownReport_ContainerNode_ShowsAggregateCounters()
    {
        var root = new TestNode { Name = "Class", FullName = "N.Class", IsContainer = true, TestCount = 3 };
        root.Children.Add(new TestNode { IsTest = true, Status = TestStatus.Passed });
        root.Children.Add(new TestNode { IsTest = true, Status = TestStatus.Passed });
        root.Children.Add(new TestNode { IsTest = true, Status = TestStatus.Failed });

        var report = TestDetailsReport.BuildMarkdownReport(root);

        report.Should().Contain("- **Total:** 3");
        report.Should().Contain("- **Passed:** 2");
        report.Should().Contain("- **Failed:** 1");
        report.Should().NotContain("- **Duration:**");
    }

    [Fact]
    public void BuildMarkdownReport_PassedTestNoOutput_OmitsSections()
    {
        var node = new TestNode
        {
            Name = "Ok",
            FullName = "N.C.Ok",
            IsTest = true,
            Status = TestStatus.Passed,
            Duration = 5
        };

        var report = TestDetailsReport.BuildMarkdownReport(node);

        report.Should().Contain("# N.C.Ok");
        report.Should().Contain("- **Status:** Passed");
        report.Should().NotContain("## Error");
        report.Should().NotContain("## Stack Trace");
        report.Should().NotContain("## Output");
    }

    [Fact]
    public void BuildMarkdownReport_FileWithoutLineNumber_OmitsLineSuffix()
    {
        var node = new TestNode
        {
            Name = "T",
            FullName = "N.C.T",
            IsTest = true,
            Status = TestStatus.Passed,
            FilePath = "/abs/T.cs",
            LineNumber = null
        };

        var report = TestDetailsReport.BuildMarkdownReport(node);

        report.Should().Contain("- **File:** `");
        report.Should().NotMatch("*- **File:** `*:*");
    }

    [Fact]
    public void TestOutputLine_DefaultSection_IsGeneric()
    {
        var line = new TestOutputLine("hello");

        line.Section.Should().Be(TestOutputSection.Generic);
        line.Style.Should().BeNull();
    }

    [Fact]
    public void TestOutputLine_NamedSection_Preserved()
    {
        var line = new TestOutputLine("err", "red", TestOutputSection.Error);

        line.Section.Should().Be(TestOutputSection.Error);
        line.Style.Should().Be("red");
    }

    [Fact]
    public void GetOutputSnapshot_ReturnsCopy()
    {
        var node = new TestNode();
        node.Output.Add(new TestOutputLine("a"));

        var snapshot = node.GetOutputSnapshot();
        node.Output.Add(new TestOutputLine("b"));

        snapshot.Should().HaveCount(1);
        node.GetOutputSnapshot().Should().HaveCount(2);
    }
}
