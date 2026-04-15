using System.IO;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

public class RuleContentTests
{
    [Fact]
    public void PlanningRule_RequiresHarnessFirstFlow()
    {
        var rulePath = FindRulePath("00-planning-mode-harness-control.mdc");
        if (rulePath == null) return;

        var content = File.ReadAllText(rulePath);

        content.Should().Contain("start-session");
        content.Should().Contain("nextAction");
        content.Should().NotContain("HarnessRunManifest");
        content.Should().Contain("need_requirement_intent");
        content.Should().Contain("need_execution_plan");
    }

    [Fact]
    public void FailureRule_IncludesErrorHandling()
    {
        var rulePath = FindRulePath("01-planning-mode-failure-handling.mdc");
        if (rulePath == null) return;

        var content = File.ReadAllText(rulePath);

        content.Should().Contain("stop_with_error");
        content.Should().Contain("error");
    }

    [Fact]
    public void ExecutionRule_ForbidsWorkerSideRetrieval()
    {
        var rulePath = FindRulePath("02-execution-mode-worker-only.mdc");
        if (rulePath == null) return;

        var content = File.ReadAllText(rulePath);

        content.Should().Contain("long-term");
        content.Should().Contain("long-term");
    }

    [Fact]
    public void PlanningRule_RequiresExactWrapperPath()
    {
        var rulePath = FindRulePath("00-planning-mode-harness-control.mdc");
        if (rulePath == null) return;

        var content = File.ReadAllText(rulePath);
        content.Should().Contain(@"Scripts\invoke-harness-control-plane.ps1");
        content.Should().Contain("submit-step-result");
    }

    [Fact]
    public void PlanningRule_RequiresSubmitAfterEveryStage()
    {
        var rulePath = FindRulePath("00-planning-mode-harness-control.mdc");
        if (rulePath == null) return;

        var content = File.ReadAllText(rulePath);
        content.Should().Contain("submit-step-result");
        content.Should().Contain("submit it back");
    }

    [Fact]
    public void PlanningRule_RequiresStageOrder()
    {
        var rulePath = FindRulePath("00-planning-mode-harness-control.mdc");
        if (rulePath == null) return;

        var content = File.ReadAllText(rulePath);
        content.Should().Contain("need_requirement_intent");
        content.Should().Contain("need_retrieval_chunk_set");
        content.Should().Contain("need_retrieval_chunk_validation");
        content.Should().Contain("need_execution_plan");
        content.Should().Contain("need_worker_execution_packet");
    }

    [Fact]
    public void FailureRule_RequiresStopOnError()
    {
        var rulePath = FindRulePath("01-planning-mode-failure-handling.mdc");
        if (rulePath == null) return;

        var content = File.ReadAllText(rulePath);
        content.Should().Contain("stop_with_error");
        content.Should().Contain("STOP IMMEDIATELY");
    }

    [Fact]
    public void ExecutionRule_ProhibitsIndependentMemoryRetrieval()
    {
        var rulePath = FindRulePath("02-execution-mode-worker-only.mdc");
        if (rulePath == null) return;

        var content = File.ReadAllText(rulePath);
        content.Should().Contain("long-term memory");
        content.Should().Contain("DO NOT Retrieve");
    }

    [Fact]
    public void McpToolRule_RequiresExactToolMapping()
    {
        var rulePath = FindRulePath("03-planning-mode-mcp-stage.mdc");
        if (rulePath == null) return;

        var content = File.ReadAllText(rulePath);
        content.Should().Contain("retrieve_memory_by_chunks");
        content.Should().Contain("merge_retrieval_results");
        content.Should().Contain("build_memory_context_pack");
    }

    [Fact]
    public void PlanningRule_ProhibitsMcpBeforeInstruction()
    {
        var rulePath = FindRulePath("00-planning-mode-harness-control.mdc");
        if (rulePath == null) return;

        var content = File.ReadAllText(rulePath);
        content.Should().Contain("NEVER skip stages");
    }

    private string? FindRulePath(string fileName)
    {
        var baseDir = AppContext.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            var path = Path.Combine(baseDir, ".cursor", "rules", fileName);
            if (File.Exists(path))
                return path;
            
            var testPath = Path.Combine(baseDir, "..", "..", "..", ".cursor", "rules", fileName);
            if (File.Exists(testPath))
                return Path.GetFullPath(testPath);

            baseDir = Path.GetDirectoryName(baseDir) ?? "";
        }

        var harnessRoot = FindHarnessRoot();
        if (harnessRoot != null)
        {
            var rulePath = Path.Combine(harnessRoot, ".cursor", "rules", fileName);
            if (File.Exists(rulePath))
                return rulePath;
        }

        return null;
    }

    private string? FindHarnessRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var testFile = Path.Combine(current, "tests", "HarnessMcp.ControlPlane.Tests");
            if (Directory.Exists(testFile))
                return current;

            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return null;
    }
}