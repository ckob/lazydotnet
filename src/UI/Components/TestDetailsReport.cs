using System.Text;
using lazydotnet.Core;
using lazydotnet.Services;

namespace lazydotnet.UI.Components;

public static class TestDetailsReport
{
    public static string BuildMarkdownReport(TestNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {node.FullName}");
        sb.AppendLine();
        sb.AppendLine($"- **Status:** {node.Status}");

        if (node.IsTest)
        {
            sb.AppendLine($"- **Duration:** {node.Duration}ms");
            if (node.FilePath != null)
            {
                var rel = PathHelper.GetRelativePath(node.FilePath);
                sb.AppendLine(node.LineNumber != null
                    ? $"- **File:** `{rel}:{node.LineNumber}`"
                    : $"- **File:** `{rel}`");
            }
        }
        else
        {
            sb.AppendLine($"- **Total:** {node.TestCount}");
            sb.AppendLine($"- **Passed:** {CountByStatus(node, TestStatus.Passed)}");
            sb.AppendLine($"- **Failed:** {CountByStatus(node, TestStatus.Failed)}");
        }

        var output = node.GetOutputSnapshot();
        AppendSection(sb, "Info", output.Where(o => o.Section == TestOutputSection.Generic));
        AppendSection(sb, "Error", output.Where(o => o.Section == TestOutputSection.Error));
        AppendSection(sb, "Stack Trace", output.Where(o => o.Section == TestOutputSection.Stack));
        AppendSection(sb, "Output", output.Where(o => o.Section == TestOutputSection.Stdout));

        return sb.ToString();
    }

    public static string BuildErrorAndStack(TestNode node)
    {
        var output = node.GetOutputSnapshot();
        var parts = output
            .Where(o => o.Section == TestOutputSection.Error || o.Section == TestOutputSection.Stack)
            .Select(o => o.Text);
        return string.Join(Environment.NewLine, parts);
    }

    private static void AppendSection(StringBuilder sb, string title, IEnumerable<TestOutputLine> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        sb.AppendLine("```");
        foreach (var l in list) sb.AppendLine(l.Text);
        sb.AppendLine("```");
    }

    private static int CountByStatus(TestNode node, TestStatus status)
    {
        if (node.IsTest) return node.Status == status ? 1 : 0;
        return node.Children.Sum(c => CountByStatus(c, status));
    }
}
