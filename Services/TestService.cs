using System.Text.RegularExpressions;
using System.Xml.Linq;
using CliWrap;

namespace lazydotnet.Services;

public class TestNode
{
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public List<TestNode> Children { get; } = [];
    public TestNode? Parent { get; set; }
    public bool IsContainer { get; set; }
    public bool IsTest { get; set; }
    public bool IsExpanded { get; set; } = true;
    public int Depth { get; set; }

    public TestStatus Status { get; set; } = TestStatus.None;
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public double Duration { get; set; }
}

public enum TestStatus
{
    None,
    Running,
    Passed,
    Failed
}

public class TestService
{
    public async Task<List<string>> DiscoverTestsAsync(string projectOrSolutionPath, CancellationToken cancellationToken = default)
    {
        var testNames = new List<string>();
        var stdOut = new List<string>();

        try
        {
            var command = Cli.Wrap("dotnet")
                .WithArguments($"test \"{projectOrSolutionPath}\" --list-tests")
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToDelegate(stdOut.Add))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(l => { }));

            await AppCli.RunAsync(command, cancellationToken).ConfigureAwait(false);

            bool startCapturing = false;
            foreach (var line in stdOut)
            {
                var trimmed = line.Trim();

                if (trimmed.Contains("The following Tests are available", StringComparison.OrdinalIgnoreCase))
                {
                    startCapturing = true;
                    continue;
                }

                if (startCapturing)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.StartsWith("Build started") ||
                        line.StartsWith("Determining projects") ||
                        line.Contains("Microsoft (R) Test Execution Command Line Tool"))
                    {
                        continue;
                    }

                    var cleanedName = Regex.Replace(trimmed, @"\(.*$", "");
                    cleanedName = cleanedName.Trim();

                    if (string.IsNullOrWhiteSpace(cleanedName)) continue;
                    if (cleanedName.Contains(' ')) continue;

                    testNames.Add(cleanedName);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
        }

        return [.. testNames.Distinct()];
    }

    public TestNode BuildTestTree(List<string> testNames)
    {
        var root = new TestNode { Name = "Tests", IsContainer = true, Depth = 0 };

        foreach (var test in testNames)
        {
            var parts = test.Split('.');
            var current = root;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var isLeaf = i == parts.Length - 1;

                var existingWrapper = current.Children.FirstOrDefault(c => c.Name == part);
                if (existingWrapper == null)
                {
                    existingWrapper = new TestNode
                    {
                        Name = part,
                        Parent = current,
                        Depth = current.Depth + 1,
                        IsContainer = !isLeaf,
                        IsTest = isLeaf,
                        FullName = isLeaf ? test : string.Join(".", parts.Take(i + 1))
                    };
                    current.Children.Add(existingWrapper);
                }
                current = existingWrapper;
            }
        }

        CompactTree(root);
        SortTree(root);
        RecalculateDepth(root, 0);

        return root;
    }

    private void CompactTree(TestNode node)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                CompactTree(node.Children[i]);
            }

            if (node.Children.Count == 1)
            {
                var child = node.Children[0];
                if (child.IsContainer && !child.IsTest)
                {
                    if (node.Depth > 0)
                    {
                        node.Name = $"{node.Name}.{child.Name}";
                        node.FullName = child.FullName;
                        node.Children.Clear();
                        node.Children.AddRange(child.Children);

                        foreach (var c in node.Children) c.Parent = node;

                        changed = true;
                    }
                }
            }
        }
    }

    private void RecalculateDepth(TestNode node, int depth)
    {
        node.Depth = depth;
        foreach (var c in node.Children) RecalculateDepth(c, depth + 1);
    }

    private void SortTree(TestNode node)
    {
        node.Children.Sort((a, b) => string.Compare(a.Name, b.Name));
        foreach(var child in node.Children) SortTree(child);
    }

    public async Task<List<TestResult>> RunTestAsync(string projectOrPath, string filterExpression)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
             var command = Cli.Wrap("dotnet")
                .WithArguments(args => args
                    .Add("test")
                    .Add(projectOrPath)
                    .Add("--filter")
                    .Add(filterExpression)
                    .Add("--results-directory")
                    .Add(tempDir)
                    .Add("--logger")
                    .Add("trx"))
                .WithValidation(CommandResultValidation.None);

             await AppCli.RunAsync(command).ConfigureAwait(false);

             var trxFiles = Directory.GetFiles(tempDir, "*.trx");
             var allResults = new List<TestResult>();

             foreach (var trxFile in trxFiles)
             {
                 allResults.AddRange(ParseTrx(trxFile));
             }

             return allResults;
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    private List<TestResult> ParseTrx(string trxPath)
    {
        var resultsList = new List<TestResult>();
        try
        {
            var doc = XDocument.Load(trxPath);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            var results = doc.Descendants(ns + "UnitTestResult").ToList();

            foreach (var r in results)
            {
                var testName = r.Attribute("testName")?.Value;
                var cleanedName = testName;
                if (cleanedName != null)
                {
                    cleanedName = Regex.Replace(cleanedName, @"\(.*$", "");
                }

                var tr = new TestResult { FullyQualifiedName = cleanedName };

                var outcomeAttr = r.Attribute("outcome")?.Value;
                var durationAttr = r.Attribute("duration")?.Value;

                if (TimeSpan.TryParse(durationAttr, out var ts))
                {
                    tr.Duration = ts.TotalMilliseconds;
                }

                tr.Outcome = outcomeAttr?.ToLower() == "passed" ? TestStatus.Passed : TestStatus.Failed;

                if (tr.Outcome == TestStatus.Failed)
                {
                    var output = r.Element(ns + "Output");
                    var errorInfo = output?.Element(ns + "ErrorInfo");
                    if (errorInfo != null)
                    {
                        tr.ErrorMessage = errorInfo.Element(ns + "Message")?.Value;
                        tr.StackTrace = errorInfo.Element(ns + "StackTrace")?.Value;
                    }
                }

                resultsList.Add(tr);
            }
        }
        catch (Exception)
        {
        }
        return resultsList;
    }
}

public class TestResult
{
    public string? FullyQualifiedName { get; set; }
    public TestStatus Outcome { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public double Duration { get; set; }
}
