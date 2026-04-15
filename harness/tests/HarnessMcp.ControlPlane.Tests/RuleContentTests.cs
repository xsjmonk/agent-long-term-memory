using System.IO;
using FluentAssertions;
using Xunit;

namespace HarnessMcp.ControlPlane.Tests;

/// <summary>
/// Tests that verify the canonical skill/rule files exist with the correct names,
/// contain the required operational rules, and fail loudly when files are missing or misnamed.
///
/// Implementation is NOT complete until these tests pass.
/// </summary>
public class RuleContentTests
{
    // Canonical rule file names — must match exactly
    private static readonly string[] CanonicalRuleFiles =
    {
        "00-harness-control-plane.mdc",
        "01-harness-failure.mdc",
        "02-harness-execution.mdc",
        "03-harness-mcp-tool-calling.mdc",
        "04-harness-skill-activation.mdc"
    };

    // Old (stale) file names that must NOT exist
    private static readonly string[] StaleRuleFiles =
    {
        "00-planning-mode-harness-control.mdc",
        "01-planning-mode-failure-handling.mdc",
        "02-execution-mode-worker-only.mdc",
        "03-planning-mode-mcp-stage.mdc"
    };

    [Fact]
    public void AllCanonicalRuleFiles_Exist()
    {
        var harnessRoot = GetHarnessRootOrFail();
        foreach (var fileName in CanonicalRuleFiles)
        {
            var path = Path.Combine(harnessRoot, ".cursor", "rules", fileName);
            File.Exists(path).Should().BeTrue(
                $"canonical rule file '{fileName}' must exist at .cursor/rules/{fileName}");
        }
    }

    [Fact]
    public void StaleRuleFiles_DoNotExist()
    {
        var harnessRoot = GetHarnessRootOrFail();
        foreach (var fileName in StaleRuleFiles)
        {
            var path = Path.Combine(harnessRoot, ".cursor", "rules", fileName);
            File.Exists(path).Should().BeFalse(
                $"stale rule file '{fileName}' must be removed — use canonical names only");
        }
    }

    [Fact]
    public void PlanningRule_RequiresHarnessFirstFlow()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");

        content.Should().Contain("start-session");
        content.Should().Contain("nextAction");
        content.Should().NotContain("HarnessRunManifest");
        content.Should().Contain("need_requirement_intent");
        content.Should().Contain("need_execution_plan");
    }

    [Fact]
    public void PlanningRule_RequiresExactWrapperPath()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");

        content.Should().Contain(@"Scripts\invoke-harness-control-plane.ps1");
        content.Should().Contain("submit-step-result");
        content.Should().NotContain("HarnessMcp.AgentClient");
    }

    [Fact]
    public void PlanningRule_RequiresSubmitAfterEveryStage()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");

        content.Should().Contain("submit-step-result");
        content.Should().Contain("submit it back");
    }

    [Fact]
    public void PlanningRule_RequiresStageOrder()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");

        content.Should().Contain("need_requirement_intent");
        content.Should().Contain("need_retrieval_chunk_set");
        content.Should().Contain("need_retrieval_chunk_validation");
        content.Should().Contain("need_execution_plan");
        content.Should().Contain("need_worker_execution_packet");
    }

    [Fact]
    public void PlanningRule_ContainsNeverSkipStages()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");
        content.Should().Contain("NEVER skip stages");
    }

    [Fact]
    public void PlanningRule_ProhibitsMcpBeforeInstruction()
    {
        var content = ReadRuleOrFail("00-harness-control-plane.mdc");
        content.Should().Contain("MCP");
    }

    [Fact]
    public void FailureRule_IncludesHardStopOnError()
    {
        var content = ReadRuleOrFail("01-harness-failure.mdc");

        content.Should().Contain("stop_with_error");
        content.Should().Contain("STOP IMMEDIATELY");
        content.Should().Contain("error");
    }

    [Fact]
    public void FailureRule_ForbidsFreeFormFallback()
    {
        var content = ReadRuleOrFail("01-harness-failure.mdc");

        content.Should().Contain("stop_with_error");
        content.Should().Contain("NEVER");
    }

    [Fact]
    public void ExecutionRule_ForbidsWorkerSideRetrieval()
    {
        var content = ReadRuleOrFail("02-harness-execution.mdc");

        content.Should().Contain("long-term memory");
        content.Should().Contain("DO NOT Retrieve");
        content.Should().Contain("forbidden");
    }

    [Fact]
    public void ExecutionRule_ProhibitsIndependentMemoryRetrieval()
    {
        var content = ReadRuleOrFail("02-harness-execution.mdc");

        content.Should().Contain("long-term memory");
        content.Should().Contain("DO NOT Retrieve");
    }

    [Fact]
    public void McpToolRule_RequiresExactToolMapping()
    {
        var content = ReadRuleOrFail("03-harness-mcp-tool-calling.mdc");

        content.Should().Contain("retrieve_memory_by_chunks");
        content.Should().Contain("merge_retrieval_results");
        content.Should().Contain("build_memory_context_pack");
        content.Should().Contain("EXACTLY");
        content.Should().Contain("nextAction");
    }

    [Fact]
    public void McpToolRule_ProhibitsToolSubstitution()
    {
        var content = ReadRuleOrFail("03-harness-mcp-tool-calling.mdc");

        content.Should().Contain("NEVER");
        content.Should().Contain("retrieve_memory_by_chunks");
        content.Should().Contain("merge_retrieval_results");
        content.Should().Contain("build_memory_context_pack");
    }

    [Fact]
    public void ActivationSkill_DefinesSemanticActivation()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");

        content.Should().Contain("semantic");
        content.Should().Contain("planning intent");
        content.Should().Contain("activate");
    }

    [Fact]
    public void ActivationSkill_IncludesActivationExamples()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");

        // Must include activation scenario examples
        content.Should().Contain("design");
        content.Should().Contain("approach");
        content.Should().Contain("plan");
    }

    [Fact]
    public void ActivationSkill_IncludesNonActivationExamples()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");

        // Must describe what does NOT activate planning mode
        content.Should().Contain("trivial");
        content.Should().Contain("casual");
    }

    [Fact]
    public void ActivationSkill_ReferencesHarnessControlPlaneSKill()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");

        // Must tell agent to use the planning skill when activated
        content.Should().Contain("00-harness-control-plane");
    }

    [Fact]
    public void ActivationSkill_IsNotLexicalOnly()
    {
        var content = ReadRuleOrFail("04-harness-skill-activation.mdc");

        // Must explicitly say it is not just keyword matching
        content.Should().Contain("semantic");
        content.Should().Contain("not",
            "must state that activation is not keyword-only");
        content.Should().NotContain("if the user says the word 'plan'");
    }

    // --- Private helpers ---

    private static string ReadRuleOrFail(string fileName)
    {
        var path = GetRulePathOrFail(fileName);
        return File.ReadAllText(path);
    }

    private static string GetRulePathOrFail(string fileName)
    {
        var harnessRoot = GetHarnessRootOrFail();
        var path = Path.Combine(harnessRoot, ".cursor", "rules", fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required canonical rule file '{fileName}' not found at: {path}\n" +
                "Implementation is not complete until all canonical rule files exist.");
        return path;
    }

    private static string GetHarnessRootOrFail()
    {
        var root = FindHarnessRoot();
        if (root == null)
            throw new DirectoryNotFoundException(
                "Could not locate harness repository root. " +
                "Expected to find 'tests/HarnessMcp.ControlPlane.Tests' or 'src/HarnessMcp.ControlPlane' subdirectory.");
        return root;
    }

    private static string? FindHarnessRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "tests", "HarnessMcp.ControlPlane.Tests")))
                return current;
            if (Directory.Exists(Path.Combine(current, "src", "HarnessMcp.ControlPlane")))
                return current;
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return null;
    }
}
