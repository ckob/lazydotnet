namespace lazydotnet.Services;

public class TestNode
{
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Uid { get; set; }
    public List<TestNode> Children { get; } = [];
    public TestNode? Parent { get; set; }
    public bool IsContainer { get; set; }
    public bool IsTest { get; set; }
    public bool IsTheoryContainer { get; set; }
    public bool IsExpanded { get; set; } = true;
    public int Depth { get; set; }
    public int TestCount { get; set; }

    public TestStatus Status { get; set; } = TestStatus.None;
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public double Duration { get; set; }
    public string? FilePath { get; set; }
    public int? LineNumber { get; set; }
}

public enum TestStatus
{
    None,
    Running,
    Passed,
    Failed
}

public class TestService(EasyDotnetService easyDotnetService)
{
    public async Task<List<DiscoveredTest>> DiscoverTestsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var results = await easyDotnetService.DiscoverTestsAsync(projectPath);
            var list = new List<DiscoveredTest>();
            await foreach (var item in results.WithCancellation(cancellationToken))
            {
                list.Add(item);
            }
            return list;
        }
        catch (Exception)
        {
            return [];
        }
    }

    public TestNode BuildTestTree(List<DiscoveredTest> tests)
    {
        var root = new TestNode { Name = "Tests", IsContainer = true, Depth = 0 };

        foreach (var test in tests)
        {
            // The Namespace field from easy-dotnet-server contains the FullyQualifiedName of the method
            var fqn = test.Namespace ?? test.Id;
            var parts = fqn.Split('.');
            var current = root;

            // Build hierarchy up to the class (all parts except the last one which is the method)
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var part = parts[i];
                var existing = current.Children.FirstOrDefault(c => c.Name == part && c.IsContainer);
                if (existing == null)
                {
                    existing = new TestNode
                    {
                        Name = part,
                        Parent = current,
                        Depth = current.Depth + 1,
                        IsContainer = true,
                        FullName = string.Join(".", parts.Take(i + 1))
                    };
                    current.Children.Add(existing);
                }
                
                // Track file path for container if not set
                if (string.IsNullOrEmpty(existing.FilePath))
                {
                    existing.FilePath = test.FilePath;
                    existing.LineNumber = test.LineNumber;
                }
                
                current = existing;
            }

            var methodName = parts[^1];
            var shortDisplayName = GetShortDisplayName(test.DisplayName, fqn);

            if (shortDisplayName == methodName)
            {
                // Simple test (Fact)
                var testNode = new TestNode
                {
                    Name = methodName,
                    Parent = current,
                    Depth = current.Depth + 1,
                    IsTest = true,
                    IsContainer = false,
                    FullName = fqn,
                    Uid = test.Id,
                    FilePath = test.FilePath,
                    LineNumber = test.LineNumber
                };
                current.Children.Add(testNode);
            }
            else
            {
                // Theory case
                // Find or create the Method container
                var methodContainer = current.Children.FirstOrDefault(c => c.Name == methodName && c.IsContainer);
                if (methodContainer == null)
                {
                    methodContainer = new TestNode
                    {
                        Name = methodName,
                        Parent = current,
                        Depth = current.Depth + 1,
                        IsContainer = true,
                        IsTheoryContainer = true,
                        IsExpanded = false,
                        FullName = fqn,
                        FilePath = test.FilePath,
                        LineNumber = test.LineNumber
                    };
                    current.Children.Add(methodContainer);
                }

                // Add the case as a child
                var caseNode = new TestNode
                {
                    Name = shortDisplayName,
                    Parent = methodContainer,
                    Depth = methodContainer.Depth + 1,
                    IsTest = true,
                    IsContainer = false,
                    FullName = fqn,
                    Uid = test.Id,
                    FilePath = test.FilePath,
                    LineNumber = test.LineNumber
                };
                methodContainer.Children.Add(caseNode);
            }
        }

        CompactTree(root);
        SortTree(root);
        RecalculateMetadata(root, 0);

        return root;
    }

    private int RecalculateMetadata(TestNode node, int depth)
    {
        node.Depth = depth;
        if (node.IsTest)
        {
            node.TestCount = 1;
        }
        else
        {
            node.TestCount = 0;
            foreach (var child in node.Children)
            {
                node.TestCount += RecalculateMetadata(child, depth + 1);
            }
        }
        return node.TestCount;
    }

    private static string GetShortDisplayName(string displayName, string fqn)
    {
        if (string.IsNullOrEmpty(displayName))
            return fqn;

        var methodName = fqn.Split('.').Last();

        // If displayName is exactly the FQN or exactly the methodName, it's a Fact
        if (displayName == fqn || displayName == methodName)
        {
            return methodName;
        }

        // If it's a Theory case, it usually looks like "MethodName(args)" or "Namespace.Class.MethodName(args)"
        if (displayName.StartsWith(fqn))
        {
            // Case: "Namespace.Class.MethodName(args)"
            var suffix = displayName[fqn.Length..];
            if (suffix.StartsWith('(')) return suffix;
        }
        
        if (displayName.StartsWith(methodName))
        {
            // Case: "MethodName(args)"
            var suffix = displayName[methodName.Length..];
            if (suffix.StartsWith('(')) return suffix;
        }

        // If DisplayName is already just the method name or something short, keep it
        if (!displayName.Contains('.'))
            return displayName;

        return displayName;
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
                // Only compact namespaces/containers that aren't tests or theories
                if (child.IsContainer && !child.IsTest && !child.IsTheoryContainer && !node.IsTheoryContainer)
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

    private void SortTree(TestNode node)
    {
        node.Children.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        foreach(var child in node.Children) SortTree(child);
    }

    public async Task<IAsyncEnumerable<TestRunResult>> RunTestsAsync(string projectPath, RunRequestNode[] filter)
    {
        // We need a configuration. Default to "Debug" or similar?
        // easy-dotnet-server might expect it.
        return await easyDotnetService.RunTestsAsync(projectPath, "Debug", filter);
    }
}
