using System.Text.RegularExpressions;
using System.Xml.Linq;
using CliWrap;
using CliWrap.EventStream;

namespace lazydotnet.Services;

public class TestNode
{
    public string Name { get; set; } = string.Empty; // Just the leaf part (e.g. MethodName)
    public string FullName { get; set; } = string.Empty; // Fully qualified name
    public List<TestNode> Children { get; } = new();
    public TestNode? Parent { get; set; }
    public bool IsContainer { get; set; } // Namespace or Class
    public bool IsTest { get; set; }
    public bool IsExpanded { get; set; } = true;
    public int Depth { get; set; }
    
    // Status
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
        // Using --list-tests
        // Output typically looks like:
        // The following Tests are available:
        //     Namespace.Class.Test1
        //     Namespace.Class.Test2
        
        // We need to capture stdout
        var stdOut = new List<string>();

        try 
        {
            var command = Cli.Wrap("dotnet")
                .WithArguments($"test \"{projectOrSolutionPath}\" --list-tests")
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToDelegate(l => stdOut.Add(l)))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(l => { })); // Ignore stderr

            await AppCli.RunAsync(command, cancellationToken).ConfigureAwait(false);

            bool startCapturing = false;
            foreach (var line in stdOut)
            {
                var trimmed = line.Trim();
                
                // Typical header: "The following Tests are available:"
                if (trimmed.Contains("The following Tests are available", StringComparison.OrdinalIgnoreCase))
                {
                    startCapturing = true;
                    continue;
                }

                if (startCapturing)
                {
                    // Ignore empty lines or build info lines if they sneak in (though usually they are above)
                    // Tests are usually indented.
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Heuristic: ignore lines starting with common build output prefixes if they appear after header
                    if (line.StartsWith("Build started") || 
                        line.StartsWith("Determining projects") ||
                        line.Contains("Microsoft (R) Test Execution Command Line Tool"))
                    {
                        continue;
                    }

                    // Strip parameterized test arguments (e.g. "Theory(x: 1)")
                    // Regex to remove everything from the first '(' onwards
                    var cleanedName = Regex.Replace(trimmed, @"\(.*$", "");
                    cleanedName = cleanedName.Trim();

                    if (string.IsNullOrWhiteSpace(cleanedName)) continue;

                    // If it still has spaces, it's likely a build message or noise
                    // A valid fully qualified test name (Namespace.Class.Method) shouldn't have spaces
                    if (cleanedName.Contains(' ')) continue;

                    testNames.Add(cleanedName);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Propagate or handle? Usually propagate.
            throw;
        }
        catch (Exception)
        {
            // Fallback or ignore
        }

        return testNames.Distinct().ToList();
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
        // Recalculate depths after compaction
        RecalculateDepth(root, 0);
        
        return root;
    }

    private void CompactTree(TestNode node)
    {
        // Post-order traversal to compact from bottom up? 
        // Or top down. 
        // If I have A -> B -> C. 
        // A has 1 child B. B is container. Merge. A becomes "A.B". Child is C.
        // Then "A.B" has 1 child C. If C is container, merge? 
        // Yes.
        
        bool changed = true;
        while (changed)
        {
            changed = false;
            // Check if WE can merge into our SINGLE child? No, we merge the child into US.
            // But we need to iterate children first.
            
            // Let's iterate backwards safely
            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                 CompactTree(node.Children[i]);
            }

            // Now check if THIS node needs to merge with its single child
            if (node.Children.Count == 1)
            {
                var child = node.Children[0];
                if (child.IsContainer && !child.IsTest) // Only merge containers
                {
                    // Merge child into this node
                    if (node.Depth > 0) // Don't merge root "Tests" usually, or do we? 
                    {
                        // "Lidl" -> "Plus" becomes "Lidl.Plus"
                        node.Name = $"{node.Name}.{child.Name}";
                        node.FullName = child.FullName; // Use child's full name
                        node.Children.Clear();
                        node.Children.AddRange(child.Children);
                        
                        foreach (var c in node.Children) c.Parent = node;
                        
                        changed = true; 
                        // Since we changed, we might need to merge again if the new child is also single container
                        // The while loop handles this.
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
             // dotnet test [Path] --filter [Filter] --results-directory [TempDir] --logger "trx"
             // Using the passed filterExpression directly.

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
                .WithValidation(CommandResultValidation.None); // Don't throw on failed tests

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
                // Parse parameterized args if present to match our deduplicated names?
                // Actually, the TestNode.FullName corresponds to the "FullyQualifiedName".
                // In TRX, "testName" might be short name, "testId" etc.
                // We need the FullyQualifiedName to map back.
                
                // Usually <UnitTestResult testName="Namespace.Class.Method" ... />
                // But sometimes "Method (args)".
                // We stored "FullyQualifiedName" in TestNode. 
                // Let's hope TRX provides it distinctly? 
                // TRX structure: <UnitTestResult ...> <InnerTest > ... </UnitTestResult>
                // It's often in `testName` attribute but might need cleanup.
                
                // Actually, let's look for definitions. <TestDefinitions> <UnitTest name="..." storage="..." id="...">
                // The `executionId` links them.
                // But simpler: `testName` usually suffices if we clean it up same way we did for discovery.
                
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
            // Ignore parse errors or return partial
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
